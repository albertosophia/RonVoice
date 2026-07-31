namespace RonVoice.Core.Pipeline;

public enum RejectionReason
{
    /// <summary>O resultado continha [unk]: fala fora da gramática.</summary>
    Unknown,
    /// <summary>Confiança média abaixo do limiar configurado.</summary>
    LowConfidence,
    /// <summary>Ouviu palavras conhecidas, mas nada bate com uma ordem.</summary>
    NoMatch,
    /// <summary>
    /// Ficou entre duas ordens e a margem recusou. Separado do NoMatch de
    /// propósito: aqui ele ENTENDEU, e dizer "não é um comando" mandaria a
    /// pessoa caçar problema de pronúncia que não existe. O que resolve é
    /// dizer a frase de outro jeito, não falar mais claro.
    /// </summary>
    Ambiguous,
    /// <summary>Casou, mas alguma tecla não pôde ser resolvida.</summary>
    Unresolvable,
}

public sealed record Rejection(RejectionReason Reason, string Text, string? Detail = null);
