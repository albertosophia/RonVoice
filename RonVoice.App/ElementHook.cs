using System.Runtime.InteropServices;

namespace RonVoice.App;

/// <summary>
/// Observa as teclas de seleção de elemento no teclado inteiro. Existe porque o
/// jogador pode apertar F5/F6/F7 direto, sem falar — e sem observar isso o
/// indicador da barra de estado dessincroniza do jogo. É a exigência da §5.5
/// do brief: o estado de seleção vive no jogo, não no app.
/// </summary>
public sealed partial class ElementHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetWindowsHookExW(
        int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(
        IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    readonly HookProc _proc;          // mantido vivo: o GC coletaria o delegate
    readonly IntPtr _hook;
    readonly Dictionary<uint, string> _byVirtualKey;

    public event Action<string>? ElementSelected;

    /// <param name="keyToElement">
    /// Nome de tecla UE (F5/F6/F7 ou o que o jogador tiver rebindado) para
    /// elemento. Vem dos binds reais lidos do Input.ini.
    /// </param>
    public ElementHook(IReadOnlyDictionary<string, string> keyToElement)
    {
        _byVirtualKey = [];
        foreach (var (keyName, element) in keyToElement)
            if (TryVirtualKey(keyName, out var vk))
                _byVirtualKey[vk] = element;

        _proc = Callback;
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_KEYDOWN)
        {
            var vk = (uint)Marshal.ReadInt32(lParam);
            if (_byVirtualKey.TryGetValue(vk, out var element))
                ElementSelected?.Invoke(element);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Só as teclas que este hook precisa reconhecer.</summary>
    internal static bool TryVirtualKey(string ueKeyName, out uint vk)
    {
        // F1..F12 são 0x70..0x7B
        if (ueKeyName.Length >= 2 && (ueKeyName[0] is 'F' or 'f')
            && int.TryParse(ueKeyName[1..], out var n) && n is >= 1 and <= 12)
        {
            vk = (uint)(0x6F + n);
            return true;
        }

        if (ueKeyName.Length == 1 && char.IsAsciiLetterOrDigit(ueKeyName[0]))
        {
            vk = char.ToUpperInvariant(ueKeyName[0]);
            return true;
        }

        vk = 0;
        return false;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
    }
}
