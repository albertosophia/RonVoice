using RonVoice.Core.Commands;

namespace RonVoice.Core.Matching;

/// <param name="Threshold">Piso contra ruído.</param>
/// <param name="Margin">
/// Quanto a melhor pontuação precisa superar a melhor de outra ordem. É o
/// parâmetro que importa: sem ele, o matcher manda ordens erradas.
/// </param>
public sealed record MatcherOptions(double Threshold = 0.60, double Margin = 0.05);

/// <summary>
/// Texto -> Intent. Sem estado entre chamadas: o estado de seleção vive no
/// jogo, não aqui.
/// </summary>
public sealed class PhraseMatcher
{
    readonly PhraseIndex _index;
    readonly MatcherOptions _options;
    readonly List<(string[] Tokens, string Element)> _elementAliases = [];
    readonly List<string[]> _queueAliases = [];

    public PhraseMatcher(CommandMap map, string language, MatcherOptions? options = null)
    {
        _index = new PhraseIndex(map, language);
        _options = options ?? new MatcherOptions();

        foreach (var element in map.Elements.Values)
            foreach (var lang in new[] { "en", "pt" })
                if (element.Aliases.TryGetValue(lang, out var aliases))
                    foreach (var a in aliases)
                        _elementAliases.Add(([.. TextNormalizer.Tokenize(a)], element.Name));

        foreach (var lang in new[] { "en", "pt" })
            if (map.Queue.Aliases.TryGetValue(lang, out var aliases))
                foreach (var a in aliases)
                    _queueAliases.Add([.. TextNormalizer.Tokenize(a)]);

        // Casamento mais longo primeiro: "team" é alias de gold e substring de
        // "red team". Varrer na ordem do JSON resolveria "red team" como gold.
        _elementAliases.Sort((x, y) => y.Tokens.Length.CompareTo(x.Tokens.Length));
        _queueAliases.Sort((x, y) => y.Length.CompareTo(x.Length));
    }

    public Intent? Match(string utterance)
    {
        var tokens = TextNormalizer.Tokenize(utterance);
        if (tokens.Count == 0) return null;

        var (afterElement, elementAlias) = StripLongest(
            tokens, _elementAliases.Select(e => e.Tokens).ToList());
        var element = elementAlias is null
            ? null
            : _elementAliases.First(e => ReferenceEquals(e.Tokens, elementAlias)).Element;

        var (afterQueue, queueAlias) = StripLongest(afterElement, _queueAliases);

        // "hold" é alias de fila E frase da ordem `hold`. Remover de forma gulosa
        // destruiria a ordem, então pontuamos os dois candidatos e ficamos com o melhor.
        var queued = queueAlias is not null ? Evaluate(afterQueue) : null;
        var plain = Evaluate(afterElement);

        var queue = queued is not null
            && (plain is null || QueueWins(queued.Value, plain.Value));
        var best = queue ? queued : plain;

        if (best is null || best.Value.OrderId is null)
            return element is null ? null : new Intent(element, null, false);

        return new Intent(element, best.Value.OrderId, queue);
    }

    /// <param name="OrderId">
    /// null quando a margem não separou o topo do melhor de outra ordem: casou
    /// alguma coisa, mas não o suficiente para mandar tecla.
    /// </param>
    readonly record struct Candidate(double Score, string? OrderId);

    Candidate? Evaluate(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) return null;

        var ranked = _index.Rank(tokens);
        if (ranked.Count == 0 || ranked[0].Score < _options.Threshold) return null;

        var top = ranked[0];
        var runnerUp = ranked.FirstOrDefault(
            r => !string.Equals(r.OrderId, top.OrderId, StringComparison.Ordinal));
        var accepted = top.Score - (runnerUp?.Score ?? 0.0) >= _options.Margin;

        return new Candidate(top.Score, accepted ? top.OrderId : null);
    }

    /// <summary>
    /// Qual leitura vale quando os dois candidatos passam do limiar.
    /// </summary>
    static bool QueueWins(Candidate queued, Candidate plain)
    {
        // Tirar o alias não mudou a ordem casada e não melhorou a pontuação: o
        // alias era palavra da frase, não modificador sobre ela. É o caso das
        // frases que dizem "espera" duas vezes — "arromba e espera espera por
        // mim" é frase de door.breach.leader.leader, e como a pontuação é sobre
        // conjuntos de tokens, remover uma das ocorrências não muda o conjunto:
        // os dois candidatos empatam em 1.000 e o desempate a favor da fila
        // engatilha uma ordem que era para executar.
        //
        // A comparação é entre os dois candidatos, nunca contra o limiar.
        // "prepara empilha a esquerda" também casa a MESMA ordem dos dois lados,
        // mas sem fila pontua 0.756 contra 1.000 — ali o alias é mesmo um
        // modificador, e a fila continua vencendo. Decidir só por "mesma ordem"
        // quebraria esse caso e ignoraria um "queue" dito com todas as letras.
        if (queued.OrderId is not null
            && string.Equals(queued.OrderId, plain.OrderId, StringComparison.Ordinal)
            && plain.Score >= queued.Score)
            return false;

        // Empate desempata a favor da fila: enfileirar o que era para executar
        // deixa os NPCs parados e o jogador percebe; o contrário arromba cedo.
        return queued.Score >= plain.Score;
    }

    /// <summary>
    /// Remove a primeira ocorrência da sequência mais longa que couber.
    /// A lista já vem ordenada por tamanho decrescente.
    /// </summary>
    static (IReadOnlyList<string> Remaining, string[]? Found) StripLongest(
        IReadOnlyList<string> tokens, IReadOnlyList<string[]> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Length == 0 || candidate.Length > tokens.Count) continue;

            for (var i = 0; i + candidate.Length <= tokens.Count; i++)
            {
                var hit = true;
                for (var j = 0; j < candidate.Length; j++)
                {
                    if (!string.Equals(tokens[i + j], candidate[j], StringComparison.Ordinal))
                    {
                        hit = false;
                        break;
                    }
                }
                if (!hit) continue;

                var rest = new List<string>(tokens.Count - candidate.Length);
                for (var k = 0; k < tokens.Count; k++)
                    if (k < i || k >= i + candidate.Length)
                        rest.Add(tokens[k]);
                return (rest, candidate);
            }
        }
        return (tokens, null);
    }
}
