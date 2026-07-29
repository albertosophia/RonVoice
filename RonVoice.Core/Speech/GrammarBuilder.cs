using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Speech;

/// <summary>
/// Monta a gramática fechada que o Vosk recebe. Lista plana composicional: frases
/// de ordem, aliases de elemento e de fila como entradas independentes. O
/// PhraseMatcher já sabe extrair elemento e fila de qualquer arranjo de palavras,
/// então não geramos o produto cartesiano.
/// </summary>
public static class GrammarBuilder
{
    /// <summary>
    /// Obrigatório. Sem ele o Vosk força qualquer áudio para dentro da gramática
    /// e ruído vira comando — o app passa a mandar ordens sozinho.
    /// </summary>
    public const string UnknownToken = "[unk]";

    public static IReadOnlyList<string> Phrases(CommandMap map, string language)
    {
        if (language is not ("en" or "pt"))
            throw new ArgumentException($"idioma não suportado: {language}", nameof(language));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        void Add(string raw)
        {
            // O reconhecedor devolve minúsculas sem pontuação; normalizamos a
            // gramática do mesmo jeito para o matcher receber o que espera.
            var normalized = string.Join(' ', TextNormalizer.Tokenize(raw));
            if (normalized.Length > 0 && seen.Add(normalized)) result.Add(normalized);
        }

        foreach (var order in map.Orders.Values)
            if (order.Phrases.TryGetValue(language, out var phrases))
                foreach (var p in phrases) Add(p);

        foreach (var element in map.Elements.Values)
            if (element.Aliases.TryGetValue(language, out var aliases))
                foreach (var a in aliases) Add(a);

        if (map.Queue.Aliases.TryGetValue(language, out var queueAliases))
            foreach (var a in queueAliases) Add(a);

        result.Add(UnknownToken);
        return result;
    }

    public static string Build(CommandMap map, string language) =>
        JsonSerializer.Serialize(Phrases(map, language));
}
