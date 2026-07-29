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
    public void RejectsABindWeCannotSendInsteadOfUsingTheDefault()
    {
        // Bind ausente cai no default; bind PRESENTE que não sabemos enviar não
        // pode cair no default — mandaria a tecla que o jogador rebindou para
        // longe, sem erro nenhum. Não é hipótese: o Input.ini real já liga
        // CycleSwatElementNext/Previous à roda do mouse, e a roda é exatamente
        // o que o KeyCatalog (corretamente) recusa.
        var binds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OpenSwatCommand"] = "MouseScrollUp",
        };
        var ex = Assert.Throws<ResolveException>(
            () => new CommandResolver(Map(), binds)
                .Resolve(new Intent(null, "door.stack.left", false)));
        Assert.Contains("OpenSwatCommand", ex.Message);
        Assert.Contains("MouseScrollUp", ex.Message);
    }

    [Fact]
    public void PrefersTheRealBindsOverTheDefaults()
    {
        // Todo bind do Input.ini real coincide com o keybind_defaults, então
        // nenhum outro teste distingue "leu o bind" de "usou o default": dá para
        // apagar a consulta a _binds inteira e o resto da suíte continua verde.
        // Este é o único teste que falha se as teclas voltarem a ser fixas —
        // que é a razão de o KeybindReader existir (§5.7 do brief).
        var binds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SelectElementRed"] = "F2",                // default: F7
            ["OpenSwatCommand"] = "ThumbMouseButton",   // default: MiddleMouse
            ["SwatInputKeyOne"] = "Q",                  // default: One
            ["SwatInputKeyTwo"] = "L",                  // default: Two
        };
        var seq = new CommandResolver(Map(), binds)
            .Resolve(new Intent("red", "door.stack.left", false));   // MENU 1 2

        Assert.Equal(
            new[]
            {
                new KeyStep(StepKind.Press, Sc(0x3C), 35, 35),                     // F2
                new KeyStep(StepKind.Press, new MouseToken(MouseButton.X1), 100, 60),
                new KeyStep(StepKind.Press, Sc(0x10), 35, 35),                     // Q
                new KeyStep(StepKind.Press, Sc(0x26), 35, 35),                     // L
            },
            seq.Steps);
    }

    [Fact]
    public void MenuTimingFollowsTheMenuTokenNotTheKindOfKeyItResolvedTo()
    {
        // O hold de 100 ms e o settle de 60 ms são do passo que ABRE o menu, não
        // de "resolveu para botão de mouse". Quem rebinda OpenSwatCommand para o
        // teclado precisa deles do mesmo jeito, e um dígito que caia num botão de
        // mouse não precisa. Ler o tipo do token troca os dois de lugar em
        // silêncio — e o clique de fechamento, que já usa 100 fixo, faria a mesma
        // tecla ter tempos diferentes ao abrir e ao fechar.
        var binds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OpenSwatCommand"] = "G",                  // menu no teclado
            ["SwatInputKeyOne"] = "ThumbMouseButton",   // dígito no mouse
        };
        var seq = new CommandResolver(Map(), binds)
            .Resolve(new Intent(null, "door.stack.left", false));   // MENU 1 2

        Assert.Equal(
            new[]
            {
                new KeyStep(StepKind.Press, Sc(0x22), 100, 60),                    // G abre o menu
                new KeyStep(StepKind.Press, new MouseToken(MouseButton.X1), 35, 35),
                new KeyStep(StepKind.Press, Sc(0x03), 35, 35),
            },
            seq.Steps);
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

    [Fact]
    public void DryRunSenderEmitsDownUpPairsInOrder()
    {
        var seq = Resolver().Resolve(new Intent("red", "door.open.flashbang", true));
        var sender = new SendInputSender(dryRun: true);
        sender.Send(seq);

        Assert.Equal(
            new[]
            {
                "down scan 0x41", "up   scan 0x41",   // F7
                "down mouse Middle", "up   mouse Middle",
                "down scan 0x03", "up   scan 0x03",
                "down scan 0x2A",                     // LShift desce e fica
                "down scan 0x03", "up   scan 0x03",
                "up   scan 0x2A",
                "down mouse Middle", "up   mouse Middle",
            },
            sender.Log);
    }

    [Fact]
    public void DryRunRespectsTiming()
    {
        var seq = Resolver().Resolve(new Intent(null, "door.stack.left", false));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        new SendInputSender(dryRun: true).Send(seq);
        // MENU(100+60) + 1(35+35) + 2(35+35) = 300 ms; folga generosa para CI lento.
        // O gap do último passo não é zerado — ver a deviation 1 da Task 6.
        // Isto mede só o total: o hold de cada tecla é asserido, evento a
        // evento, em SendInputSenderTests.EveryPressHoldsTheKeyForItsWholeHold.
        Assert.InRange(sw.Elapsed.TotalMilliseconds, 240, 900);
    }
}
