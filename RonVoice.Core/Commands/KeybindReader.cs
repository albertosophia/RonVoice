using System.Text.RegularExpressions;

namespace RonVoice.Core.Commands;

/// <summary>
/// Lê os binds reais do jogo. Devolve ActionName -> nome de tecla UE e nada mais:
/// não conhece ordens, não conhece MENU. Quem junta as pontas é o CommandResolver.
/// </summary>
public static partial class KeybindReader
{
    [GeneratedRegex(
        """^ActionMappings=\(ActionName="(?<action>[^"]+)".*?Key=(?<key>[A-Za-z0-9_]+)""",
        RegexOptions.Compiled)]
    private static partial Regex ActionMappingLine();

    /// <summary>Dispositivos que não nos interessam; ficam de fora do resultado.</summary>
    static readonly string[] NonDesktopPrefixes =
    [
        "Gamepad_", "OculusTouch_", "Vive_", "ValveIndex_", "MixedReality_",
        "MotionController_", "Daydream_", "SteamVR_", "HTC",
    ];

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var binds = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return binds;

        foreach (var line in File.ReadLines(path))
        {
            var m = ActionMappingLine().Match(line);
            if (!m.Success) continue;

            var key = m.Groups["key"].Value;
            if (key == "None") continue;                       // bind vazio: cai no default
            if (NonDesktopPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)))
                continue;

            // Uma ação pode aparecer várias vezes; vence o primeiro bind de desktop.
            binds.TryAdd(m.Groups["action"].Value, key);
        }
        return binds;
    }

    /// <summary>
    /// Windows/ é o caminho do UE5, usado pela versão atual do jogo.
    /// WindowsNoEditor/ é o do UE4, mantido para instalações antigas.
    /// </summary>
    public static string? FindDefaultIniPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(local, "ReadyOrNot", "Saved", "Config", "Windows", "Input.ini"),
            Path.Combine(local, "ReadyOrNot", "Saved", "Config", "WindowsNoEditor", "Input.ini"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
