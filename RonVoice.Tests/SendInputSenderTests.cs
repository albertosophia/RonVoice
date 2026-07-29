using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class SendInputSenderTests
{
    static readonly InputToken LShift = new ScanCodeToken(0x2A, false);

    static KeySequence Seq(params KeyStep[] steps) => new(steps);

    /// <summary>
    /// Token que o Emit não sabe converter. Faz o envio falhar num ponto exato,
    /// sem depender de relógio nem de Win32 de verdade.
    /// </summary>
    sealed record UnsendableToken : InputToken;

    [Fact]
    public void AFailedStepStillReleasesTheHeldModifier()
    {
        var sender = new SendInputSender(dryRun: true);
        var seq = Seq(
            new KeyStep(StepKind.Down, LShift, 0, 0),
            new KeyStep(StepKind.Press, new UnsendableToken(), 35, 0),
            new KeyStep(StepKind.Up, LShift, 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => sender.Send(seq));

        // Sem o finally, o LShift ficaria fisicamente descido no jogo: o go-code
        // segue engatado e, pela §5.3 do brief, cancela o menu de toda ordem
        // seguinte — em silêncio, até o jogador tocar no shift.
        Assert.Equal(new[] { "down scan 0x2A", "up   scan 0x2A" }, sender.Log);
    }

    [Fact]
    public void CancellationStillReleasesTheHeldModifier()
    {
        var sender = new SendInputSender(dryRun: true);
        // O gap de 400 ms no passo Down é a janela em que o cancelamento cai: o
        // Down já saiu (é a primeira coisa que Send faz, sem espera antes) e o
        // ThrowIfCancellationRequested da iteração seguinte ainda não rodou.
        var seq = Seq(
            new KeyStep(StepKind.Down, LShift, 0, 400),
            new KeyStep(StepKind.Press, new ScanCodeToken(0x02, false), 35, 0),
            new KeyStep(StepKind.Up, LShift, 0, 0));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(60);

        Assert.ThrowsAny<OperationCanceledException>(() => sender.Send(seq, cts.Token));
        Assert.Equal(new[] { "down scan 0x2A", "up   scan 0x2A" }, sender.Log);
    }

    [Fact]
    public void ASequenceThatCompletesReleasesTheModifierExactlyOnce()
    {
        // O contrapeso dos dois testes acima: o finally não pode soltar de novo
        // uma tecla que o passo Up já soltou.
        var map = CommandMap.Load(CommandMapTests.MapPath);
        var seq = new CommandResolver(map, new Dictionary<string, string>(StringComparer.Ordinal))
            .Resolve(new Intent(null, "door.open.flashbang", true));

        var sender = new SendInputSender(dryRun: true);
        sender.Send(seq);

        Assert.Equal(1, sender.Log.Count(l => l == "up   scan 0x2A"));
        Assert.Equal(1, sender.Log.Count(l => l == "down scan 0x2A"));
    }
}
