using RonVoice.Core.Config;

namespace RonVoice.Tests;

public class GameExecutableTests
{
    /// <summary>
    /// O nome do processo varia por loja. A versao Steam desta maquina chama-se
    /// ReadyOrNotSteam-Win64-Shipping, e assumir o nome padrao fez o app
    /// descartar todas as ordens em silencio ate isso ser descoberto.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Steam\ReadyOrNot\ReadyOrNotSteam-Win64-Shipping.exe",
                "ReadyOrNotSteam-Win64-Shipping")]
    [InlineData(@"D:\Epic\ReadyOrNot\ReadyOrNot-Win64-Shipping.exe",
                "ReadyOrNot-Win64-Shipping")]
    [InlineData(@"C:\Jogos\ReadyOrNot.exe", "ReadyOrNot")]
    public void DerivesTheProcessNameFromThePath(string path, string expected) =>
        Assert.Equal(expected, GameExecutable.ProcessNameOf(path));

    [Fact]
    public void AcceptsAPathWithoutTheExtension() =>
        Assert.Equal("ReadyOrNot", GameExecutable.ProcessNameOf(@"C:\Jogos\ReadyOrNot"));

    [Fact]
    public void EmptyPathThrows() =>
        Assert.Throws<ArgumentException>(() => GameExecutable.ProcessNameOf("  "));

    [Theory]
    [InlineData(@"C:\x\ReadyOrNotSteam-Win64-Shipping.exe", true)]
    [InlineData(@"C:\x\ReadyOrNot-Win64-Shipping.exe", true)]
    [InlineData(@"C:\x\readyornot.exe", true)]
    [InlineData(@"C:\x\chrome.exe", false)]
    [InlineData(@"C:\x\ReadyOrNotLauncher.exe", true)]
    public void RecognisesWhenThePathLooksLikeTheGame(string path, bool expected) =>
        Assert.Equal(expected, GameExecutable.LooksLikeReadyOrNot(path));
}
