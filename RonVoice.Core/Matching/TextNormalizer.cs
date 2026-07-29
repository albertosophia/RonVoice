using System.Globalization;
using System.Text;

namespace RonVoice.Core.Matching;

public static class TextNormalizer
{
    /// <summary>
    /// Minúsculas, sem diacríticos, sem pontuação, espaços colapsados.
    /// "Red team, open the door!" -> ["red","team","open","the","door"]
    /// </summary>
    /// <summary>
    /// Como <see cref="Tokenize"/>, mas preserva os acentos. É o que a gramática
    /// do reconhecedor precisa: o vocabulário do modelo português contém as
    /// formas acentuadas, e entregar "avanca" faz o Vosk descartar a palavra
    /// com "Ignoring word missing in vocabulary". O matcher continua tirando
    /// acento dos dois lados, então o casamento não muda.
    /// </summary>
    public static IReadOnlyList<string> TokenizeKeepingAccents(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');

        return sb.ToString()
                 .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static IReadOnlyList<string> Tokenize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : ' ');
        }

        return sb.ToString()
                 .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
