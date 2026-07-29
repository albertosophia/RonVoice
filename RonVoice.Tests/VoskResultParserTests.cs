using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class VoskResultParserTests
{
    const string WithWords = """
        {"result":[
          {"conf":0.98,"end":1.02,"start":0.75,"word":"stack"},
          {"conf":0.86,"end":1.31,"start":1.02,"word":"left"}],
         "text":"stack left"}
        """;

    [Fact]
    public void ReadsTextAndWords()
    {
        var r = VoskResultParser.Parse(WithWords, isFinal: true);
        Assert.Equal("stack left", r.Text);
        Assert.True(r.IsFinal);
        Assert.Collection(r.Words,
            w => Assert.Equal(new WordConfidence("stack", 0.98), w),
            w => Assert.Equal(new WordConfidence("left", 0.86), w));
    }

    [Fact]
    public void AveragesConfidence() =>
        Assert.Equal(0.92, VoskResultParser.Parse(WithWords, true).AverageConfidence, 3);

    [Fact]
    public void HandlesPartialResults()
    {
        var r = VoskResultParser.Parse("""{"partial":"stack"}""", isFinal: false);
        Assert.Equal("stack", r.Text);
        Assert.False(r.IsFinal);
        Assert.Empty(r.Words);
    }

    [Fact]
    public void HandlesEmptyResult()
    {
        var r = VoskResultParser.Parse("""{"text":""}""", isFinal: true);
        Assert.Equal("", r.Text);
        Assert.Empty(r.Words);
    }

    [Fact]
    public void ConfidenceOfAnEmptyResultIsOneSoItIsNotRejectedByTheGate() =>
        Assert.Equal(1.0, VoskResultParser.Parse("""{"text":""}""", true).AverageConfidence);

    [Fact]
    public void DetectsTheUnknownToken()
    {
        Assert.True(VoskResultParser.Parse("""{"text":"[unk] left"}""", true).ContainsUnknown);
        Assert.False(VoskResultParser.Parse(WithWords, true).ContainsUnknown);
    }

    [Fact]
    public void MalformedJsonYieldsAnEmptyResultInsteadOfThrowing()
    {
        // O que vem da lib nativa não é nosso; um resultado vazio é descartado
        // pelo pipeline, e derrubar o reconhecimento por causa disso seria pior.
        var r = VoskResultParser.Parse("nao e json", isFinal: true);
        Assert.Equal("", r.Text);
    }
}
