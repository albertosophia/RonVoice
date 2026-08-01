using System.Runtime.InteropServices;
using System.Text;

namespace RonVoice.Core.Speech;

/// <summary>
/// Faz a gramática atravessar inteira até a biblioteca nativa.
///
/// O Vosk guarda o vocabulário em UTF-8, mas o binding .NET dele é gerado por
/// SWIG e declara a gramática como string comum — o que o marshaller entrega em
/// ANSI. "só" sai daqui em dois bytes e chega lá em um, e nenhuma palavra
/// acentuada casa com o vocabulário. Nunca.
///
/// E não casar não dá erro. A lib nativa escreve "Ignoring word missing in
/// vocabulary" num stderr que ninguém lê, e segue. Como a gramática é fechada —
/// palavra que não está nela jamais é emitida — o efeito é o modo português
/// entender apenas as frases sem acento. Sem exceção, sem log, sem sintoma:
/// a pessoa fala e não acontece nada.
/// </summary>
public static class VoskGrammar
{
    /// <summary>
    /// A mesma gramática, escrita de forma que o marshaller ANSI produza os
    /// bytes UTF-8 que a biblioteca espera.
    ///
    /// O jeito é decodificar os bytes UTF-8 COMO SE fossem Latin-1, que é um
    /// mapa de um byte para um caractere: ao voltar, o marshaller reproduz byte
    /// por byte o que entrou. Texto sem acento passa intocado, porque em ASCII
    /// as codificações coincidem.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Quando a travessia não é fiel. A página ANSI vem do Windows e muda com a
    /// máquina: aqui é a 1252, onde isto funciona, mas noutra pode não ser. Só
    /// que essa página é a mesma que o marshaller usa, então dá para CONFERIR em
    /// vez de torcer — e conferir é obrigatório, porque a falha é muda. Melhor o
    /// app não abrir dizendo o porquê do que abrir entendendo metade das frases.
    /// </exception>
    public static string ForNativeCall(string grammarJson)
    {
        var esperado = Encoding.UTF8.GetBytes(grammarJson);
        var preparada = Encoding.Latin1.GetString(esperado);

        if (!Crosses(preparada, esperado))
            throw new NotSupportedException(
                "a gramática não sobrevive à passagem para o Vosk nesta máquina: "
                + "a página de código do Windows não representa todos os bytes. "
                + "As frases com acento seriam descartadas em silêncio.");

        return preparada;
    }

    /// <summary>
    /// Marshala do mesmo jeito que o P/Invoke do binding faz, e não do jeito que
    /// supomos que ele faça — é a única resposta que vale.
    /// </summary>
    static bool Crosses(string preparada, byte[] esperado)
    {
        var p = Marshal.StringToHGlobalAnsi(preparada);
        try
        {
            for (var i = 0; i < esperado.Length; i++)
                if (Marshal.ReadByte(p, i) != esperado[i]) return false;

            return Marshal.ReadByte(p, esperado.Length) == 0;
        }
        finally { Marshal.FreeHGlobal(p); }
    }
}
