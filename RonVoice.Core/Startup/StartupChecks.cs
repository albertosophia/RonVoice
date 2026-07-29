namespace RonVoice.Core.Startup;

/// <summary>
/// As cinco coisas que precisam estar certas, verificadas de uma vez e ditas em
/// português. Existem porque toda falha deste sistema é silenciosa: sem elas,
/// cada relato de "não funciona" vira uma conversa de quatro perguntas.
/// </summary>
public static class StartupChecks
{
    /// <summary>O mesmo piso do VoiceTestRunner, para os dois concordarem.</summary>
    public const double SilenceFloor = 0.02;

    public static IReadOnlyList<CheckResult> Run(CheckInputs i) =>
    [
        new("Elevação",
            i.Elevated ? CheckStatus.Ok : CheckStatus.Failed,
            i.Elevated
                ? "rodando como administrador"
                : "abra como administrador, senão as teclas não chegam ao jogo "
                  + "e não aparece erro nenhum"),

        new($"Modelo de voz ({i.Language})",
            i.ModelPresent ? CheckStatus.Ok : CheckStatus.Failed,
            i.ModelPresent
                ? "instalado"
                : $"o modelo de {i.Language} não está instalado"),

        new("Microfone",
            i.MicrophonePeak > SilenceFloor ? CheckStatus.Ok : CheckStatus.Failed,
            i.MicrophonePeak > SilenceFloor
                ? "captando som"
                : "não captei nenhum som. Confira o microfone escolhido na aba "
                  + "Configuração e o volume de entrada do Windows"),

        new("Jogo",
            i.GameFound ? CheckStatus.Ok : CheckStatus.Warning,
            i.GameFound
                ? "encontrado"
                : "não encontrei o Ready or Not. Escolha o executável na aba Configuração"),

        new("Teclas do jogo",
            i.InputIniFound ? CheckStatus.Ok : CheckStatus.Warning,
            i.InputIniFound
                ? "lidas do Input.ini"
                : "não achei o Input.ini; usando as teclas padrão. "
                  + "Se você remapeou algo no jogo, pode não funcionar"),
    ];

    public static string Summarize(IReadOnlyList<CheckResult> results)
    {
        var failed = results.Where(r => r.Status == CheckStatus.Failed).ToList();

        if (failed.Count == 0)
            return "Está pronto — abra o jogo e fale \"stack up\" mirando numa porta.";

        return "Falta resolver:\n"
             + string.Join('\n', failed.Select(f => $"  · {f.Name}: {f.Message}"));
    }
}
