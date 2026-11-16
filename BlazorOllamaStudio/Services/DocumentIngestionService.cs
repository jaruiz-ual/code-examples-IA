using System.Text;
using System.Text.RegularExpressions;
using BlazorOllamaStudio.Data;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using OllamaSharp;
using OllamaSharp.Models;
using UglyToad.PdfPig;

namespace BlazorOllamaStudio.Services;

public class DocumentIngestionService(
    IOllamaApiClient ollama,
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration)
{
    private string ModeloEmbeddings => configuration["Ollama:EmbeddingModel"] ?? "bge-m3";

    public async Task IndexarDocumentoAsync(string nombreArchivo, Stream stream)
    {
        string extension = Path.GetExtension(nombreArchivo).ToLowerInvariant();

        List<(int Pagina, string Texto)> secciones = extension switch
        {
            ".pdf" => await ExtraerPdfPorPaginasAsync(stream),
            ".docx" => [(1, await ExtraerWordEstructuradoAsync(stream))],
            _ => [(1, await new StreamReader(stream).ReadToEndAsync())]
        };

        await using var db = await dbFactory.CreateDbContextAsync();

        // Reemplazar versión anterior si el archivo ya estaba indexado
        await db.Fragmentos
            .Where(f => f.NombreArchivo == nombreArchivo)
            .ExecuteDeleteAsync();

        foreach (var (pagina, textoLimpio) in secciones)
        {
            var chunks = FragmentarPorOraciones(textoLimpio, maxCaracteres: 850, solapamientoOraciones: 2);
            if (chunks.Count == 0) continue;

            var embedResponse = await ollama.EmbedAsync(new EmbedRequest
            {
                Model = ModeloEmbeddings,
                Input = chunks
            });

            for (int i = 0; i < chunks.Count; i++)
            {
                db.Fragmentos.Add(new DocumentoFragmento
                {
                    NombreArchivo = nombreArchivo,
                    NumeroPagina = pagina,
                    Contenido = chunks[i],
                    Embedding = embedResponse.Embeddings[i]
                });
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<(string NombreArchivo, int Fragmentos, DateTime Fecha)>> ListarDocumentosAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var resumen = await db.Fragmentos
            .GroupBy(f => f.NombreArchivo)
            .Select(g => new
            {
                NombreArchivo = g.Key,
                Fragmentos = g.Count(),
                Fecha = g.Max(x => x.FechaIndexado)
            })
            .OrderByDescending(x => x.Fecha)
            .ToListAsync();

        return resumen
            .Select(x => (x.NombreArchivo, x.Fragmentos, x.Fecha))
            .ToList();
    }

    public async Task EliminarDocumentoAsync(string nombreArchivo)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Fragmentos.Where(f => f.NombreArchivo == nombreArchivo).ExecuteDeleteAsync();
    }

    private static async Task<List<(int Pagina, string Texto)>> ExtraerPdfPorPaginasAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        var resultado = new List<(int, string)>();
        using var pdf = PdfDocument.Open(ms);
        foreach (var page in pdf.GetPages())
        {
            string limpio = Regex.Replace(page.Text, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(limpio))
                resultado.Add((page.Number, limpio));
        }
        return resultado;
    }

    private static async Task<string> ExtraerWordEstructuradoAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        var sb = new StringBuilder();
        using var wordDoc = WordprocessingDocument.Open(ms, false);
        var body = wordDoc.MainDocumentPart?.Document.Body;
        if (body is null) return string.Empty;

        string seccionActual = "General";

        foreach (var element in body.ChildElements)
        {
            if (element is Paragraph para)
            {
                string texto = para.InnerText.Trim();
                if (string.IsNullOrWhiteSpace(texto)) continue;

                string? estilo = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                if (estilo != null && (
                    estilo.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
                    estilo.StartsWith("Ttulo", StringComparison.OrdinalIgnoreCase) ||
                    estilo.StartsWith("Title", StringComparison.OrdinalIgnoreCase)))
                {
                    seccionActual = texto;
                    sb.AppendLine($"\n## {seccionActual}");
                }
                else
                {
                    sb.AppendLine($"[{seccionActual}] {texto}");
                }
            }
            else if (element is Table table)
            {
                foreach (var row in table.Elements<TableRow>())
                {
                    var celdas = row.Elements<TableCell>()
                        .Select(c => Regex.Replace(c.InnerText, @"\s+", " ").Trim())
                        .Where(t => !string.IsNullOrEmpty(t));
                    string fila = string.Join(" | ", celdas);
                    if (!string.IsNullOrWhiteSpace(fila))
                        sb.AppendLine($"[{seccionActual} - Tabla] {fila}");
                }
            }
        }
        return sb.ToString();
    }

    private static List<string> FragmentarPorOraciones(string texto, int maxCaracteres, int solapamientoOraciones)
    {
        var oraciones = Regex.Split(texto, @"(?<=[.!?])\s+|\r?\n+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var chunks = new List<string>();
        var buffer = new List<string>();
        int longitudActual = 0;

        foreach (var oracion in oraciones)
        {
            if (longitudActual + oracion.Length > maxCaracteres && buffer.Count > 0)
            {
                chunks.Add(string.Join(" ", buffer));
                buffer = buffer.TakeLast(solapamientoOraciones).ToList();
                longitudActual = buffer.Sum(s => s.Length);
            }
            buffer.Add(oracion);
            longitudActual += oracion.Length;
        }

        if (buffer.Count > 0)
            chunks.Add(string.Join(" ", buffer));

        return chunks;
    }
}
