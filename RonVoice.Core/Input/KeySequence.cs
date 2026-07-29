namespace RonVoice.Core.Input;

/// <summary>
/// Dado puro: carrega o tempo, não o executa. É o que torna a regra de hold
/// de 35 ms testável sem tocar em Win32.
/// </summary>
public sealed record KeySequence(IReadOnlyList<KeyStep> Steps);
