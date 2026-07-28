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

    /// <summary>Nomes de processo do jogo, sem extensão.</summary>
    public static readonly string[] GameProcessNames =
        ["ReadyOrNot-Win64-Shipping", "ReadyOrNot"];

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

    public static bool IsGameForeground(IReadOnlyCollection<string>? processNames = null)
    {
        var name = ForegroundProcessName();
        if (name is null) return false;
        return (processNames ?? GameProcessNames)
            .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
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
