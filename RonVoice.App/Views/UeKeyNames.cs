using System.Windows.Input;

namespace RonVoice.App.Views;

/// <summary>
/// Tecla do WPF para o nome que o Unreal usa no Input.ini. Precisa bater com o
/// vocabulário do KeyCatalog e dos binds do jogo, senão o aviso de colisão de
/// tecla compara maçãs com laranjas e nunca dispara.
/// </summary>
public static class UeKeyNames
{
    static readonly string[] Digits =
        ["Zero", "One", "Two", "Three", "Four", "Five",
         "Six", "Seven", "Eight", "Nine"];

    public static string From(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => Digits[key - Key.D0],
        >= Key.NumPad0 and <= Key.NumPad9 => "NumPad" + Digits[key - Key.NumPad0],
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.F1 and <= Key.F12 => key.ToString(),

        Key.Space => "SpaceBar",
        Key.Back => "BackSpace",
        Key.Return => "Enter",
        Key.Capital => "CapsLock",
        Key.Escape => "Escape",
        Key.Tab => "Tab",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.Home => "Home",
        Key.End => "End",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Divide => "Divide",
        Key.Multiply => "Multiply",
        Key.Subtract => "Subtract",
        Key.Add => "Add",
        Key.Decimal => "Decimal",
        Key.OemTilde => "Tilde",
        Key.OemMinus => "Hyphen",
        Key.OemPlus => "Equals",
        Key.OemComma => "Comma",
        Key.OemPeriod => "Period",
        Key.OemQuestion => "Slash",
        Key.OemSemicolon => "Semicolon",
        Key.OemQuotes => "Apostrophe",
        Key.OemOpenBrackets => "LeftBracket",
        Key.OemCloseBrackets => "RightBracket",
        Key.OemBackslash or Key.OemPipe => "Backslash",

        _ => key.ToString(),
    };
}
