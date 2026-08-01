using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class GrammarBuilderTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static string[] Parse(string json) => JsonSerializer.Deserialize<string[]>(json)!;

    [Fact]
    public void ProducesValidJsonArray() =>
        Assert.NotEmpty(Parse(GrammarBuilder.Build(Map(), "en")));

    /// <summary>
    /// COM acento, e não na forma dobrada que o casamento usa. O vocabulário do
    /// modelo português tem "lança" e não conhece "lanca": entregar a grafia sem
    /// acento faz o Vosk descartar a palavra com um aviso que ninguém lê, e a
    /// frase deixa de ser ouvível — sem erro, sem falha de teste, sem nada.
    /// Aconteceu com 107 frases.
    /// </summary>
    [Theory]
    [InlineData("en", 438)]
    [InlineData("pt", 427)]
    public void ContainsEveryOrderPhraseOfTheLanguage(string lang, int expected)
    {
        var grammar = Parse(GrammarBuilder.Build(Map(), lang));
        var orderPhrases = Map().Orders.Values.SelectMany(o => o.Phrases[lang]).ToList();
        Assert.Equal(expected, orderPhrases.Count);
        foreach (var p in orderPhrases)
            Assert.Contains(string.Join(' ', TextNormalizer.TokenizeKeepingAccents(p)), grammar);
    }

    [Fact]
    public void ContainsElementAndQueueAliases()
    {
        var grammar = Parse(GrammarBuilder.Build(Map(), "en"));
        Assert.Contains("red team", grammar);
        Assert.Contains("prep", grammar);
    }

    [Fact]
    public void AlwaysContainsTheUnknownToken()
    {
        // Sem [unk] o Vosk força qualquer áudio para dentro da gramática:
        // ruído vira comando porque foi a opção menos improvável.
        Assert.Contains("[unk]", Parse(GrammarBuilder.Build(Map(), "en")));
        Assert.Contains("[unk]", Parse(GrammarBuilder.Build(Map(), "pt")));
    }

    [Fact]
    public void HasNoDuplicates()
    {
        var grammar = Parse(GrammarBuilder.Build(Map(), "en"));
        Assert.Equal(grammar.Length, grammar.Distinct().Count());
    }

    [Fact]
    public void EveryEntryIsLowercaseAndFreeOfPunctuation()
    {
        foreach (var entry in Parse(GrammarBuilder.Build(Map(), "en")))
        {
            if (entry == GrammarBuilder.UnknownToken) continue;
            Assert.Equal(entry.ToLowerInvariant(), entry);
            Assert.DoesNotContain(',', entry);
            Assert.DoesNotContain('!', entry);
        }
    }

    [Fact]
    public void TheTwoLanguagesDiffer() =>
        Assert.NotEqual(GrammarBuilder.Build(Map(), "en"), GrammarBuilder.Build(Map(), "pt"));

    [Fact]
    public void UnknownLanguageThrows() =>
        Assert.Throws<ArgumentException>(() => GrammarBuilder.Build(Map(), "de"));
}
