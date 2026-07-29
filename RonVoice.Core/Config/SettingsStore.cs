using System.Text.Json;
using System.Text.Json.Serialization;

namespace RonVoice.Core.Config;

/// <summary>
/// Persiste as preferências ao lado do executável — é o que faz o modo portable
/// significar alguma coisa: copiar a pasta leva tudo junto.
/// </summary>
public static class SettingsStore
{
    const string FileName = "settings.json";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Ao lado do executável quando dá para gravar ali; senão %APPDATA%\RonVoice.
    /// O caso real do fallback é a pasta estar em Program Files.
    /// </summary>
    public static (string Path, bool Portable) ResolvePath(string exeDirectory)
    {
        if (IsWritable(exeDirectory))
            return (System.IO.Path.Combine(exeDirectory, FileName), true);

        var appData = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RonVoice");
        Directory.CreateDirectory(appData);
        return (System.IO.Path.Combine(appData, FileName), false);
    }

    public static (AppSettings Settings, string Path, bool Portable) Load(string? directory = null)
    {
        var dir = directory ?? AppContext.BaseDirectory;
        var (path, portable) = ResolvePath(dir);

        if (!File.Exists(path)) return (AppSettings.Default, path, portable);

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            return (loaded ?? AppSettings.Default, path, portable);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Arquivo corrompido não pode impedir o app de abrir: a correção é
            // pela própria tela, e sem abrir o usuário não tem como corrigir.
            return (AppSettings.Default, path, portable);
        }
    }

    public static void Save(AppSettings settings, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));

    static bool IsWritable(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return false;
            var probe = System.IO.Path.Combine(directory, $".ronvoice-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
