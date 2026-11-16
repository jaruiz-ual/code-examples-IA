using System.Security.Cryptography;
using OllamaSharp;
using OllamaSharp.Models;

namespace BlazorOllamaStudio.Services;

public class ModeloGgufService(IOllamaApiClient ollama)
{
    public async Task<List<string>> ObtenerModelosLocalesAsync()
    {
        var modelos = await ollama.ListLocalModelsAsync();
        return modelos.Select(m => m.Name).OrderBy(n => n).ToList();
    }

    public async IAsyncEnumerable<double> DescargarModeloAsync(string nombreModelo)
    {
        await foreach (var status in ollama.PullModelAsync(nombreModelo))
        {
            yield return status?.Percent ?? 0;
        }
    }

    public async IAsyncEnumerable<string> ImportarGgufAsync(
        Stream archivoStream,
        string nombreArchivo,
        string nombreModeloOllama,
        string systemPrompt)
    {
        var rutaTemp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{nombreArchivo}");
        try
        {
            await using (var fs = File.Create(rutaTemp))
                await archivoStream.CopyToAsync(fs);

            yield return "Calculando firma SHA256...";
            string digest;
            using (var sha256 = SHA256.Create())
            await using (var fsRead = File.OpenRead(rutaTemp))
            {
                var hash = await sha256.ComputeHashAsync(fsRead);
                digest = $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
            }

            yield return "Transfiriendo archivo GGUF a Ollama...";
            byte[] fileBytes = await File.ReadAllBytesAsync(rutaTemp);
            await ollama.PushBlobAsync(digest, fileBytes);

            yield return $"Creando modelo '{nombreModeloOllama}'...";
            var request = new CreateModelRequest
            {
                Model = nombreModeloOllama,
                Files = new Dictionary<string, string> { [nombreArchivo] = digest },
                System = systemPrompt,
                Parameters = new Dictionary<string, object> { ["temperature"] = 0.2 }
            };

            await foreach (var status in ollama.CreateModelAsync(request))
                yield return status?.Status ?? "Finalizando...";
        }
        finally
        {
            if (File.Exists(rutaTemp)) File.Delete(rutaTemp);
        }
    }
}
