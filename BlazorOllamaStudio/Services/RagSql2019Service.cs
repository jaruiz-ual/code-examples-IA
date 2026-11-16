using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BlazorOllamaStudio.Data;
using Microsoft.EntityFrameworkCore;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace BlazorOllamaStudio.Services;

public class RagSql2019Service(
    IOllamaApiClient ollama,
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration)
{
    private string ModeloEmbeddings => configuration["Ollama:EmbeddingModel"] ?? "bge-m3";

    public async IAsyncEnumerable<string> ConsultarRagStreamingAsync(
        string pregunta,
        string modeloChat,
        string? imagenBase64 = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Vectorizar la pregunta del usuario
        var queryEmbed = await ollama.EmbedAsync(new EmbedRequest
        {
            Model = ModeloEmbeddings,
            Input = [pregunta]
        }, ct);
        float[] vectorPregunta = queryEmbed.Embeddings[0];

        // 2. Extraer palabras clave para búsqueda híbrida
        var palabrasClave = pregunta.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length > 3)
            .Select(p => p.Trim('?', '¿', '.', ',', ':', ';'))
            .ToList();

        // 3. Recuperar candidatos de SQL Server LocalDB y calcular similitud SIMD (.NET 10 nativo)
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var candidatos = await db.Fragmentos
            .AsNoTracking()
            .Select(f => new { f.NombreArchivo, f.NumeroPagina, f.Contenido, f.EmbeddingBytes })
            .ToListAsync(ct);

        var mejoresCoincidencias = candidatos
            .Select(c =>
            {
                var vec = MemoryMarshal.Cast<byte, float>(c.EmbeddingBytes);
                float scoreVectorial = TensorPrimitives.CosineSimilarity(vectorPregunta, vec);
                int exactas = palabrasClave.Count(k =>
                    c.Contenido.Contains(k, StringComparison.OrdinalIgnoreCase));
                float bonusTexto = Math.Min(0.15f, exactas * 0.05f);

                return new
                {
                    c.NombreArchivo,
                    c.NumeroPagina,
                    c.Contenido,
                    ScoreFinal = scoreVectorial + bonusTexto
                };
            })
            .Where(x => x.ScoreFinal > 0.58f)
            .OrderByDescending(x => x.ScoreFinal)
            .Take(4)
            .ToList();

        string systemPrompt = """
            Eres un asistente documental corporativo preciso y riguroso.
            Responde exclusivamente utilizando la información del CONTEXTO proporcionado.
            Al final de cada dato o afirmación relevante, cita entre paréntesis el documento y la página, ejemplo: (Manual.pdf, Pág. 3).
            Si la respuesta no se encuentra en el contexto, responde literalmente: "No dispongo de información sobre ese tema en los documentos cargados."
            """;

        string promptUsuario = pregunta;
        if (mejoresCoincidencias.Count > 0)
        {
            var contexto = string.Join("\n\n---\n\n",
                mejoresCoincidencias.Select(m =>
                    $"[Fuente: {m.NombreArchivo}, Pág. {m.NumeroPagina} | Relevancia: {m.ScoreFinal:P0}]\n{m.Contenido}"));

            promptUsuario = $"""
                CONTEXTO DOCUMENTAL:
                {contexto}

                PREGUNTA DEL USUARIO:
                {pregunta}
                """;
        }

        var request = new ChatRequest
        {
            Model = modeloChat,
            Messages =
            [
                new Message(ChatRole.System, systemPrompt),
                new Message
                {
                    Role = ChatRole.User,
                    Content = promptUsuario,
                    Images = string.IsNullOrEmpty(imagenBase64) ? null : [imagenBase64]
                }
            ]
        };

        await foreach (var chunk in ollama.ChatAsync(request, ct))
        {
            if (chunk?.Message?.Content is not null)
                yield return chunk.Message.Content;
        }
    }
}
