using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Config;

public sealed record CustomPhraseResult(
    CommandMap Map,
    IReadOnlyList<PhraseIssue> Issues,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Accepted);

/// <summary>
/// Acrescenta ao mapa as frases que o usuário escreveu. Só acrescenta: não
/// remove frase de fábrica nem cria ordem, porque uma ordem nova exigiria que
/// ele escrevesse a sequência de teclas do menu, e uma sequência errada manda
/// teclas erradas ao jogo sem explicação nenhuma.
/// </summary>
public static class CustomPhrases
{
    public const string FileName = "minhas_frases.json";

    public static CustomPhraseResult Apply(CommandMap map, string? filePath, string language)
    {
        var issues = new List<PhraseIssue>();
        var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        if (filePath is null || !File.Exists(filePath))
            return new CustomPhraseResult(map, issues, accepted);

        Dictionary<string, string[]>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                File.ReadAllText(filePath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            issues.Add(new PhraseIssue(
                PhraseIssueKind.FileUnreadable, "", "",
                $"não consegui ler {Path.GetFileName(filePath)}: {ex.Message}"));
            return new CustomPhraseResult(map, issues, accepted);
        }

        if (raw is null || raw.Count == 0)
            return new CustomPhraseResult(map, issues, accepted);

        // Índice de frase normalizada -> ordem dona, para detectar colisão.
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var order in map.Orders.Values)
            if (order.Phrases.TryGetValue(language, out var existing))
                foreach (var p in existing)
                    owner.TryAdd(Normalize(p), order.Id);

        var additions = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (orderId, phrases) in raw)
        {
            if (!map.Orders.ContainsKey(orderId))
            {
                issues.Add(new PhraseIssue(
                    PhraseIssueKind.UnknownOrder, orderId, "",
                    $"ordem desconhecida: {orderId}"));
                continue;
            }

            foreach (var phrase in phrases ?? [])
            {
                var normalized = Normalize(phrase ?? "");

                if (normalized.Length == 0)
                {
                    issues.Add(new PhraseIssue(
                        PhraseIssueKind.Empty, orderId, phrase ?? "", "frase vazia"));
                    continue;
                }

                if (owner.TryGetValue(normalized, out var existingOwner))
                {
                    if (existingOwner == orderId)
                        issues.Add(new PhraseIssue(
                            PhraseIssueKind.Duplicate, orderId, phrase!,
                            $"\"{phrase}\" já existe em {orderId}"));
                    else
                        // Aceitar deixaria as duas ordens mudas: o matcher
                        // rejeita por ambiguidade e não há erro em lugar nenhum.
                        issues.Add(new PhraseIssue(
                            PhraseIssueKind.Collision, orderId, phrase!,
                            $"\"{phrase}\" já pertence a {existingOwner}; "
                            + "aceitar deixaria as duas ordens sem funcionar"));
                    continue;
                }

                owner[normalized] = orderId;
                if (!additions.TryGetValue(orderId, out var list))
                    additions[orderId] = list = [];
                list.Add(phrase!);
            }
        }

        foreach (var (orderId, list) in additions)
            accepted[orderId] = list;

        return new CustomPhraseResult(Merge(map, additions, language), issues, accepted);
    }

    static string Normalize(string phrase) =>
        string.Join(' ', TextNormalizer.Tokenize(phrase));

    static CommandMap Merge(
        CommandMap map, Dictionary<string, List<string>> additions, string language)
    {
        if (additions.Count == 0) return map;

        var orders = new Dictionary<string, OrderDefinition>(StringComparer.Ordinal);
        foreach (var (id, order) in map.Orders)
        {
            if (!additions.TryGetValue(id, out var extra)) { orders[id] = order; continue; }

            var phrases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var (lang, list) in order.Phrases)
                phrases[lang] = lang == language ? [.. list, .. extra] : list;

            orders[id] = order with { Phrases = phrases };
        }

        return map.WithOrders(orders);
    }
}
