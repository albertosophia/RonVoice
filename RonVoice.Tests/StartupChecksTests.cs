using RonVoice.Core.Startup;

namespace RonVoice.Tests;

public class StartupChecksTests
{
    static CheckInputs AllGood() => new(
        Elevated: true, ModelPresent: true, Language: "en",
        MicrophonePeak: 0.5, GameFound: true, InputIniFound: true);

    static CheckResult Find(IReadOnlyList<CheckResult> r, string fragment) =>
        r.First(x => x.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void RunsFiveChecks() => Assert.Equal(5, StartupChecks.Run(AllGood()).Count);

    [Fact]
    public void EverythingGoodIsAllOk() =>
        Assert.All(StartupChecks.Run(AllGood()), c => Assert.Equal(CheckStatus.Ok, c.Status));

    /// <summary>
    /// Sem elevacao nenhuma tecla chega ao jogo e nao ha erro. E' falha, nao aviso.
    /// </summary>
    [Fact]
    public void NotElevatedIsAFailureAndSaysWhatToDo()
    {
        var check = Find(StartupChecks.Run(AllGood() with { Elevated = false }), "eleva");
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("administrador", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingModelIsAFailure()
    {
        var check = Find(StartupChecks.Run(AllGood() with { ModelPresent = false }), "modelo");
        Assert.Equal(CheckStatus.Failed, check.Status);
    }

    /// <summary>Silencio significa microfone, e' a distincao que evita a tarde perdida.</summary>
    [Fact]
    public void ASilentMicrophoneIsAFailureThatBlamesTheMicrophone()
    {
        var check = Find(StartupChecks.Run(AllGood() with { MicrophonePeak = 0.0 }), "microfone");
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("microfone", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AudioJustAboveTheFloorCounts()
    {
        var check = Find(
            StartupChecks.Run(AllGood() with { MicrophonePeak = StartupChecks.SilenceFloor + 0.01 }),
            "microfone");
        Assert.Equal(CheckStatus.Ok, check.Status);
    }

    [Fact]
    public void GameNotFoundIsAWarningBecauseTheAppStillOpens()
    {
        var check = Find(StartupChecks.Run(AllGood() with { GameFound = false }), "jogo");
        Assert.Equal(CheckStatus.Warning, check.Status);
        Assert.Contains("Configuração", check.Message);
    }

    /// <summary>
    /// Sem Input.ini o app usa keybind_defaults e funciona; so' quebra para quem
    /// remapeou. Aviso, nao falha.
    /// </summary>
    [Fact]
    public void MissingInputIniIsAWarningNotAFailure()
    {
        var check = Find(StartupChecks.Run(AllGood() with { InputIniFound = false }), "teclas");
        Assert.Equal(CheckStatus.Warning, check.Status);
    }

    [Fact]
    public void SummaryOfEverythingGoodTellsThemWhatToSay()
    {
        var summary = StartupChecks.Summarize(StartupChecks.Run(AllGood()));
        Assert.Contains("pronto", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stack up", summary);
    }

    [Fact]
    public void SummaryWithAFailureNamesWhatIsMissing()
    {
        var summary = StartupChecks.Summarize(
            StartupChecks.Run(AllGood() with { Elevated = false }));
        Assert.DoesNotContain("pronto", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("administrador", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningsAloneStillCountAsReady()
    {
        var summary = StartupChecks.Summarize(
            StartupChecks.Run(AllGood() with { InputIniFound = false }));
        Assert.Contains("pronto", summary, StringComparison.OrdinalIgnoreCase);
    }
}
