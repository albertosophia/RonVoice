using RonVoice.Core.Input;

namespace RonVoice.Tests;

// IsGameForeground() em si chama GetForegroundWindow via P/Invoke e não dá pra
// testar sem o jogo (ou algum processo) de fato em foco. O que segue cobre
// ForegroundGuard.Matches, o predicado puro por trás dela — é ali que mora a
// regressão real: o jogo roda sob nomes de processo diferentes por loja
// (Steam: ReadyOrNotSteam-Win64-Shipping) e nenhuma lista fechada cobre todas.
public class ForegroundGuardTests
{
    [Theory]
    [InlineData("ReadyOrNotSteam-Win64-Shipping")]
    [InlineData("ReadyOrNot-Win64-Shipping")]
    [InlineData("ReadyOrNot")]
    [InlineData("readyornotsteam-win64-shipping")]   // sem diferenciar maiúsculas/minúsculas
    public void DefaultMatchingAcceptsEveryKnownGameProcessNameByPrefix(string processName) =>
        Assert.True(ForegroundGuard.Matches(processName));

    [Theory]
    [InlineData("chrome")]
    [InlineData("explorer")]
    [InlineData("notepad")]
    public void DefaultMatchingRejectsUnrelatedProcesses(string processName) =>
        Assert.False(ForegroundGuard.Matches(processName));

    [Fact]
    public void AnExplicitOverrideListIsHonoredInsteadOfThePrefix()
    {
        // --process aponta pro nome exato que o jogador viu no Gerenciador de
        // Tarefas: não deve se abrir para qualquer coisa que comece com
        // "ReadyOrNot" nem exigir esse prefixo.
        var overrideNames = new[] { "TotallyDifferentBuildName" };

        Assert.True(ForegroundGuard.Matches("TotallyDifferentBuildName", overrideNames));
        Assert.True(ForegroundGuard.Matches("totallydifferentbuildname", overrideNames));
        Assert.False(ForegroundGuard.Matches("ReadyOrNot-Win64-Shipping", overrideNames));
    }
}
