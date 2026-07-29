using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class SendInputSenderTests
{
    static readonly InputToken LShift = new ScanCodeToken(0x2A, false);

    static KeySequence Seq(params KeyStep[] steps) => new(steps);

    // Constantes do Win32 escritas à mão de propósito. Um teste que reusasse as
    // do próprio SendInputSender passaria mesmo se elas estivessem erradas — que
    // é exatamente a regressão silenciosa que estes testes existem para pegar.
    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_SCANCODE = 0x0008;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

    [Fact]
    public void AnOrdinaryKeyGoesOutAsAScanCodeWithNoVirtualKey()
    {
        // §5.1 do brief: o jogo lê via RawInput e ignora input mandado com
        // virtual key. Sem erro, sem sintoma — nada acontece no jogo. Trocar
        // wScan por wVk, ou perder KEYEVENTF_SCANCODE, deixaria o log em prosa
        // idêntico, então esta é a única asserção que acusaria a troca.
        var sender = new SendInputSender(dryRun: true);
        sender.Send(Seq(new KeyStep(StepKind.Press, new ScanCodeToken(0x41, false), 35, 0)));

        var down = sender.Events[0];
        var up = sender.Events[1];

        Assert.Equal(INPUT_KEYBOARD, down.Type);
        Assert.Equal((ushort)0, down.Vk);
        Assert.Equal((ushort)0x41, down.Scan);
        Assert.Equal(KEYEVENTF_SCANCODE, down.Flags);          // e nada mais

        Assert.Equal(INPUT_KEYBOARD, up.Type);
        Assert.Equal((ushort)0, up.Vk);
        Assert.Equal((ushort)0x41, up.Scan);
        Assert.Equal(KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, up.Flags);
    }

    [Fact]
    public void AnExtendedKeyAlsoCarriesTheExtendedFlag()
    {
        // PageUp é E0-prefixada e divide o scan 0x49 com o NumPad9. Sem
        // KEYEVENTF_EXTENDEDKEY o jogo recebe a outra tecla — de novo, sem erro.
        Assert.True(KeyCatalog.TryResolve("PageUp", out var pageUp));

        var sender = new SendInputSender(dryRun: true);
        sender.Send(Seq(new KeyStep(StepKind.Press, pageUp, 35, 0)));

        Assert.Equal((ushort)0, sender.Events[0].Vk);
        Assert.Equal((ushort)0x49, sender.Events[0].Scan);
        Assert.Equal(KEYEVENTF_SCANCODE | KEYEVENTF_EXTENDEDKEY, sender.Events[0].Flags);
        Assert.Equal(
            KEYEVENTF_SCANCODE | KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP,
            sender.Events[1].Flags);
    }

    [Fact]
    public void TheMenuClickGoesOutAsAMouseEventNotAKey()
    {
        var sender = new SendInputSender(dryRun: true);
        sender.Send(Seq(new KeyStep(StepKind.Press, new MouseToken(MouseButton.Middle), 100, 0)));

        Assert.Equal(INPUT_MOUSE, sender.Events[0].Type);
        Assert.Equal(MOUSEEVENTF_MIDDLEDOWN, sender.Events[0].Flags);
        Assert.Equal(INPUT_MOUSE, sender.Events[1].Type);
        Assert.Equal(MOUSEEVENTF_MIDDLEUP, sender.Events[1].Flags);
    }

    [Fact]
    public void NoKeyboardEventInTheWholeMapEverCarriesAVirtualKey()
    {
        // A varredura larga da regra 1: todas as 70 ordens × 4 estados de
        // elemento × 2 de fila. Os tempos vão a zero porque aqui interessa o
        // conteúdo do INPUT, não o relógio.
        var map = CommandMap.Load(CommandMapTests.MapPath);
        var resolver = new CommandResolver(map, new Dictionary<string, string>(StringComparer.Ordinal));

        var steps = new List<KeyStep>();
        foreach (var id in map.Orders.Keys)
            foreach (var element in new string?[] { null, "gold", "blue", "red" })
                foreach (var queue in new[] { false, true })
                    foreach (var step in resolver.Resolve(new Intent(element, id, queue)).Steps)
                        steps.Add(step with { HoldMs = 0, GapAfterMs = 0 });

        var sender = new SendInputSender(dryRun: true);
        sender.Send(new KeySequence(steps));

        Assert.NotEmpty(sender.Events);
        Assert.All(sender.Events, e =>
        {
            if (e.Type != INPUT_KEYBOARD) return;
            Assert.Equal((ushort)0, e.Vk);
            Assert.True((e.Flags & KEYEVENTF_SCANCODE) != 0, $"{e.Token} saiu sem KEYEVENTF_SCANCODE");
        });
    }

    [Fact]
    public void EveryPressHoldsTheKeyForItsWholeHold()
    {
        // §5.2 do brief: press-and-release no mesmo tick é perdido pelo jogo, e o
        // sintoma é "funciona 70% das vezes", pior que nunca funcionar. Uma
        // asserção sobre o tempo TOTAL da sequência não pega isso — passaria
        // igual se Send virasse Emit(down); Emit(up); Wait(hold). Só o intervalo
        // entre o down e o up de cada Press prova a regra.
        var map = CommandMap.Load(CommandMapTests.MapPath);
        var seq = new CommandResolver(map, new Dictionary<string, string>(StringComparer.Ordinal))
            .Resolve(new Intent("red", "door.open.flashbang", true));

        var sender = new SendInputSender(dryRun: true);
        sender.Send(seq);

        var i = 0;
        var presses = 0;
        foreach (var step in seq.Steps)
        {
            if (step.Kind != StepKind.Press) { i++; continue; }

            var heldMs = sender.Events[i + 1].AtMs - sender.Events[i].AtMs;
            Assert.True(
                heldMs >= step.HoldMs,
                $"passo {i}: segurou {heldMs:F1} ms, esperado ao menos {step.HoldMs} ms");
            presses++;
            i += 2;
        }

        // Um Press vira exatamente dois eventos e um Down/Up vira exatamente um:
        // se a contagem não fechar, o pareamento acima estava olhando para os
        // eventos errados e as asserções de hold não valiam nada.
        Assert.Equal(sender.Events.Count, i);
        Assert.Equal(5, presses);   // F7, MMB abre, 2, 2 envolvida, MMB fecha
    }

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
