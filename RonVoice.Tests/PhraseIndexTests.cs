using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class PhraseIndexTests
{
    static PhraseIndex Index(string lang) =>
        new(CommandMap.Load(CommandMapTests.MapPath), lang);

    static IReadOnlyList<string> T(string s) => TextNormalizer.Tokenize(s);

    [Fact]
    public void IdenticalPhrasesScoreOne() =>
        Assert.Equal(1.0, Index("en").Score(T("stack left"), T("stack left")), 6);

    [Fact]
    public void DisjointPhrasesScoreZero() =>
        Assert.Equal(0.0, Index("en").Score(T("banana pudding"), T("stack left")), 6);

    [Fact]
    public void RareTokensOutweighCommonOnes()
    {
        // "flashbang" discrimina; "door" não. É o que desempata o caso 1 do brief.
        var idx = Index("en");
        var input = T("open the door with flashbang");
        var flash = idx.Score(input, T("open with flashbang"));
        var toggle = idx.Score(input, T("open the door"));
        Assert.True(flash > toggle, $"esperado flashbang > toggle, veio {flash} vs {toggle}");
        Assert.True(flash - toggle >= 0.05, $"margem insuficiente: {flash - toggle}");
    }

    [Fact]
    public void StopwordsAreLanguageSpecific()
    {
        // "do" é artigo em pt e verbo em en. Uma lista compartilhada zeraria "do it".
        Assert.Equal(1.0, Index("en").Score(T("do it"), T("do it")), 6);
    }

    [Fact]
    public void AllStopwordPhraseFallsBackToRawTokens() =>
        Assert.Equal(1.0, Index("en").Score(T("the a and"), T("the a and")), 6);

    [Fact]
    public void RankReturnsBestFirst()
    {
        var top = Index("en").Rank(T("open the door with flashbang"))[0];
        Assert.Equal("door.open.flashbang", top.OrderId);
    }

    [Fact]
    public void RankOnlyContainsPhrasesOfItsLanguage()
    {
        var ptIds = Index("pt").Rank(T("abre com flash")).Select(r => r.Phrase);
        Assert.DoesNotContain("open with flashbang", ptIds);
    }
}
