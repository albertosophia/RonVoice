using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RonVoice.Core.Input;

/// <summary>
/// Um evento INPUT como ele sai — ou sairia — do SendInput, com os campos que o
/// jogo de fato lê. Existe porque errar a §5.1 do brief é silencioso: com wVk
/// preenchido, ou sem KEYEVENTF_SCANCODE, o jogo ignora a tecla e não há erro
/// nenhum para observar. A renderização em prosa do log continuaria idêntica
/// byte a byte. Só comparando estes campos um teste consegue acusar a regressão.
/// </summary>
/// <param name="Vk">wVk do KEYBDINPUT. Tem que ser 0 quando se manda scan code.</param>
/// <param name="Scan">wScan do KEYBDINPUT.</param>
/// <param name="Flags">dwFlags do KEYBDINPUT ou do MOUSEINPUT, conforme Type.</param>
/// <param name="MouseData">mouseData do MOUSEINPUT; 0 para teclado.</param>
/// <param name="AtMs">
/// Quando o evento saiu, em ms desde a criação do sender. É o que torna a §5.2
/// verificável: sem carimbo por evento, um press-and-release no mesmo tick passa
/// em qualquer asserção de tempo total da sequência.
/// </param>
public readonly record struct EmittedInput(
    InputToken Token,
    bool Down,
    uint Type,
    ushort Vk,
    ushort Scan,
    uint Flags,
    uint MouseData,
    double AtMs);

/// <summary>
/// SendInput com scan codes. O jogo é Unreal e lê via RawInput: mensagens de
/// janela e keybd_event são ignoradas sem erro nenhum.
/// </summary>
public sealed partial class SendInputSender : IInputSender
{
    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_SCANCODE = 0x0008;

    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100;
    const uint XBUTTON1 = 0x0001, XBUTTON2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    readonly bool _dryRun;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// Os eventos INPUT emitidos, na ordem, com os campos exatos que foram (ou
    /// seriam) passados ao SendInput. É a fonte de verdade da depuração — o Log
    /// abaixo é só a leitura em prosa dela.
    /// </summary>
    public List<EmittedInput> Events { get; } = [];

    /// <summary>Descrição legível do que foi (ou seria) enviado. Só para depuração.</summary>
    public IReadOnlyList<string> Log =>
        [.. Events.Select(e => $"{(e.Down ? "down" : "up  ")} {Render(e.Token)}")];

    public SendInputSender(bool dryRun = false) => _dryRun = dryRun;

    public void Send(KeySequence sequence, CancellationToken ct = default)
    {
        // Down/Up existem só para o LShift do envelope de fila, e são os únicos
        // passos que deixam uma tecla descida entre iterações. Abortar ou falhar
        // no meio do envelope deixaria o go-code engatado: pela §5.3 do brief,
        // hold_command segurado durante a navegação cancela o menu, então toda
        // ordem seguinte erra em silêncio até o jogador tocar no shift.
        var held = new List<InputToken>();
        try
        {
            foreach (var step in sequence.Steps)
            {
                ct.ThrowIfCancellationRequested();

                switch (step.Kind)
                {
                    case StepKind.Press:
                        Emit(step.Token, down: true);
                        Wait(step.HoldMs);
                        Emit(step.Token, down: false);
                        break;
                    case StepKind.Down:
                        Emit(step.Token, down: true);
                        held.Add(step.Token);
                        break;
                    case StepKind.Up:
                        Emit(step.Token, down: false);
                        held.Remove(step.Token);
                        break;
                }

                Wait(step.GapAfterMs);
            }
        }
        finally
        {
            // No caminho feliz `held` já está vazio. Solta em ordem inversa, como
            // uma pilha, para o dia em que houver mais de um modificador.
            for (var i = held.Count - 1; i >= 0; i--)
            {
                // A tentativa já fica registrada no log antes do SendInput. Uma
                // segunda falha do mesmo SendInput não acrescenta informação e
                // não pode substituir a exceção que nos trouxe até aqui — trocar
                // um cancelamento por um InvalidOperationException perderia o
                // motivo real do aborto.
                try { Emit(held[i], down: false); }
                catch (InvalidOperationException) { }
            }
        }
    }

    void Emit(InputToken token, bool down)
    {
        var input = token switch
        {
            ScanCodeToken s => KeyInput(s, down),
            MouseToken m => MouseInput(m, down),
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        };

        // ki e mi ocupam o mesmo espaço da união: cada tipo é lido pelo campo que
        // é dele. Ler ki.dwFlags de um MOUSEINPUT devolveria mouseData.
        var at = _clock.Elapsed.TotalMilliseconds;
        Events.Add(input.type == INPUT_MOUSE
            ? new EmittedInput(token, down, input.type, 0, 0,
                               input.u.mi.dwFlags, input.u.mi.mouseData, at)
            : new EmittedInput(token, down, input.type, input.u.ki.wVk, input.u.ki.wScan,
                               input.u.ki.dwFlags, 0, at));

        if (_dryRun) return;

        var buffer = new[] { input };
        var sent = SendInput(1, buffer, Marshal.SizeOf<INPUT>());
        if (sent != 1)
            throw new InvalidOperationException(
                $"SendInput rejeitou o evento (erro {Marshal.GetLastWin32Error()})");
    }

    static string Render(InputToken token) => token switch
    {
        ScanCodeToken s => $"scan 0x{s.Scan:X2}{(s.Extended ? " E0" : "")}",
        MouseToken m => $"mouse {m.Button}",
        _ => token.ToString()!,
    };

    static INPUT KeyInput(ScanCodeToken token, bool down)
    {
        var flags = KEYEVENTF_SCANCODE;
        if (token.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (!down) flags |= KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                // wVk = 0 é obrigatório: com scan code, a virtual key tem que ficar vazia.
                ki = new KEYBDINPUT { wVk = 0, wScan = token.Scan, dwFlags = flags },
            },
        };
    }

    static INPUT MouseInput(MouseToken token, bool down)
    {
        uint flags;
        uint data = 0;

        switch (token.Button)
        {
            case MouseButton.Left: flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
            case MouseButton.Right: flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
            case MouseButton.Middle: flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
            case MouseButton.X1: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON1; break;
            case MouseButton.X2: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON2; break;
            default: throw new ArgumentOutOfRangeException(nameof(token));
        }

        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } },
        };
    }

    /// <summary>
    /// Thread.Sleep tem granularidade de ~15 ms no Windows, o que estoura um hold
    /// de 35 ms. Dorme o grosso e faz spin no resto.
    /// </summary>
    static void Wait(int ms)
    {
        if (ms <= 0) return;

        var sw = Stopwatch.StartNew();
        var coarse = ms - 16;
        if (coarse > 0) Thread.Sleep(coarse);
        while (sw.Elapsed.TotalMilliseconds < ms) Thread.SpinWait(50);
    }
}
