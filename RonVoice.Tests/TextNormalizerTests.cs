using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void LowercasesAndSplits() =>
        Assert.Equal(new[] { "stack", "up" }, TextNormalizer.Tokenize("Stack Up"));

    [Fact]
    public void StripsPunctuation() =>
        Assert.Equal(new[] { "red", "team", "open", "the", "door" },
                     TextNormalizer.Tokenize("Red team, open the door!"));

    [Fact]
    public void StripsDiacritics() =>
        Assert.Equal(new[] { "posicao", "a", "esquerda" },
                     TextNormalizer.Tokenize("posição à esquerda"));

    [Fact]
    public void CollapsesWhitespace() =>
        Assert.Equal(new[] { "a", "b" }, TextNormalizer.Tokenize("  a \t\n  b  "));

    [Fact]
    public void KeepsDigits() =>
        Assert.Equal(new[] { "c2", "and", "clear" }, TextNormalizer.Tokenize("C2 and clear"));

    [Fact]
    public void EmptyInputYieldsEmptyList() =>
        Assert.Empty(TextNormalizer.Tokenize("  ,.!  "));
}
