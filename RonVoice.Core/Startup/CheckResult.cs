namespace RonVoice.Core.Startup;

public enum CheckStatus
{
    Ok,
    /// <summary>Funciona, mas com ressalva que vale dizer.</summary>
    Warning,
    /// <summary>Não vai funcionar enquanto isto não for resolvido.</summary>
    Failed,
}

public sealed record CheckResult(string Name, CheckStatus Status, string Message);

/// <param name="MicrophonePeak">
/// Pico de áudio medido enquanto a pessoa falava. Vem de fora porque quem grava
/// é a UI; assim a lógica continua testável sem hardware.
/// </param>
public sealed record CheckInputs(
    bool Elevated,
    bool ModelPresent,
    string Language,
    double MicrophonePeak,
    bool GameFound,
    bool InputIniFound);
