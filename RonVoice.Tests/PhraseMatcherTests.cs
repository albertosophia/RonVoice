using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class PhraseMatcherTests
{
    static PhraseMatcher M(string lang = "en") =>
        new(CommandMap.Load(CommandMapTests.MapPath), lang);

    [Theory]
    // Os seis casos da §8 do brief, com o caso 5 corrigido pela §2.6 da spec.
    [InlineData("red team, open the door with flashbang", "door.open.flashbang", "red", false)]
    [InlineData("open the door with flashbang", "door.open.flashbang", null, false)]
    [InlineData("red team", null, "red", false)]
    [InlineData("stack up left", "door.stack.left", null, false)]
    [InlineData("blue team prep breach and clear", "door.breach.leader.clear", "blue", true)]
    // Colisão team/red team: casamento mais longo primeiro.
    [InlineData("team", null, "gold", false)]
    // Colisão hold: alias de fila e frase de ordem ao mesmo tempo.
    [InlineData("hold", "hold", null, false)]
    [InlineData("hold position", "hold", null, false)]
    [InlineData("gold team hold", "hold", "gold", false)]
    [InlineData("red team hold up", "hold", "red", false)]
    // Stopwords por idioma: "do" não pode ser removido em inglês.
    [InlineData("do it", "confirm.default", null, false)]
    [InlineData("go go go", "confirm.default", null, false)]
    public void EnglishAdversarialCases(string text, string? orderId, string? element, bool queue)
    {
        var intent = M().Match(text);
        Assert.NotNull(intent);
        Assert.Equal(orderId, intent!.OrderId);
        Assert.Equal(element, intent.Element);
        Assert.Equal(queue, intent.Queue);
    }

    [Theory]
    [InlineData("time vermelho abre com flash", "door.open.flashbang", "red", false)]
    [InlineData("azul prepara empilha a esquerda", "door.stack.left", "blue", true)]
    public void PortugueseAdversarialCases(string text, string orderId, string element, bool queue)
    {
        var intent = M("pt").Match(text);
        Assert.NotNull(intent);
        Assert.Equal(orderId, intent!.OrderId);
        Assert.Equal(element, intent.Element);
        Assert.Equal(queue, intent.Queue);
    }

    [Fact]
    public void NoiseYieldsNothing() => Assert.Null(M("pt").Match("banana pudim relogio"));

    [Fact]
    public void EmptyInputYieldsNothing() => Assert.Null(M().Match("   "));

    [Fact]
    public void ElementOnlyIntentHasNoOrder()
    {
        var intent = M().Match("blue team");
        Assert.Equal(new Intent("blue", null, false), intent);
    }

    [Fact]
    public void TighterMarginRejectsInsteadOfGuessing()
    {
        var strict = new PhraseMatcher(
            CommandMap.Load(CommandMapTests.MapPath), "en", new MatcherOptions(Margin: 0.90));
        Assert.Null(strict.Match("open the door with flashbang"));
    }
}
