namespace RonVoice.Core.Speech;

public sealed class ModelNotFoundException(string message) : Exception(message);

/// <summary>
/// Acha a pasta do modelo Vosk. Os modelos não são versionados: são baixados
/// por tools/fetch-models.ps1 para data/models/.
/// </summary>
public static class ModelLocator
{
    /// <summary>Prefixo da pasta de cada idioma suportado.</summary>
    static readonly Dictionary<string, string> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "vosk-model-small-en",
        ["pt"] = "vosk-model-small-pt",
    };

    public static string Find(string language, string? modelsDir = null)
    {
        if (!Prefixes.TryGetValue(language, out var prefix))
            throw new ModelNotFoundException(
                $"idioma sem modelo configurado: {language} (suportados: {string.Join(", ", Prefixes.Keys)})");

        var dir = modelsDir ?? Path.Combine(AppContext.BaseDirectory, "data", "models");
        if (!Directory.Exists(dir))
            throw new ModelNotFoundException(
                $"pasta de modelos não encontrada: {dir}. Rode tools/fetch-models.ps1.");

        var hit = Directory.GetDirectories(dir)
            .FirstOrDefault(d => Path.GetFileName(d)
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (hit is null)
            throw new ModelNotFoundException(
                $"nenhum modelo de '{language}' em {dir} (esperado algo começando com '{prefix}'). "
                + "Rode tools/fetch-models.ps1.");

        if (!LooksLikeAModel(hit))
            throw new ModelNotFoundException(
                $"modelo em {hit} parece incompleto: nem o layout clássico (am/ + conf/) "
                + "nem o plano (final.mdl + mfcc.conf) foi encontrado");

        return hit;
    }

    /// <summary>
    /// Os modelos do Vosk vêm em dois formatos e ambos são válidos:
    /// o clássico do Kaldi, com as pastas am/ e conf/ (é o caso do en-us), e o
    /// plano, com final.mdl e mfcc.conf na raiz (é o caso do pt). Validar só um
    /// dos dois rejeitaria metade dos idiomas suportados.
    /// Serve para dar erro legível: sem isso a lib nativa aborta o processo.
    /// </summary>
    public static bool LooksLikeAModel(string modelDir)
    {
        if (!Directory.Exists(modelDir)) return false;

        var classic = Directory.Exists(Path.Combine(modelDir, "am"))
                   && Directory.Exists(Path.Combine(modelDir, "conf"));

        var flat = File.Exists(Path.Combine(modelDir, "final.mdl"))
                && File.Exists(Path.Combine(modelDir, "mfcc.conf"));

        return classic || flat;
    }

    /// <summary>Idioma inferido do nome da pasta, ou null se não reconhecido.</summary>
    public static string? LanguageOf(string modelDir)
    {
        var name = Path.GetFileName(modelDir.TrimEnd(Path.DirectorySeparatorChar));
        foreach (var (lang, prefix) in Prefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return lang;
        return null;
    }
}
