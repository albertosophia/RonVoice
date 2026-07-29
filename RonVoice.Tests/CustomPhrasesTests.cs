using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Config;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class CustomPhrasesTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static string WriteFile(Dictionary<string, string[]> content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-frases-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(content));
        return path;
    }

    static IReadOnlyList<string> PhrasesOf(CommandMap map, string orderId, string lang) =>
        map.Orders[orderId].Phrases[lang];

    [Fact]
    public void NoFileMeansNoChangeAndNoComplaints()
    {
        var result = CustomPhrases.Apply(Map(), null, "pt");
        Assert.Empty(result.Issues);
        Assert.Equal(371, result.Map.Orders.Values.Sum(o => o.Phrases["pt"].Count));
    }

    [Fact]
    public void MissingFileIsSilent()
    {
        var result = CustomPhrases.Apply(
            Map(), Path.Combine(Path.GetTempPath(), "nao-existe-ronvoice.json"), "pt");
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AddsAPhraseToAnExistingOrder()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            Assert.Empty(result.Issues);
            Assert.Contains("manda a bang", PhrasesOf(result.Map, "door.open.flashbang", "pt"));
            Assert.Contains("manda a bang", result.Accepted["door.open.flashbang"]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void KeepsTheOriginalPhrasesOfThatOrder()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Contains("abre com flash", PhrasesOf(result.Map, "door.open.flashbang", "pt"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void DoesNotTouchOtherOrders()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var before = PhrasesOf(Map(), "hold", "pt").Count;
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Equal(before, PhrasesOf(result.Map, "hold", "pt").Count);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void UnknownOrderIsReportedAndIgnored()
    {
        var file = WriteFile(new() { ["ordem.que.nao.existe"] = ["qualquer coisa"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            var issue = Assert.Single(result.Issues);
            Assert.Equal(PhraseIssueKind.UnknownOrder, issue.Kind);
            Assert.Contains("ordem.que.nao.existe", issue.Message);
            Assert.Empty(result.Accepted);
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// A validacao que justifica a funcionalidade existir. Este projeto ja sofreu
    /// isso: "drop chemlight" estava em duas ordens e AS DUAS ficavam mudas, sem
    /// erro em lugar nenhum.
    /// </summary>
    [Fact]
    public void APhraseThatCollidesWithAnotherOrderIsRefused()
    {
        // "empilha" ja e' frase de door.stack.auto; nao pode entrar em hold.
        var file = WriteFile(new() { ["hold"] = ["empilha"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            var issue = Assert.Single(result.Issues);
            Assert.Equal(PhraseIssueKind.Collision, issue.Kind);
            Assert.Contains("door.stack.auto", issue.Message);
            Assert.DoesNotContain("empilha", PhrasesOf(result.Map, "hold", "pt"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ARefusedPhraseDoesNotBlockTheGoodOnesInTheSameFile()
    {
        var file = WriteFile(new()
        {
            ["hold"] = ["empilha", "fica quieto"],
        });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            Assert.Single(result.Issues);
            Assert.Contains("fica quieto", PhrasesOf(result.Map, "hold", "pt"));
        }
        finally { File.Delete(file); }
    }

    /// <summary>A checagem tem que usar a mesma normalizacao do matcher, ou mente.</summary>
    [Fact]
    public void CollisionIgnoresCaseAccentAndPunctuation()
    {
        var file = WriteFile(new() { ["hold"] = ["Empilha!"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Equal(PhraseIssueKind.Collision, Assert.Single(result.Issues).Kind);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ADuplicateOnTheSameOrderIsIgnoredQuietly()
    {
        var file = WriteFile(new() { ["door.stack.auto"] = ["empilha"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            var issue = Assert.Single(result.Issues);
            Assert.Equal(PhraseIssueKind.Duplicate, issue.Kind);
            // Nao duplica na lista.
            Assert.Equal(1, PhrasesOf(result.Map, "door.stack.auto", "pt")
                .Count(p => p == "empilha"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EmptyPhrasesAreIgnored()
    {
        var file = WriteFile(new() { ["hold"] = ["", "   "] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.All(result.Issues, i => Assert.Equal(PhraseIssueKind.Empty, i.Kind));
            Assert.Empty(result.Accepted);
        }
        finally { File.Delete(file); }
    }

    /// <summary>Arquivo quebrado nao pode impedir o app de abrir.</summary>
    [Fact]
    public void AMalformedFileYieldsTheOriginalMapPlusOneComplaint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-ruim-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ isto nao e json ");
        try
        {
            var result = CustomPhrases.Apply(Map(), path, "pt");

            Assert.Equal(PhraseIssueKind.FileUnreadable, Assert.Single(result.Issues).Kind);
            Assert.Equal(371, result.Map.Orders.Values.Sum(o => o.Phrases["pt"].Count));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OnlyTheChosenLanguageIsTouched()
    {
        var file = WriteFile(new() { ["hold"] = ["fica quieto"] });
        try
        {
            var before = PhrasesOf(Map(), "hold", "en").Count;
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Equal(before, PhrasesOf(result.Map, "hold", "en").Count);
        }
        finally { File.Delete(file); }
    }

    // ---- Task 2: as invariantes do mapa sobrevivem a mesclagem ----

    /// <summary>
    /// A garantia central do projeto: nenhuma frase resolve para ordem errada.
    /// Frases proprias nao podem quebra-la.
    /// </summary>
    [Fact]
    public void AfterMergingNoPhraseResolvesToTheWrongOrder()
    {
        var file = WriteFile(new()
        {
            ["door.open.flashbang"] = ["manda a bang", "joga a luz e entra"],
            ["hold"] = ["fica quieto"],
            ["door.stack.left"] = ["cola na esquerda"],
        });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Empty(result.Issues);

            var matcher = new PhraseMatcher(result.Map, "pt");
            var wrong = new List<string>();

            foreach (var order in result.Map.Orders.Values)
                foreach (var phrase in order.Phrases["pt"])
                {
                    var got = matcher.Match(phrase)?.OrderId;
                    if (got is not null && got != order.Id)
                        wrong.Add($"{phrase}: {order.Id} -> {got}");
                }

            Assert.Empty(wrong);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheNewPhrasesAreActuallyReachable()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            var matcher = new PhraseMatcher(result.Map, "pt");

            Assert.Equal("door.open.flashbang", matcher.Match("manda a bang")?.OrderId);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EveryOrderStaysReachableAfterMerging()
    {
        var file = WriteFile(new() { ["hold"] = ["fica quieto"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            var matcher = new PhraseMatcher(result.Map, "pt");

            var reachable = new HashSet<string>(StringComparer.Ordinal);
            foreach (var order in result.Map.Orders.Values)
                foreach (var phrase in order.Phrases["pt"])
                    if (matcher.Match(phrase)?.OrderId == order.Id) reachable.Add(order.Id);

            Assert.Empty(result.Map.Orders.Keys.Except(reachable));
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// A gramatica precisa conter a frase nova, senao o Vosk nunca a ouve — e o
    /// usuario conclui que a funcionalidade nao funciona.
    /// </summary>
    [Fact]
    public void TheNewPhraseEntersTheRecognizerGrammar()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            var grammar = GrammarBuilder.Build(result.Map, "pt");
            Assert.Contains("manda a bang", grammar);
        }
        finally { File.Delete(file); }
    }
}
