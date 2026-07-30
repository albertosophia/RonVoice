using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RonVoice.Core.Commands;

namespace RonVoice.Core.Config;

/// <param name="Kind">
/// Marcador de formato. Existe para importar um JSON qualquer falhar dizendo o
/// que é, em vez de funcionar pela metade e deixar o usuário sem saber por quê.
/// </param>
/// <param name="Language">
/// O idioma para o qual as frases foram escritas. Importar frases em português
/// num app em inglês não daria erro nenhum — elas simplesmente nunca seriam
/// ouvidas, porque a gramática do reconhecedor é montada por idioma.
/// </param>
/// <param name="Phrases">Por id de ordem, como no minhas_frases.json.</param>
public sealed record PhraseProfile(
    [property: JsonPropertyName("ronvoice_profile")] int Kind,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("phrases")] Dictionary<string, List<string>> Phrases)
{
    public const int CurrentKind = 1;

    /// <summary>
    /// Configuração NÃO entra aqui, de propósito. Microfone, caminho do jogo e
    /// tecla de push-to-talk são da máquina de quem exportou; importar isso
    /// apontaria o app de outra pessoa para um microfone que ela não tem.
    /// </summary>
    public static PhraseProfile Of(string language, Dictionary<string, List<string>> phrases) =>
        new(CurrentKind, language, phrases);
}

/// <param name="Added">Quantas frases entraram.</param>
/// <param name="Issues">
/// O que foi recusado, e por quê. Nunca fica só num log: quem importou precisa
/// ver que parte do arquivo não entrou.
/// </param>
/// <param name="LanguageMismatch">
/// O perfil foi escrito para outro idioma. As frases entram — o arquivo é do
/// usuário e ele pode estar preparando o outro idioma — mas isso tem que ser
/// dito, senão ele fala e nada acontece.
/// </param>
public sealed record ProfileImport(
    int Added, IReadOnlyList<PhraseIssue> Issues, bool LanguageMismatch);

public static class PhraseProfiles
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Sem escapar acentos: o arquivo é trocado entre pessoas e lido por elas.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string SuggestedFileName(string language) =>
        $"perfil-ronvoice-{language}.json";

    public static void Export(
        string path, string language, Dictionary<string, List<string>> phrases)
    {
        var clean = phrases
            .Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        File.WriteAllText(path, JsonSerializer.Serialize(
            PhraseProfile.Of(language, clean), Json));
    }

    /// <summary>
    /// Lê e confere o formato. Devolve null com o motivo quando não é um perfil
    /// do RonVoice — importar um JSON qualquer tem que falhar dizendo isso.
    /// </summary>
    public static PhraseProfile? TryRead(string path, out string? problem)
    {
        problem = null;
        try
        {
            var profile = JsonSerializer.Deserialize<PhraseProfile>(
                File.ReadAllText(path), Json);

            if (profile is null || profile.Kind == 0)
            {
                problem = "esse arquivo não é um perfil do RonVoice";
                return null;
            }
            if (profile.Kind > PhraseProfile.CurrentKind)
            {
                problem = $"esse perfil é da versão {profile.Kind} e este RonVoice "
                          + $"entende até a {PhraseProfile.CurrentKind} — atualize o programa";
                return null;
            }
            return profile with { Phrases = profile.Phrases ?? [] };
        }
        catch (JsonException)
        {
            problem = "esse arquivo não é um JSON válido";
            return null;
        }
        catch (IOException ex)
        {
            problem = $"não deu para ler o arquivo: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Soma o perfil ao que já existe, frase por frase, recusando o que colide.
    ///
    /// Soma em vez de substituir porque substituir apagaria calado o trabalho de
    /// quem importou. E a validação é obrigatória: uma frase do perfil de outra
    /// pessoa que caia numa ordem diferente da sua deixaria as DUAS ordens sem
    /// funcionar, sem erro nenhum.
    /// </summary>
    public static ProfileImport Merge(
        CommandMap map, string language, PhraseProfile profile,
        Dictionary<string, List<string>> into)
    {
        var issues = new List<PhraseIssue>();
        var added = 0;

        foreach (var (orderId, phrases) in profile.Phrases)
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
                if (CustomPhraseStore.Reject(map, orderId, phrase ?? "", language, into)
                    is { } reason)
                {
                    issues.Add(new PhraseIssue(
                        PhraseIssueKind.Collision, orderId, phrase ?? "",
                        $"\"{phrase}\" não entrou: {reason}"));
                    continue;
                }

                if (!into.TryGetValue(orderId, out var list)) into[orderId] = list = [];
                list.Add(phrase!.Trim());
                added++;
            }
        }

        return new ProfileImport(
            added, issues,
            LanguageMismatch: !string.Equals(
                profile.Language, language, StringComparison.OrdinalIgnoreCase));
    }
}
