namespace RonVoice.Core.Input;

public enum StepKind { Press, Down, Up }

/// <param name="HoldMs">Tempo entre o down e o up. Ignorado quando Kind != Press.</param>
/// <param name="GapAfterMs">Espera depois do passo, antes do próximo.</param>
public sealed record KeyStep(StepKind Kind, InputToken Token, int HoldMs, int GapAfterMs);
