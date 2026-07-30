using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

/// <summary>
/// A forma alternativa da sequência do menu, para VR: segurar a tecla do menu
/// durante a navegação em vez de clicar e soltar antes dos dígitos.
///
/// Em VR o menu abre e o teclado comprovadamente chega — uma ordem de tecla
/// pura funciona — e mesmo assim os dígitos não escolhem nada, com espera de
/// 60, 300 ou 800 ms. Isso descarta latência e sobra a forma.
/// </summary>
public class HeldMenuTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IReadOnlyDictionary<string, string> Binds() => KeybindReader.Read(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

    static KeySequence Resolve(bool holdMenu) =>
        new CommandResolver(Map(), Binds(), defaults: null, holdMenuOpen: holdMenu)
            .Resolve(new Intent(null, "door.stack.auto", false));

    [Fact]
    public void TheDefaultShapeStillClicksAndReleasesBeforeTheDigits()
    {
        var steps = Resolve(holdMenu: false).Steps;

        Assert.Equal(StepKind.Press, steps[0].Kind);
        Assert.DoesNotContain(steps, s => s.Kind == StepKind.Down);
        Assert.DoesNotContain(steps, s => s.Kind == StepKind.Up);
    }

    [Fact]
    public void TheHeldShapeKeepsTheMenuDownAcrossTheDigits()
    {
        var steps = Resolve(holdMenu: true).Steps;

        Assert.Equal(StepKind.Down, steps[0].Kind);
        Assert.Equal(StepKind.Up, steps[^1].Kind);
        Assert.Equal(steps[0].Token, steps[^1].Token);

        // Tudo entre o Down e o Up é navegação, e nenhum deles solta o menu.
        for (var i = 1; i < steps.Count - 1; i++)
            Assert.Equal(StepKind.Press, steps[i].Kind);
    }

    /// <summary>Os mesmos dígitos, na mesma ordem: só o envelope muda.</summary>
    [Fact]
    public void BothShapesPressTheSameKeysInTheSameOrder()
    {
        static IEnumerable<InputToken> Digits(KeySequence s) =>
            s.Steps.Where(x => x.Kind == StepKind.Press).Select(x => x.Token);

        var clicked = Digits(Resolve(holdMenu: false)).ToList();
        var held = Digits(Resolve(holdMenu: true)).ToList();

        // A forma que clica inclui o próprio menu como Press; a que segura não.
        Assert.Equal(clicked.Skip(1), held);
    }

    /// <summary>
    /// O botão do menu não pode ficar preso se a sequência abortar no meio: um
    /// mouse com botão travado é pior que a ordem não sair.
    /// </summary>
    [Fact]
    public void AnAbortedHeldSequenceStillReleasesTheMenu()
    {
        var sender = new SendInputSender(dryRun: true);
        using var cancel = new CancellationTokenSource();
        var sequence = Resolve(holdMenu: true);

        // Cancela depois do primeiro passo, que é justamente o Down do menu.
        cancel.CancelAfter(TimeSpan.Zero);

        try { sender.Send(sequence, cancel.Token); }
        catch (OperationCanceledException) { }

        var menu = sequence.Steps[0].Token;
        var downs = sender.Events.Count(e => e.Token.Equals(menu) && e.Down);
        var ups = sender.Events.Count(e => e.Token.Equals(menu) && !e.Down);
        Assert.Equal(downs, ups);
    }

    [Fact]
    public void TheSettleStillAppliesAfterTheMenuGoesDown()
    {
        var map = Map();
        var steps = new CommandResolver(
                map.WithTiming(map.Timing with { MenuOpenSettleMs = 300 }),
                Binds(), defaults: null, holdMenuOpen: true)
            .Resolve(new Intent(null, "door.stack.auto", false)).Steps;

        Assert.Equal(300, steps[0].GapAfterMs);
    }
}
