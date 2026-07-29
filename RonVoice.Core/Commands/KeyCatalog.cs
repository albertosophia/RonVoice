using RonVoice.Core.Input;

namespace RonVoice.Core.Commands;

/// <summary>
/// Nome de tecla do Unreal para scan code do conjunto 1. Nome desconhecido
/// devolve false: a ordem é rejeitada em vez de virar uma tecla plausível.
/// </summary>
public static class KeyCatalog
{
    static ScanCodeToken K(int scan) => new((ushort)scan, false);
    static ScanCodeToken E(int scan) => new((ushort)scan, true);

    static readonly Dictionary<string, InputToken> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // linha de dígitos
        ["One"] = K(0x02), ["Two"] = K(0x03), ["Three"] = K(0x04), ["Four"] = K(0x05),
        ["Five"] = K(0x06), ["Six"] = K(0x07), ["Seven"] = K(0x08), ["Eight"] = K(0x09),
        ["Nine"] = K(0x0A), ["Zero"] = K(0x0B),
        ["Hyphen"] = K(0x0C), ["Equals"] = K(0x0D),

        // letras
        ["Q"] = K(0x10), ["W"] = K(0x11), ["E"] = K(0x12), ["R"] = K(0x13),
        ["T"] = K(0x14), ["Y"] = K(0x15), ["U"] = K(0x16), ["I"] = K(0x17),
        ["O"] = K(0x18), ["P"] = K(0x19),
        ["A"] = K(0x1E), ["S"] = K(0x1F), ["D"] = K(0x20), ["F"] = K(0x21),
        ["G"] = K(0x22), ["H"] = K(0x23), ["J"] = K(0x24), ["K"] = K(0x25),
        ["L"] = K(0x26),
        ["Z"] = K(0x2C), ["X"] = K(0x2D), ["C"] = K(0x2E), ["V"] = K(0x2F),
        ["B"] = K(0x30), ["N"] = K(0x31), ["M"] = K(0x32),

        // pontuação
        ["LeftBracket"] = K(0x1A), ["RightBracket"] = K(0x1B),
        ["Semicolon"] = K(0x27), ["Apostrophe"] = K(0x28), ["Tilde"] = K(0x29),
        ["Backslash"] = K(0x2B), ["Comma"] = K(0x33), ["Period"] = K(0x34),
        ["Slash"] = K(0x35),

        // controle
        ["Escape"] = K(0x01), ["BackSpace"] = K(0x0E), ["Tab"] = K(0x0F),
        ["Enter"] = K(0x1C), ["LeftControl"] = K(0x1D), ["LeftShift"] = K(0x2A),
        ["RightShift"] = K(0x36), ["LeftAlt"] = K(0x38), ["SpaceBar"] = K(0x39),
        ["CapsLock"] = K(0x3A), ["NumLock"] = K(0x45), ["ScrollLock"] = K(0x46),

        // função
        ["F1"] = K(0x3B), ["F2"] = K(0x3C), ["F3"] = K(0x3D), ["F4"] = K(0x3E),
        ["F5"] = K(0x3F), ["F6"] = K(0x40), ["F7"] = K(0x41), ["F8"] = K(0x42),
        ["F9"] = K(0x43), ["F10"] = K(0x44), ["F11"] = K(0x57), ["F12"] = K(0x58),

        // numpad — não são estendidas, exceto Divide e Enter
        ["NumPadSeven"] = K(0x47), ["NumPadEight"] = K(0x48), ["NumPadNine"] = K(0x49),
        ["Subtract"] = K(0x4A),
        ["NumPadFour"] = K(0x4B), ["NumPadFive"] = K(0x4C), ["NumPadSix"] = K(0x4D),
        ["Add"] = K(0x4E),
        ["NumPadOne"] = K(0x4F), ["NumPadTwo"] = K(0x50), ["NumPadThree"] = K(0x51),
        ["NumPadZero"] = K(0x52), ["Decimal"] = K(0x53), ["Multiply"] = K(0x37),

        // estendidas: prefixo E0
        ["RightControl"] = E(0x1D), ["RightAlt"] = E(0x38),
        ["Divide"] = E(0x35), ["NumPadEnter"] = E(0x1C),
        ["Home"] = E(0x47), ["Up"] = E(0x48), ["PageUp"] = E(0x49),
        ["Left"] = E(0x4B), ["Right"] = E(0x4D),
        ["End"] = E(0x4F), ["Down"] = E(0x50), ["PageDown"] = E(0x51),
        ["Insert"] = E(0x52), ["Delete"] = E(0x53),

        // mouse. "MiddleMouse" é a grafia usada em keybind_defaults do JSON.
        ["LeftMouseButton"] = new MouseToken(MouseButton.Left),
        ["RightMouseButton"] = new MouseToken(MouseButton.Right),
        ["MiddleMouseButton"] = new MouseToken(MouseButton.Middle),
        ["MiddleMouse"] = new MouseToken(MouseButton.Middle),
        ["ThumbMouseButton"] = new MouseToken(MouseButton.X1),
        ["ThumbMouseButton2"] = new MouseToken(MouseButton.X2),
    };

    public static bool TryResolve(string ueKeyName, out InputToken token) =>
        Map.TryGetValue(ueKeyName, out token!);

    /// <summary>
    /// Só para os testes: toda tecla que sabemos MANDAR também precisa ser
    /// LEGÍVEL pelo VirtualKeys, senão ela pode ser escolhida como push-to-talk
    /// e o portão nunca abre.
    /// </summary>
    public static IEnumerable<string> Names() => Map.Keys;
}
