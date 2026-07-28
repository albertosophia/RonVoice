using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class CommandResolverTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IReadOnlyDictionary<string, string> Binds() => KeybindReader.Read(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

    static CommandResolver Resolver(IReadOnlyDictionary<string, string>? binds = null) =>
        new(Map(), binds ?? Binds());

    static readonly InputToken Mmb = new MouseToken(MouseButton.Middle);
    static InputToken Sc(int s) => new ScanCodeToken((ushort)s, false);

    [Fact]
    public void ElementOnlySendsJustTheSelectionKey()
    {
        var seq = Resolver().Resolve(new Intent("red", null, false));
        Assert.Collection(seq.Steps,
            s => Assert.Equal(new KeyStep(StepKind.Press, Sc(0x41), 35, 35), s));
    }

    [Fact]
    public void OrderWithoutElementSkipsTheSelectionKey()
    {
        // door.stack.left = MENU 1 2
        var seq = Resolver().Resolve(new Intent(null, "door.stack.left", false));
        Assert.Equal(
            new[]
            {
                new KeyStep(StepKind.Press, Mmb,      100, 60),
                new KeyStep(StepKind.Press, Sc(0x02),  35, 35),
                new KeyStep(StepKind.Press, Sc(0x03),  35, 35),
            },
            seq.Steps);
    }

    [Fact]
    public void QueuedOrderWrapsOnlyTheLastKeyAndClosesTheMenu()
    {
        // door.open.flashbang = MENU 2 2, close_menu: true
        var seq = Resolver().Resolve(new Intent("red", "door.open.flashbang", true));
        Assert.Equal(
            new[]
            {
                new KeyStep(StepKind.Press, Sc(0x41),  35, 35),   // F7
                new KeyStep(StepKind.Press, Mmb,      100, 60),   // abre
                new KeyStep(StepKind.Press, Sc(0x03),  35, 35),   // 2
                new KeyStep(StepKind.Down,  Sc(0x2A),   0, 0),    // LShift
                new KeyStep(StepKind.Press, Sc(0x03),  35, 35),   // 2, envolvida
                new KeyStep(StepKind.Up,    Sc(0x2A),   0, 0),
                new KeyStep(StepKind.Press, Mmb,      100, 0),    // fecha
            },
            seq.Steps);
    }

    [Fact]
    public void QueuedOrderWithoutCloseMenuDoesNotClose()
    {
        // door.stack.auto = MENU 1 4, sem close_menu.
        // Caminho de 3 tokens -> 1 (MENU) + 1 (dígito do meio) + 3 (down/press/up
        // do último dígito, envolvido pela fila) = 5 passos. Ver task-6-report.md
        // para a divergência com a contagem original do brief (4).
        var seq = Resolver().Resolve(new Intent(null, "door.stack.auto", true));
        Assert.Equal(StepKind.Up, seq.Steps[^1].Kind);
        Assert.Equal(5, seq.Steps.Count);
    }

    [Fact]
    public void UnqueuedOrderNeverClosesTheMenuEvenWhenFlagged()
    {
        var seq = Resolver().Resolve(new Intent(null, "door.open.flashbang", false));
        Assert.Equal(3, seq.Steps.Count);
        Assert.DoesNotContain(seq.Steps.Skip(1), s => Equals(s.Token, Mmb));
    }

    [Fact]
    public void ResolvesDirectKeyTokens()
    {
        // player.fireselect = KEY:X -> FireSelect -> X
        var seq = Resolver().Resolve(new Intent(null, "player.fireselect", false));
        Assert.Collection(seq.Steps,
            s => Assert.Equal(new KeyStep(StepKind.Press, Sc(0x2D), 35, 35), s));
    }

    [Fact]
    public void FallsBackToDefaultsWhenBindIsAbsent()
    {
        // Input.missing.ini não tem OpenSwatCommand; keybind_defaults diz MiddleMouse
        var binds = KeybindReader.Read(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.missing.ini"));
        var seq = new CommandResolver(Map(), binds)
            .Resolve(new Intent(null, "door.stack.left", false));
        Assert.Equal(Mmb, seq.Steps[0].Token);
    }

    [Fact]
    public void ThrowsNamingTheActionWhenNothingResolves()
    {
        var map = Map();
        var broken = map.Defaults with { CommandKeys = ["Xyzzy", "Two", "Three", "Four",
                                                        "Five", "Six", "Seven", "Eight", "Nine"] };
        var resolver = new CommandResolver(map, new Dictionary<string, string>(), broken);
        var ex = Assert.Throws<ResolveException>(
            () => resolver.Resolve(new Intent(null, "door.stack.left", false)));
        Assert.Contains("Xyzzy", ex.Message);
    }

    [Fact]
    public void ThrowsOnUnknownOrderId() =>
        Assert.Throws<ResolveException>(
            () => Resolver().Resolve(new Intent(null, "nao.existe", false)));

    [Fact]
    public void EveryOrderInTheMapResolves()
    {
        var r = Resolver();
        foreach (var id in Map().Orders.Keys)
            _ = r.Resolve(new Intent(null, id, false));
    }
}
