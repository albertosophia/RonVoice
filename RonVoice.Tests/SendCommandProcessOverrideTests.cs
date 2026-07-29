namespace RonVoice.Tests;

// Cobre Cli.StripExeSuffix, o parsing por trás de --process. Ver o comentário em
// SendCommandDelayTests.cs sobre por que a chamada é totalmente qualificada.
using CliArgs = RonVoice.Cli.Commands.Cli;

public class SendCommandProcessOverrideTests
{
    [Theory]
    [InlineData("ReadyOrNotSteam-Win64-Shipping", "ReadyOrNotSteam-Win64-Shipping")]
    [InlineData("ReadyOrNotSteam-Win64-Shipping.exe", "ReadyOrNotSteam-Win64-Shipping")]
    [InlineData("ReadyOrNotSteam-Win64-Shipping.EXE", "ReadyOrNotSteam-Win64-Shipping")]   // sem diferenciar maiúsculas/minúsculas
    public void StripExeSuffixRemovesTheOptionalExeExtension(string raw, string expected) =>
        Assert.Equal(expected, CliArgs.StripExeSuffix(raw));
}
