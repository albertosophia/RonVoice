namespace RonVoice.Tests;

// Cobre só o parsing/validação de --delay (Cli.TryParseDelay). O countdown em
// si (Cli.CountdownDelay) dorme de verdade e escreve no console — não há nada
// determinístico ali pra testar sem reescrever o comando, o que o brief pediu
// pra evitar.
//
// Chamada totalmente qualificada de propósito: "Cli" é tanto o nome da classe
// de helpers (RonVoice.Cli.Commands.Cli) quanto o namespace raiz do projeto
// RonVoice.Cli, e a partir de RonVoice.Tests o namespace ganha da classe
// importada por `using` — um `using RonVoice.Cli.Commands;` aqui resolve
// "Cli.TryParseDelay" como uma busca de membro no namespace RonVoice.Cli, não
// como a classe, e o build quebra com CS0234.
using CliArgs = RonVoice.Cli.Commands.Cli;

public class SendCommandDelayTests
{
    [Fact]
    public void AbsentDelayDefaultsToZeroWithoutError()
    {
        var ok = CliArgs.TryParseDelay(["stack up"], out var seconds, out var error);

        Assert.True(ok);
        Assert.Equal(0, seconds);
        Assert.Null(error);
    }

    [Fact]
    public void AGoodValueParses()
    {
        var ok = CliArgs.TryParseDelay(["stack up", "--delay", "2.5"], out var seconds, out var error);

        Assert.True(ok);
        Assert.Equal(2.5, seconds);
        Assert.Null(error);
    }

    [Fact]
    public void AZeroValueIsAccepted()
    {
        var ok = CliArgs.TryParseDelay(["stack up", "--delay", "0"], out var seconds, out var error);

        Assert.True(ok);
        Assert.Equal(0, seconds);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("-0.5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("")]
    public void ABadValueIsRejectedWithAnErrorInsteadOfSilentlyBecomingZero(string raw)
    {
        var ok = CliArgs.TryParseDelay(["stack up", "--delay", raw], out var seconds, out var error);

        Assert.False(ok);
        Assert.Equal(0, seconds);
        Assert.NotNull(error);
    }
}
