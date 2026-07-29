namespace RonVoice.Core.Input;

public enum MouseButton { Left, Right, Middle, X1, X2 }

/// <summary>
/// Tecla e botão de mouse são tipos distintos de propósito: geram estruturas
/// INPUT diferentes. Colapsar os dois num ushort é a forma silenciosa de
/// mandar input que o jogo ignora.
/// </summary>
public abstract record InputToken;

/// <param name="Scan">Scan code do conjunto 1.</param>
/// <param name="Extended">Precisa do prefixo E0.</param>
public sealed record ScanCodeToken(ushort Scan, bool Extended) : InputToken;

public sealed record MouseToken(MouseButton Button) : InputToken;
