namespace RonVoice.Core.Commands;

/// <summary>
/// Token do mapa para ActionName do Ready or Not. Existe para que nem os
/// dígitos do menu fiquem fixos no código: eles são ações rebindáveis
/// (SwatInputKeyOne..Nine) como qualquer outra.
/// </summary>
public static class ActionNames
{
    public const string OpenSwatCommand = "OpenSwatCommand";
    public const string HoldGoCode = "HoldGoCode";

    static readonly string[] DigitWords =
        ["One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"];

    /// <summary>'1'..'9' -> "SwatInputKeyOne".."SwatInputKeyNine".</summary>
    public static string ForDigit(char digit) =>
        "SwatInputKey" + DigitWords[digit - '1'];

    public static string ForElement(string element) => element switch
    {
        "gold" => "SelectElementGold",
        "blue" => "SelectElementBlue",
        "red" => "SelectElementRed",
        _ => throw new ArgumentOutOfRangeException(nameof(element), element, "elemento desconhecido"),
    };

    /// <summary>
    /// "KEY:NOME" -> ActionName, quando existe um. Devolve null para tokens que
    /// são nome de tecla literal e vão direto ao KeyCatalog.
    /// </summary>
    public static string? ForKeyToken(string token) => token switch
    {
        "KEY:DEFAULT_COMMAND" => "IssueDefaultCommand",
        "KEY:INTERACT" => "Use",
        "KEY:X" => "FireSelect",
        "KEY:C" => "DropChem",
        "KEY:PAGEUP" => "VoteYes",
        _ => null,
    };
}
