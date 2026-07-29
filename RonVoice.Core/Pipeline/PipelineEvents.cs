namespace RonVoice.Core.Pipeline;

public enum RejectionReason
{
    /// <summary>O resultado continha [unk]: fala fora da gramática.</summary>
    Unknown,
    /// <summary>Confiança média abaixo do limiar configurado.</summary>
    LowConfidence,
    /// <summary>O matcher não casou nada, ou casou de forma ambígua.</summary>
    NoMatch,
    /// <summary>Casou, mas alguma tecla não pôde ser resolvida.</summary>
    Unresolvable,
}

public sealed record Rejection(RejectionReason Reason, string Text, string? Detail = null);
