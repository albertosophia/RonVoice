using RonVoice.Core.Commands;

namespace RonVoice.Core.Matching;

public sealed record ScoredPhrase(double Score, string OrderId, string Phrase);

/// <summary>
/// Catálogo de frases de um idioma, com pesos IDF. Sobreposição simples de
/// tokens não separa "open the door" de "open with flashbang" — pesar cada
/// token pelo inverso da frequência separa.
/// </summary>
public sealed class PhraseIndex
{
    /// <summary>
    /// Por idioma, nunca compartilhadas: "do" é artigo em pt e verbo em en.
    /// "with"/"com" ficam de fora de propósito — são o que distingue
    /// "open with flashbang" de "open the door".
    /// </summary>
    static readonly IReadOnlyDictionary<string, HashSet<string>> Stopwords =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["en"] = new(StringComparer.Ordinal)
                { "the", "a", "an", "and", "to", "of", "on", "it", "that", "for" },
            ["pt"] = new(StringComparer.Ordinal)
                { "o", "a", "os", "as", "e", "de", "do", "da", "no", "na", "um", "uma", "que" },
        };

    readonly HashSet<string> _stop;
    readonly Dictionary<string, double> _idf = new(StringComparer.Ordinal);
    readonly double _defaultIdf;
    readonly List<(string OrderId, string Raw, HashSet<string> Tokens)> _phrases = [];

    public string Language { get; }

    public PhraseIndex(CommandMap map, string language)
    {
        Language = language;
        _stop = Stopwords.TryGetValue(language, out var s) ? s : new HashSet<string>(StringComparer.Ordinal);

        foreach (var order in map.Orders.Values)
        {
            if (!order.Phrases.TryGetValue(language, out var list)) continue;
            foreach (var raw in list)
                _phrases.Add((order.Id, raw, [.. TextNormalizer.Tokenize(raw)]));
        }

        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, _, tokens) in _phrases)
            foreach (var t in tokens)
                if (!_stop.Contains(t))
                    df[t] = df.GetValueOrDefault(t) + 1;

        var n = _phrases.Count;
        _defaultIdf = Math.Log(1 + n);
        foreach (var (t, c) in df)
            _idf[t] = Math.Log(1 + (double)n / (1 + c));
    }

    public double Score(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        Score(Filter(a), Filter(b));

    public IReadOnlyList<ScoredPhrase> Rank(IReadOnlyList<string> tokens)
    {
        var a = Filter(tokens);
        var results = new List<ScoredPhrase>(_phrases.Count);
        foreach (var (orderId, raw, phraseTokens) in _phrases)
            results.Add(new ScoredPhrase(Score(a, Filter(phraseTokens)), orderId, raw));
        results.Sort((x, y) => y.Score.CompareTo(x.Score));
        return results;
    }

    /// <summary>Remove stopwords; se sobrar nada, devolve o conjunto cru.</summary>
    HashSet<string> Filter(IEnumerable<string> tokens)
    {
        var all = new HashSet<string>(tokens, StringComparer.Ordinal);
        var kept = new HashSet<string>(all, StringComparer.Ordinal);
        kept.ExceptWith(_stop);
        return kept.Count > 0 ? kept : all;
    }

    double Score(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;

        double intersection = 0, weightA = 0, weightB = 0;
        foreach (var t in a)
        {
            var w = Weight(t);
            weightA += w;
            if (b.Contains(t)) intersection += w;
        }
        foreach (var t in b) weightB += Weight(t);

        return 2 * intersection / (weightA + weightB);
    }

    double Weight(string token) => _idf.GetValueOrDefault(token, _defaultIdf);
}
