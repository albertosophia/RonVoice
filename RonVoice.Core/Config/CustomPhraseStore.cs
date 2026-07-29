using System.Text.Encodings.Web;
using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Config;

/// <summary>
/// Lê e grava o minhas_frases.json. Existe para o app poder editar as frases
/// sem que ninguém precise abrir um editor de texto — o arquivo continua sendo
/// o formato, o que mantém possível trocá-lo com outra pessoa.
/// </summary>
public static class CustomPhraseStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Sem escapar acentos: o arquivo é para humanos lerem e editarem.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Dictionary<string, List<string>> Read(string path)
    {
        if (!File.Exists(path)) return [];

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                File.ReadAllText(path));
            return raw ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Quem chama já mostra o aviso pela validação do CustomPhrases.
            return [];
        }
    }

    /// <summary>
    /// Grava por arquivo temporário e substitui, para que uma falha no meio não
    /// deixe o usuário com o arquivo pela metade e todas as frases perdidas.
    /// </summary>
    public static void Write(string path, Dictionary<string, List<string>> content)
    {
        var cleaned = content
            .Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        if (cleaned.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(cleaned, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Por que uma frase não pode ser aceita, ou null se pode. A checagem roda
    /// antes de gravar, para o usuário saber na hora em vez de descobrir que a
    /// ordem parou de funcionar depois.
    /// </summary>
    /// <param name="pending">
    /// Frases gravadas nesta sessão, por ordem. O mapa é montado na abertura e
    /// não as contém; sem esta segunda passada dá para gravar a mesma frase em
    /// duas ordens no mesmo uso e deixar as duas mudas.
    /// </param>
    public static string? Reject(
        CommandMap map, string orderId, string phrase, string language,
        IReadOnlyDictionary<string, List<string>>? pending = null)
    {
        // Canônica, não só sem acento: "abra com flash" e "abre com flash" são
        // a mesma frase depois da dobra do matcher, e aceitar as duas em ordens
        // diferentes deixaria as duas mudas sem erro nenhum.
        var normalized = VerbForms.Canonical(phrase, language);

        if (normalized.Length == 0) return "escreva alguma coisa";

        foreach (var order in map.Orders.Values)
            if (order.Phrases.TryGetValue(language, out var existing)
                && Owns(existing, normalized, language))
                return Reason(order.Id, orderId, phrase);

        if (pending is not null)
            foreach (var (id, phrases) in pending)
                if (Owns(phrases, normalized, language)) return Reason(id, orderId, phrase);

        return null;
    }

    static bool Owns(IEnumerable<string> phrases, string normalized, string language) =>
        phrases.Any(p => VerbForms.Canonical(p, language) == normalized);

    static string Reason(string ownerId, string orderId, string phrase) =>
        ownerId == orderId
            ? "essa frase já está nesta ordem"
            : $"\"{phrase}\" já pertence a {ownerId} — aceitar deixaria "
              + "as duas ordens sem funcionar";

    public static void Add(
        string path, string orderId, string phrase,
        Dictionary<string, List<string>> content)
    {
        if (!content.TryGetValue(orderId, out var list)) content[orderId] = list = [];
        list.Add(phrase.Trim());
        Write(path, content);
    }

    public static void Remove(
        string path, string orderId, string phrase,
        Dictionary<string, List<string>> content)
    {
        if (!content.TryGetValue(orderId, out var list)) return;
        list.RemoveAll(p => string.Equals(p, phrase, StringComparison.Ordinal));
        if (list.Count == 0) content.Remove(orderId);
        Write(path, content);
    }
}
