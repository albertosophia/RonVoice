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

    [Theory]
    // O alias de fila ("espera") aparece dentro da própria frase da ordem, e
    // duas vezes. Como a pontuação é sobre conjuntos de tokens, tirar uma das
    // ocorrências não muda o conjunto: os dois candidatos empatam em 1.000 e o
    // desempate a favor da fila engatilhava uma ordem que era para executar —
    // o time empilha e não faz nada até um go-code separado, sem erro visível.
    [InlineData("arromba e espera espera por mim", "door.breach.leader.leader")]
    [InlineData("arromba e espera e me espera", "door.breach.leader.leader")]
    public void AQueueAliasThatIsPartOfThePhraseDoesNotQueue(string text, string orderId)
    {
        var intent = M("pt").Match(text);
        Assert.NotNull(intent);
        Assert.Equal(orderId, intent!.OrderId);
        Assert.False(intent.Queue);
    }

    [Theory]
    // O contrapeso do teste acima, e a razão de a regra comparar pontuações em
    // vez de só comparar ids: aqui os dois candidatos também casam a MESMA
    // ordem, mas tirar o alias melhora muito a pontuação (0.756 -> 1.000 em pt,
    // 0.656 -> 1.000 em en), então o alias era mesmo um modificador. Decidir só
    // por "mesma ordem dos dois lados" faria "queue" dito com todas as letras
    // ser ignorado.
    [InlineData("pt", "prepara empilha a esquerda", "door.stack.left")]
    [InlineData("en", "queue open the door", "door.toggle")]
    public void AQueueAliasThatModifiesThePhraseStillQueues(string lang, string text, string orderId)
    {
        var intent = M(lang).Match(text);
        Assert.NotNull(intent);
        Assert.Equal(orderId, intent!.OrderId);
        Assert.True(intent.Queue);
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
