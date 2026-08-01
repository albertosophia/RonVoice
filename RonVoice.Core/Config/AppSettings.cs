using RonVoice.Core.Commands;

namespace RonVoice.Core.Config;

public enum ListenModeSetting
{
    /// <summary>Padrão de fábrica: escuta sempre, com o portão de foco do jogo.</summary>
    AlwaysOn,
    /// <summary>Escuta só enquanto a tecla configurada estiver pressionada.</summary>
    PushToTalk,
}

/// <param name="MicrophoneDevice">
/// Posição na enumeração. Continua aqui só para as configurações antigas e como
/// desempate quando o nome não é achado — não é a fonte de verdade.
/// </param>
/// <param name="MicrophoneName">
/// O dispositivo pedido, por nome. É o que manda, porque a posição se desloca
/// quando um dispositivo entra ou sai — o que acontece ao entrar em VR, e faz o
/// app gravar do microfone errado sem erro nenhum.
/// </param>
/// <param name="SendMode">
/// Mailbox é o padrão e não tem interruptor na tela: o RonVoiceMod passou a ser
/// requisito. É o único caminho que alcança as 70 ordens — o RoNSpeech para nas
/// 32 e não tem como passar disso, porque o Windows acaba no F24 — e o único que
/// responde se o jogo agiu.
///
/// Os outros dois continuam no código e aceitam ser escolhidos editando este
/// arquivo à mão: RonSpeech como recuo enquanto o caminho novo não estiver
/// conferido em jogo, e Menu para depurar ou para quem jogue só na tela. Não são
/// oferecidos porque em VR o menu não funciona, e oferecer as três convidava a
/// escolher a que quebra.
/// </param>
public sealed record AppSettings(
    string Language,
    string? GameExecutablePath,
    int MicrophoneDevice,
    ListenModeSetting Mode,
    string? PushToTalkKey,
    double ConfidenceThreshold,
    string? MicrophoneName = null,
    SendMode SendMode = SendMode.Mailbox)
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
        ConfidenceThreshold: 0.0,
        MicrophoneName: null,
        SendMode: SendMode.Mailbox);
}
