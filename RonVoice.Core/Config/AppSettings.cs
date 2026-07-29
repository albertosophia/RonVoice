namespace RonVoice.Core.Config;

public enum ListenModeSetting
{
    /// <summary>Padrão de fábrica: escuta sempre, com o portão de foco do jogo.</summary>
    AlwaysOn,
    /// <summary>Escuta só enquanto a tecla configurada estiver pressionada.</summary>
    PushToTalk,
}

public sealed record AppSettings(
    string Language,
    string? GameExecutablePath,
    int MicrophoneDevice,
    ListenModeSetting Mode,
    string? PushToTalkKey,
    double ConfidenceThreshold)
{
    /// <summary>
    /// Sempre-ligado é o padrão por decisão do autor; PTT existe para quem
    /// preferir. O limiar nasce em 0 (desligado) porque depende de microfone,
    /// voz e ambiente — fixar um número seria inventá-lo.
    /// </summary>
    public static AppSettings Default { get; } = new(
        Language: "en",
        GameExecutablePath: null,
        MicrophoneDevice: 0,
        Mode: ListenModeSetting.AlwaysOn,
        PushToTalkKey: null,
        ConfidenceThreshold: 0.0);
}
