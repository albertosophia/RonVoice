namespace RonVoice.Core.Input;

/// <summary>
/// Nome de tecla do Unreal para virtual-key code do Windows.
///
/// Não dá para reaproveitar o <c>KeyCatalog</c>: ele guarda scan codes, que são
/// o que o <c>SendInput</c> precisa para MANDAR tecla. Ler o estado de uma tecla
/// é a operação inversa, e o <c>GetAsyncKeyState</c> só fala virtual-key.
/// Confundir os dois não dá erro — devolve o estado da tecla errada.
/// </summary>
public static class VirtualKeys
{
    static readonly Dictionary<string, ushort> Map = Build();

    static Dictionary<string, ushort> Build()
    {
        var m = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // mouse — os botões de polegar são o PTT mais usado
            ["LeftMouseButton"] = 0x01,
            ["RightMouseButton"] = 0x02,
            ["MiddleMouseButton"] = 0x04,
            ["MiddleMouse"] = 0x04,
            ["ThumbMouseButton"] = 0x05,
            ["ThumbMouseButton2"] = 0x06,

            ["BackSpace"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D,
            ["CapsLock"] = 0x14, ["Escape"] = 0x1B, ["SpaceBar"] = 0x20,

            ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
            ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
            ["Insert"] = 0x2D, ["Delete"] = 0x2E,

            ["Multiply"] = 0x6A, ["Add"] = 0x6B, ["Subtract"] = 0x6D,
            ["Decimal"] = 0x6E, ["Divide"] = 0x6F,

            ["NumLock"] = 0x90, ["ScrollLock"] = 0x91,

            ["LeftShift"] = 0xA0, ["RightShift"] = 0xA1,
            ["LeftControl"] = 0xA2, ["RightControl"] = 0xA3,
            ["LeftAlt"] = 0xA4, ["RightAlt"] = 0xA5,

            ["Semicolon"] = 0xBA, ["Equals"] = 0xBB, ["Comma"] = 0xBC,
            ["Hyphen"] = 0xBD, ["Period"] = 0xBE, ["Slash"] = 0xBF,
            ["Tilde"] = 0xC0,
            ["LeftBracket"] = 0xDB, ["Backslash"] = 0xDC,
            ["RightBracket"] = 0xDD, ["Apostrophe"] = 0xDE,

            // O Enter do numpad compartilha o virtual-key com o Enter normal.
            // Para MANDAR tecla eles diferem (E0 1C contra 1C); para LER estado,
            // não há como separar, e o Windows é quem decide isso.
            ["NumPadEnter"] = 0x0D,
        };

        string[] digits =
            ["Zero", "One", "Two", "Three", "Four",
             "Five", "Six", "Seven", "Eight", "Nine"];

        for (var i = 0; i < digits.Length; i++)
        {
            m[digits[i]] = (ushort)(0x30 + i);
            m["NumPad" + digits[i]] = (ushort)(0x60 + i);
        }

        for (var c = 'A'; c <= 'Z'; c++) m[c.ToString()] = c;

        // F1..F12 são 0x70..0x7B e F13..F24 seguem em 0x7C..0x87, contíguos.
        // A faixa alta existe para o mod UE4SS do RoNSpeech, que a usa porque
        // teclado físico nenhum tem essas teclas.
        for (var i = 1; i <= 24; i++) m["F" + i] = (ushort)(0x6F + i);

        return m;
    }

    public static bool TryResolve(string? ueKeyName, out ushort virtualKey)
    {
        virtualKey = 0;
        return ueKeyName is { Length: > 0 } && Map.TryGetValue(ueKeyName, out virtualKey);
    }

    /// <summary>Só para os testes conferirem a cobertura contra o KeyCatalog.</summary>
    public static IEnumerable<string> Names() => Map.Keys;
}
