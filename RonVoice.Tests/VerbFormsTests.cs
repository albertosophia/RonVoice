using RonVoice.Core.Commands;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>
/// O mapa foi escrito todo no imperativo informal ("abre com flash"). Quem
/// comanda no formal ("abra com flash") não casava com nada: o elemento era
/// selecionado, nenhuma ordem saía, e não havia erro em lugar nenhum.
/// </summary>
public class VerbFormsTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IEnumerable<string> PtPhrases() =>
        Map().Orders.Values
            .Where(o => o.Phrases.ContainsKey("pt"))
            .SelectMany(o => o.Phrases["pt"]);

    /// <summary>
    /// Tudo que a gramática cobre, não só as frases das ordens: "prepara" é
    /// alias de fila e também tem forma formal.
    /// </summary>
    static IEnumerable<string> PtEverything()
    {
        var map = Map();
        foreach (var p in PtPhrases()) yield return p;

        foreach (var element in map.Elements.Values)
            if (element.Aliases.TryGetValue("pt", out var aliases))
                foreach (var a in aliases) yield return a;

        if (map.Queue.Aliases.TryGetValue("pt", out var queue))
            foreach (var a in queue) yield return a;
    }

    [Fact]
    public void TheReportedPhraseNowResolves()
    {
        var intent = new PhraseMatcher(Map(), "pt").Match("equipe vermelha, abra com flash");

        Assert.NotNull(intent);
        Assert.Equal("door.open.flashbang", intent.OrderId);
        Assert.Equal("red", intent.Element);
    }

    /// <summary>
    /// A rede de verdade: a variante formal de CADA frase do mapa tem que cair
    /// na mesma ordem que a informal. Um par errado na tabela manda a ordem
    /// errada, que compromete a missão.
    /// </summary>
    [Fact]
    public void EveryFormalVariantResolvesToTheSameOrder()
    {
        var map = Map();
        var matcher = new PhraseMatcher(map, "pt");
        var wrong = new List<string>();

        foreach (var order in map.Orders.Values)
        {
            if (!order.Phrases.TryGetValue("pt", out var phrases)) continue;

            foreach (var phrase in phrases)
            {
                if (VerbForms.Variant(phrase, "pt") is not { } formal) continue;

                var got = matcher.Match(formal)?.OrderId;
                if (got != order.Id)
                    wrong.Add($"\"{formal}\" (de \"{phrase}\"): {order.Id} -> {got ?? "nada"}");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// A gramática do Vosk é fechada: palavra que não está nela nunca é
    /// emitida. Sem a variante ali, a dobra do matcher seria inútil — o "abra"
    /// jamais chegaria a ser dobrado porque o reconhecedor não o produziria.
    /// </summary>
    [Fact]
    public void TheFormalFormsAreInTheGrammar()
    {
        var grammar = GrammarBuilder.Phrases(Map(), "pt");
        var words = grammar.SelectMany(p => p.Split(' ')).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("abra", words);
        Assert.Contains("arrombe", words);
        Assert.Contains("chute", words);
        Assert.Contains("jogue", words);
        Assert.Contains("entre", words);
    }

    /// <summary>Acentos preservados, senão o Vosk descarta a palavra.</summary>
    [Fact]
    public void TheAccentedFormalFormsKeepTheirAccents()
    {
        var words = GrammarBuilder.Phrases(Map(), "pt")
            .SelectMany(p => p.Split(' ')).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("vá", words);
        Assert.Contains("avance", words);
    }

    /// <summary>
    /// Um par cujo lado informal não existe no mapa é código morto que não
    /// avisa: a chave nunca casa e a variante desaparece calada. Foi assim que
    /// "avança" com cedilha ficou sem efeito — o mapa escreve "avanca".
    /// </summary>
    [Fact]
    public void TheInformalSideMatchesTheMap()
    {
        var words = PtEverything()
            .SelectMany(p => TextNormalizer.TokenizeKeepingAccents(p))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = VerbForms.PtInformalForms().Where(f => !words.Contains(f)).ToList();
        Assert.Empty(orphans);
    }

    [Fact]
    public void FoldingIsIdempotentOnThePhrasesAlreadyInTheMap()
    {
        foreach (var phrase in PtPhrases())
        {
            var once = VerbForms.Canonical(phrase, "pt");
            Assert.Equal(once, VerbForms.Canonical(once, "pt"));
        }
    }

    /// <summary>
    /// Três formas formais do português — complete, execute, prepare — são
    /// palavras inglesas do próprio mapa. Dobrar sem olhar o idioma
    /// transformaria "prepare to open" em "prepara" e quebraria o inglês.
    /// </summary>
    [Fact]
    public void EnglishIsNeverFolded()
    {
        foreach (var word in new[] { "complete", "execute", "prepare", "move", "para" })
            Assert.Equal(word, VerbForms.Canonical(word, "en"));
    }

    [Fact]
    public void EnglishPhrasesGetNoVariantInTheGrammar() =>
        Assert.Null(VerbForms.Variant("open with flashbang", "en"));

    /// <summary>
    /// "dar" ficou fora da tabela de propósito: o imperativo formal é "dê", que
    /// sem acento é "de" — a preposição mais comum do português. Dobrar "de"
    /// estragaria toda frase que a contém.
    /// </summary>
    [Fact]
    public void ThePrepositionDeSurvivesTheFold() =>
        Assert.Equal("granada de luz", VerbForms.Canonical("granada de luz", "pt"));

    /// <summary>
    /// Duas ordens com a mesma frase depois da dobra ficariam as DUAS mudas,
    /// sem erro. É a falha que este projeto já teve com "drop chemlight".
    /// </summary>
    [Fact]
    public void FoldingNeverMakesTwoOrdersShareAPhrase()
    {
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        var collisions = new List<string>();

        foreach (var order in Map().Orders.Values)
        {
            if (!order.Phrases.TryGetValue("pt", out var phrases)) continue;

            foreach (var phrase in phrases)
            {
                var key = VerbForms.Canonical(phrase, "pt");
                if (owner.TryGetValue(key, out var other) && other != order.Id)
                    collisions.Add($"\"{key}\": {other} vs {order.Id}");
                owner[key] = order.Id;
            }
        }

        Assert.Empty(collisions);
    }

    /// <summary>
    /// A checagem de colisão das frases do usuário tem que enxergar a
    /// equivalência. Sem isso, alguém acrescentaria "abra com flash" a outra
    /// ordem e as duas parariam de funcionar.
    /// </summary>
    [Fact]
    public void AUserPhraseInTheFormalFormIsRefusedByCollision()
    {
        var reason = RonVoice.Core.Config.CustomPhraseStore.Reject(
            Map(), "hold", "abra com flash", "pt");

        Assert.NotNull(reason);
        Assert.Contains("door.open.flashbang", reason);
    }

    [Fact]
    public void TheQueueAliasWorksInTheFormalForm()
    {
        var intent = new PhraseMatcher(Map(), "pt").Match("espere, abra com flash");

        Assert.NotNull(intent);
        Assert.Equal("door.open.flashbang", intent.OrderId);
        Assert.True(intent.Queue);
    }
}
