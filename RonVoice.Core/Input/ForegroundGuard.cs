using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RonVoice.Core.Input;

public static partial class ForegroundGuard
{
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// Prefixo comum a toda distribuição conhecida do jogo: Steam usa
    /// "ReadyOrNotSteam-Win64-Shipping", o build nu usa "ReadyOrNot-Win64-Shipping"
    /// ou só "ReadyOrNot", e a Epic (ou qualquer loja futura) provavelmente cola
    /// seu próprio sufixo do mesmo jeito. Comparar por igualdade exata contra uma
    /// lista curta é a mesma armadilha que o resto do projeto já evita com
    /// keybindings — presumir um conjunto fechado de nomes quando o jogo em si não
    /// garante nenhum. Nenhum outro processo comum plausivelmente começa com
    /// "ReadyOrNot", então o prefixo é seguro sem precisar listar cada loja.
    /// </summary>
    public const string GameProcessPrefix = "ReadyOrNot";

    public static string? ForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;   // processo morreu entre a consulta e o acesso
        }
    }

    /// <summary>
    /// Predicado puro por trás de <see cref="IsGameForeground"/>, separado dela
    /// para poder ser testado sem os P/Invoke de GetForegroundWindow. Sem
    /// <paramref name="processNames"/>, casa por prefixo — cobre Steam, Epic e
    /// variantes futuras. Com <paramref name="processNames"/> (o override de
    /// `--process`), casa só por igualdade exata contra a lista dada: é o
    /// jogador dizendo o nome certo do processo dele, não um palpite nosso.
    /// </summary>
    public static bool Matches(string processName, IReadOnlyCollection<string>? processNames = null) =>
        processNames is null
            ? processName.StartsWith(GameProcessPrefix, StringComparison.OrdinalIgnoreCase)
            : processNames.Any(n => string.Equals(n, processName, StringComparison.OrdinalIgnoreCase));

    public static bool IsGameForeground(IReadOnlyCollection<string>? processNames = null)
    {
        var name = ForegroundProcessName();
        return name is not null && Matches(name, processNames);
    }

    /// <summary>
    /// Se o jogo estiver elevado e nós não, o input não chega e não há erro.
    /// Detectar e avisar é o único remédio.
    /// </summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
