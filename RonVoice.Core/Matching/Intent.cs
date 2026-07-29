namespace RonVoice.Core.Matching;

/// <summary>
/// Element e OrderId nunca são ambos nulos. Só Element é válido e manda apenas
/// a tecla de seleção — é o que faz "red team" dito sozinho funcionar.
/// </summary>
public sealed record Intent(string? Element, string? OrderId, bool Queue);
