using System.Text.Encodings.Web;
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
            // Acentos PRESERVADOS de propósito: o vocabulário do modelo português
            // contém as formas acentuadas, e entregar "avanca" em vez de "avança"
            // faz o Vosk descartar a palavra inteira com "Ignoring word missing in
            // vocabulary" — o modo português parava de funcionar por isso.
            // O matcher tira acento dos dois lados, então o casamento não muda.
            var normalized = string.Join(' ', TextNormalizer.TokenizeKeepingAccents(raw));
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

    /// <summary>
    /// Escapamento relaxado é obrigatório: o padrão do System.Text.Json converte
    /// "avança" em "avança", e o parser JSON do Vosk **não decodifica**
    /// essas sequências — ele recebe a barra invertida literal e descarta a
    /// palavra com "Ignoring word missing in vocabulary". O modo português
    /// parava aí. A gramática precisa sair em UTF-8 puro.
    /// </summary>
    static readonly JsonSerializerOptions GrammarJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(CommandMap map, string language) =>
        JsonSerializer.Serialize(Phrases(map, language), GrammarJson);
}
