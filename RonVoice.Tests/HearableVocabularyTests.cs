using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

/// <summary>
/// O modelo de voz tem vocabulário fechado: palavra que ele não conhece nunca é
/// emitida, e uma frase que dependa dela está morta. Não dá erro em lugar nenhum
/// — a lib nativa avisa num stderr que ninguém lê, o catálogo continua mostrando
/// a frase, o teste continua verde, e a pessoa fala e não acontece nada.
///
/// A lista abaixo é medida, não suposta: sai dos avisos do próprio Vosk ao montar
/// a gramática de cada idioma. Para refazê-la:
///
///     dist-app\RonVoice.Cli.exe listen --lang pt --dry-run
///     (procure "Ignoring word missing in vocabulary")
///
/// Ela encolheu de 24 para 8 quando a gramática passou a chegar em UTF-8; ver
/// <see cref="VoskGrammarEncodingTests"/>. O que sobrou são palavras inventadas
/// ou estrangeiras que o modelo pequeno realmente não tem.
/// </summary>
public class HearableVocabularyTests
{
    static readonly Dictionary<string, string[]> NotInTheModel = new()
    {
        ["en"] = ["c2", "chemlight", "exfil", "flashbang", "lockpick"],
        ["pt"] = ["c2", "chemlight", "escaneia", "escaneie", "flashbang",
                  "gaseia", "gaseie", "stinger"],
    };

    static bool Hearable(string phrase, string lang) =>
        !TextNormalizer.Tokenize(phrase).Intersect(NotInTheModel[lang]).Any();

    /// <summary>
    /// Uma ordem pode ter frase surda entre as suas — "chemlight" é surda e
    /// "light out" não é, e a ordem funciona. O que não pode é a ordem inteira
    /// ser surda: aí ela está no catálogo, aparece na busca, e não existe.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void EveryOrderKeepsAtLeastOnePhraseTheModelCanHear(string lang)
    {
        var mudas = CommandMap.Load(CommandMapTests.MapPath).Orders.Values
            .Where(o => o.Phrases.TryGetValue(lang, out var f)
                        && f.Count > 0
                        && !f.Any(p => Hearable(p, lang)))
            .Select(o => $"{o.Id}: {string.Join(" / ", o.Phrases[lang])}")
            .ToList();

        Assert.Empty(mudas);
    }

    /// <summary>
    /// E a frase de entrada de cada ordem — a primeira, que é a que o catálogo
    /// mostra e a que a pessoa vai tentar — tem que ser ouvível. Uma ordem que só
    /// funciona pelo sinônimo escondido é uma ordem que parece quebrada.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void ThePhraseOnTheScreenIsOneThatWorks(string lang)
    {
        var enganosas = CommandMap.Load(CommandMapTests.MapPath).Orders.Values
            .Where(o => o.Phrases.TryGetValue(lang, out var f)
                        && f.Count > 0
                        && !Hearable(f[0], lang))
            .Select(o => $"{o.Id}: \"{o.Phrases[lang][0]}\"")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(enganosas);
    }
}
