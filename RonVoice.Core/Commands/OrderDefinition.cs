namespace RonVoice.Core.Commands;

/// <param name="Path">
/// Caminho pelo menu SWAT: abrir o menu e navegar. É o caminho de fábrica, e o
/// único que funciona sem mod nenhum instalado.
/// </param>
/// <param name="RonSpeechKeys">
/// Teclas do mod UE4SS RoNSpeech, que chama as funções do jogo direto e não
/// abre menu. Vazio quando o mod não cobre esta ordem.
///
/// Existe porque em VR o menu é inalcançável: ele abre, os dígitos chegam, e
/// ele não age sobre eles com espera nenhuma entre 60 e 800 ms. Por este
/// caminho o esquadrão obedece em VR — está verificado em jogo.
/// </param>
/// <param name="Name">
/// Título legível, para a tela e a busca. O <paramref name="Id"/> continua sendo
/// a chave — é ele que vai no minhas_frases.json e em qualquer perfil trocado
/// entre pessoas, então renomear nunca pode quebrar arquivo de ninguém.
/// </param>
public sealed record OrderDefinition(
    string Id,
    string Context,
    IReadOnlyList<string> Path,
    bool CloseMenu,
    string Confidence,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Phrases,
    IReadOnlyList<string>? RonSpeechKeys = null,
    string? Name = null)
{
    /// <summary>O nome quando existe, senão o id — a tela nunca fica sem título.</summary>
    public string Title => Name is { Length: > 0 } ? Name : Id;
}

public sealed record ElementDefinition(
    string Name,
    string Key,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases);

public sealed record ModifierDefinition(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases);

public sealed record KeybindDefaults(
    string SwatCommandMenu,
    string DefaultCommand,
    string HoldCommand,
    string Back,
    string SelectGold,
    string SelectBlue,
    string SelectRed,
    IReadOnlyList<string> CommandKeys,
    string InteractYell);

public sealed record TimingSettings(
    int KeyHoldMs,
    int GapBetweenKeysMs,
    int MenuOpenSettleMs);
