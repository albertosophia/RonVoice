using System.Runtime.InteropServices;

namespace RonVoice.Core.Input;

/// <summary>
/// Responde "a tecla de falar está pressionada agora?".
///
/// Consulta em vez de hook: o portão de escuta já pergunta a cada bloco de
/// áudio, e a pergunta dele é sobre o instante, não sobre o evento. Um hook
/// global exigiria manter estado de pressionado/solto em paralelo com o
/// Windows, e qualquer tecla perdida deixaria o portão travado.
/// </summary>
public static partial class TalkKeyProbe
{
    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Só o bit alto. O bit baixo do GetAsyncKeyState significa "foi pressionada
    /// desde a última consulta" e é consumido na leitura — usá-lo faria o portão
    /// abrir num piscar a cada toque em vez de ficar aberto enquanto segura.
    /// </summary>
    public static bool IsDown(ushort virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>
    /// A sonda para o nome de tecla configurado, ou null se o nome não é
    /// conhecido. Null é o sinal para a interface dizer que o push-to-talk não
    /// tem como funcionar, em vez de ficar calada e nunca escutar.
    /// </summary>
    public static Func<bool>? For(string? ueKeyName) =>
        VirtualKeys.TryResolve(ueKeyName, out var vk) ? () => IsDown(vk) : null;
}
