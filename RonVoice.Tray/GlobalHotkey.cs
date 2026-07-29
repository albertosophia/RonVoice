using System.Runtime.InteropServices;

namespace RonVoice.Tray;

/// <summary>
/// Atalho global via RegisterHotKey. O mesmo mecanismo que a etapa 6 vai usar
/// para observar F5/F6/F7 e manter o indicador de elemento em sincronia.
/// </summary>
public sealed partial class GlobalHotkey : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_ID = 0xB001;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    public event Action? Pressed;

    public GlobalHotkey(uint modifiers, uint virtualKey)
    {
        CreateHandle(new CreateParams());
        if (!RegisterHotKey(Handle, HOTKEY_ID, modifiers, virtualKey))
            throw new InvalidOperationException(
                $"não foi possível registrar o atalho global (erro {Marshal.GetLastWin32Error()}); "
                + "outro programa provavelmente já o usa");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, HOTKEY_ID);
        DestroyHandle();
    }
}
