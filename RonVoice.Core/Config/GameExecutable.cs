namespace RonVoice.Core.Config;

/// <summary>
/// Converte o executável que o usuário escolheu no nome de processo que o
/// ForegroundGuard compara. O nome varia por loja: a build Steam chama-se
/// ReadyOrNotSteam-Win64-Shipping, e assumir o nome padrão fez o app descartar
/// todas as ordens em silêncio até isso ser descoberto em jogo.
/// </summary>
public static class GameExecutable
{
    public static string ProcessNameOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("caminho vazio", nameof(path));

        return Path.GetFileNameWithoutExtension(path.Trim());
    }

    /// <summary>
    /// Serve para avisar quem escolher o arquivo errado, não para impedir:
    /// builds futuras podem ter nomes que não previmos.
    /// </summary>
    public static bool LooksLikeReadyOrNot(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && ProcessNameOf(path).StartsWith("ReadyOrNot", StringComparison.OrdinalIgnoreCase);
}
