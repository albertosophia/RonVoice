using System.Globalization;
using System.Text;

namespace RonVoice.Core.Matching;

public static class TextNormalizer
{
    /// <summary>
    /// Minúsculas, sem diacríticos, sem pontuação, espaços colapsados.
    /// "Red team, open the door!" -> ["red","team","open","the","door"]
    /// </summary>
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
