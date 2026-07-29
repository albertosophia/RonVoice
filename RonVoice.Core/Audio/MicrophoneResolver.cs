namespace RonVoice.Core.Audio;

/// <param name="Index">Índice a entregar ao WaveIn, já validado.</param>
/// <param name="Name">O dispositivo que vai gravar de verdade.</param>
/// <param name="Problem">
/// Por que não é o dispositivo pedido, ou null quando é. Precisa aparecer na
/// tela: gravar do microfone errado produz silêncio absoluto e nenhum erro.
/// </param>
public sealed record MicrophoneChoice(int Index, string Name, string? Problem);

/// <summary>
/// Escolhe o microfone pelo NOME, não pela posição.
///
/// Índices de WaveIn não são estáveis: eles se deslocam quando um dispositivo
/// entra ou sai da enumeração. Entrar em VR faz exatamente isso — o microfone
/// virtual do streaming aparece e empurra todos os outros — e o índice 6 que
/// era o microfone da mesa passa a ser uma saída de mixer que não carrega voz
/// nenhuma. O app grava silêncio e não há erro em lugar nenhum.
///
/// Guardar o nome resolve o caso normal e, quando o dispositivo pedido não
/// está presente, deixa dizer isso em voz alta em vez de gravar do vizinho.
/// </summary>
public static class MicrophoneResolver
{
    public static MicrophoneChoice Resolve(
        IReadOnlyList<string> devices, string? preferredName, int fallbackIndex = 0)
    {
        if (devices.Count == 0)
            return new MicrophoneChoice(-1, "(nenhum)", "nenhum microfone encontrado");

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var found = IndexOf(devices, preferredName);
            if (found >= 0) return new MicrophoneChoice(found, devices[found], null);

            var (index, name) = Clamp(devices, fallbackIndex);
            return new MicrophoneChoice(
                index, name,
                $"MICROFONE TROCADO — \"{preferredName}\" não está disponível agora; "
                + $"gravando de \"{name}\"");
        }

        // Configuração antiga, que só guardava a posição. Continua funcionando,
        // e o nome passa a ser gravado na próxima vez que salvarem.
        var (i, n) = Clamp(devices, fallbackIndex);
        return new MicrophoneChoice(i, n, null);
    }

    /// <summary>
    /// Nomes vindos do WaveIn são cortados em 31 caracteres pelo Windows, então
    /// a comparação é do jeito que os dois lados vêm da mesma API.
    /// </summary>
    static int IndexOf(IReadOnlyList<string> devices, string name)
    {
        for (var i = 0; i < devices.Count; i++)
            if (string.Equals(devices[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    static (int Index, string Name) Clamp(IReadOnlyList<string> devices, int index)
    {
        var safe = index >= 0 && index < devices.Count ? index : 0;
        return (safe, devices[safe]);
    }
}
