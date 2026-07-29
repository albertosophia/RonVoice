using System.IO.Compression;

namespace RonVoice.Core.Speech;

public sealed record ModelSpec(string Language, string DirectoryName, string Url, long Bytes);

/// <summary>
/// Baixa e instala modelos Vosk. A ordem — pasta temporária, valida, só então
/// move — não é preciosismo: a biblioteca nativa aborta o processo diante de um
/// modelo inválido em vez de lançar exceção, e o app fecharia sem mensagem
/// nenhuma, de novo a cada abertura.
/// </summary>
public static class ModelDownloader
{
    public static IReadOnlyDictionary<string, ModelSpec> Specs { get; } =
        new Dictionary<string, ModelSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("en", "vosk-model-small-en-us-0.15",
                "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip",
                41205931),
            ["pt"] = new("pt", "vosk-model-small-pt-0.3",
                "https://alphacephei.com/vosk/models/vosk-model-small-pt-0.3.zip",
                32453112),
        };

    public static async Task<string> DownloadAsync(
        ModelSpec spec, string modelsDir, IProgress<double>? progress, CancellationToken ct)
    {
        var zip = Path.Combine(Path.GetTempPath(), $"{spec.DirectoryName}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await http.GetAsync(
                       spec.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? spec.Bytes;

                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(zip);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(total > 0 ? (double)done / total : 0);
                }
            }

            return InstallFromZip(zip, modelsDir, spec);
        }
        finally
        {
            if (File.Exists(zip)) File.Delete(zip);
        }
    }

    /// <summary>
    /// Extrai para uma pasta temporária, valida, e só então substitui o destino.
    /// Falhando em qualquer ponto, o que já existia permanece intacto.
    /// </summary>
    public static string InstallFromZip(string zipPath, string modelsDir, ModelSpec spec)
    {
        Directory.CreateDirectory(modelsDir);
        var staging = Path.Combine(modelsDir, $".staging-{Guid.NewGuid():N}");

        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging);

            var extracted = Path.Combine(staging, spec.DirectoryName);
            if (!Directory.Exists(extracted))
            {
                // Alguns zips não trazem a pasta raiz com o nome esperado.
                var only = Directory.GetDirectories(staging);
                extracted = only.Length == 1 ? only[0] : staging;
            }

            if (!ModelLocator.LooksLikeAModel(extracted))
                throw new InvalidDataException(
                    $"o conteúdo baixado não é um modelo Vosk válido: {spec.Url}");

            var final = Path.Combine(modelsDir, spec.DirectoryName);
            if (Directory.Exists(final)) Directory.Delete(final, recursive: true);
            Directory.Move(extracted, final);
            return final;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
