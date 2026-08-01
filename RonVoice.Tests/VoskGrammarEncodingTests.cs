using System.Runtime.InteropServices;
using System.Text;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>
/// O Vosk guarda o vocabulário em UTF-8, mas o binding dele é gerado por SWIG e
/// entrega a gramática como string ANSI. "só" sai daqui em dois bytes e chega lá
/// em um: nenhuma palavra acentuada casa, nunca.
///
/// E não casar não dá erro. A biblioteca escreve "Ignoring word missing in
/// vocabulary" no stderr da lib nativa, que ninguém lê, e segue. A gramática é
/// fechada: palavra que não está nela jamais é emitida. O efeito é o modo
/// português entender só as frases sem acento — o resto some sem sintoma.
///
/// Isto foi visto: o Vosk imprimiu s\xf3 ao recusar "só", um byte só.
/// </summary>
public class VoskGrammarEncodingTests
{
    /// <summary>
    /// Marshala do mesmo jeito que o P/Invoke do binding, e não do jeito que eu
    /// acho que ele faz: a página de código ANSI é do Windows, não nossa, e
    /// supor qual é seria trocar um palpite por outro.
    /// </summary>
    static byte[] ComoChegaNoVosk(string s)
    {
        var p = Marshal.StringToHGlobalAnsi(s);
        try
        {
            var n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var bytes = new byte[n];
            Marshal.Copy(p, bytes, 0, n);
            return bytes;
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    [Theory]
    [InlineData("só")]
    [InlineData("aríete")]
    [InlineData("lança granadas")]
    [InlineData("põe a mão na cabeça")]
    [InlineData("gás lacrimogêneo")]
    [InlineData("formação em diamante")]
    public void AnAccentedPhraseArrivesAsUtf8(string frase)
    {
        var preparada = VoskGrammar.ForNativeCall(frase);

        Assert.Equal(Encoding.UTF8.GetBytes(frase), ComoChegaNoVosk(preparada));
    }

    /// <summary>
    /// Sem acento nada pode mudar: o inglês inteiro passa por aqui.
    /// </summary>
    [Theory]
    [InlineData("kick and clear")]
    [InlineData("[\"open the door\", \"[unk]\"]")]
    public void PlainTextIsLeftAlone(string texto) =>
        Assert.Equal(texto, VoskGrammar.ForNativeCall(texto));

    /// <summary>
    /// A gramática de verdade, inteira, e não só as palavras que eu lembrei de
    /// testar. Se um dia entrar um caractere que a página ANSI não representa,
    /// tem que quebrar aqui e não silenciosamente lá dentro.
    /// </summary>
    [Theory]
    [InlineData("pt")]
    [InlineData("en")]
    public void TheWholeGrammarSurvivesTheCrossing(string lang)
    {
        var grammar = GrammarBuilder.Build(
            RonVoice.Core.Commands.CommandMap.Load(CommandMapTests.MapPath), lang);

        var chegou = ComoChegaNoVosk(VoskGrammar.ForNativeCall(grammar));

        Assert.Equal(Encoding.UTF8.GetBytes(grammar), chegou);
    }
}
