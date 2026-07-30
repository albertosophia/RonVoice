using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

/// <summary>
/// O caminho do mod UE4SS RoNSpeech: uma tecla por ordem, sem abrir o menu SWAT.
/// É o único que funciona em VR — verificado em jogo, onde o menu abre, os
/// dígitos chegam e ele não age sobre eles com espera nenhuma de 60 a 800 ms.
/// </summary>
public class RonSpeechResolverTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IReadOnlyDictionary<string, string> Binds() => KeybindReader.Read(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

    static CommandResolver Resolver() => new(Map(), Binds());

    static IReadOnlyList<ushort> Scans(KeySequence seq) =>
        [.. seq.Steps.Select(s => ((ScanCodeToken)s.Token).Scan)];

    /// <summary>F15 = scan 0x66. É a tecla que foi confirmada em jogo, em VR.</summary>
    [Fact]
    public void BreachWithFlashIsTheSingleKeyThatWasVerifiedInVr()
    {
        var seq = Resolver().ResolveViaRonSpeech(
            new Intent(null, "door.open.flashbang", false));

        Assert.Equal([0x66], Scans(seq));
    }

    /// <summary>
    /// Nenhum passo pode ser o botão do meio: abrir o menu é justamente o que
    /// este caminho existe para não fazer.
    /// </summary>
    [Fact]
    public void NoOrderEverOpensTheMenuOnThisPath()
    {
        var map = Map();
        var resolver = Resolver();

        foreach (var order in map.Orders.Values)
        {
            if (order.RonSpeechKeys is not { Count: > 0 }) continue;

            var seq = resolver.ResolveViaRonSpeech(new Intent(null, order.Id, false));
            Assert.DoesNotContain(seq.Steps, s => s.Token is MouseToken);
        }
    }

    [Fact]
    public void TheElementKeyStillComesFirstAndIsTheSameKeyAsOnTheMenuPath()
    {
        var seq = Resolver().ResolveViaRonSpeech(
            new Intent("red", "door.open.flashbang", false));

        // F7 = 0x41 no Input.ini de teste, depois F15 = 0x66.
        Assert.Equal([0x41, 0x66], Scans(seq));
    }

    /// <summary>"on my command" no mod é o 9 antes da tecla, não o LShift.</summary>
    [Fact]
    public void QueueingPressesNineBeforeTheCommandKey()
    {
        var seq = Resolver().ResolveViaRonSpeech(
            new Intent(null, "door.open.flashbang", true));

        Assert.Equal([0x0A, 0x66], Scans(seq));
        Assert.DoesNotContain(seq.Steps, s => s.Kind is StepKind.Down or StepKind.Up);
    }

    /// <summary>
    /// Nas formações o 9 JÁ é a formação. Acrescentar outro trocaria o comando
    /// em vez de enfileirá-lo.
    /// </summary>
    [Fact]
    public void QueueingDoesNotDoubleTheNineWhenTheOrderAlreadyUsesIt()
    {
        var seq = Resolver().ResolveViaRonSpeech(
            new Intent(null, "move.formation.diamond", true));

        Assert.Equal([0x0A, 0x49], Scans(seq));
    }

    /// <summary>
    /// Recusar em voz alta é o ponto. Cair no menu como reserva seria o pior dos
    /// dois mundos: em VR o jogador veria o menu abrir e nada acontecer.
    /// </summary>
    [Fact]
    public void AnOrderTheModDoesNotCoverIsRefusedNamingIt()
    {
        var ex = Assert.Throws<ResolveException>(() =>
            Resolver().ResolveViaRonSpeech(
                new Intent(null, "door.breach.ram.launcher", false)));

        Assert.Contains("door.breach.ram.launcher", ex.Message);
        Assert.Contains("menu", ex.Message);
    }

    /// <summary>
    /// Toda tecla que a tabela pede tem que ser mandável. Uma que não resolva
    /// deixaria a ordem morta só quando alguém a falasse.
    /// </summary>
    [Fact]
    public void EveryKeyTheTableAsksForCanBeSent()
    {
        var unsendable = Map().Orders.Values
            .Where(o => o.RonSpeechKeys is { Count: > 0 })
            .SelectMany(o => o.RonSpeechKeys!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(k => !KeyCatalog.TryResolve(k, out _))
            .ToList();

        Assert.Empty(unsendable);
    }

    [Fact]
    public void TheModCoversThirtyTwoOfTheSeventyOrders() =>
        Assert.Equal(32, Map().Orders.Values.Count(o => o.RonSpeechKeys is { Count: > 0 }));

    /// <summary>
    /// Duas ordens com a mesma tecla obedeceriam a mesma coisa, e uma delas
    /// ficaria mentindo no catálogo. Cover e door.cover são o mesmo comando no
    /// mod de propósito; fora esse par, nada pode repetir.
    /// </summary>
    [Fact]
    public void NoTwoUnrelatedOrdersShareTheSameKeys()
    {
        var byKeys = Map().Orders.Values
            .Where(o => o.RonSpeechKeys is { Count: > 0 })
            .GroupBy(o => string.Join('+', o.RonSpeechKeys!), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: "
                         + string.Join(", ", g.Select(o => o.Id).OrderBy(x => x, StringComparer.Ordinal)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["F13+F14: cover, door.cover"], byKeys);
    }

    /// <summary>O caminho do menu não pode ter mudado.</summary>
    [Fact]
    public void TheMenuPathIsUntouched()
    {
        var seq = Resolver().Resolve(new Intent(null, "door.open.flashbang", false));

        Assert.IsType<MouseToken>(seq.Steps[0].Token);
    }
}
