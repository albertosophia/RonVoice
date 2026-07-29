namespace RonVoice.Core.Speech;

public sealed record WordConfidence(string Word, double Confidence);

public sealed record RecognitionResult(
    string Text,
    IReadOnlyList<WordConfidence> Words,
    bool IsFinal)
{
    /// <summary>Média simples. 1.0 quando não há palavras, para não rejeitar vazio.</summary>
    public double AverageConfidence =>
        Words.Count == 0 ? 1.0 : Words.Average(w => w.Confidence);

    /// <summary>
    /// O token [unk] é como o Vosk diz "isto está fora da gramática". Resultado
    /// que o contenha é descartado sem exceção.
    /// </summary>
    public bool ContainsUnknown =>
        Text.Contains(GrammarBuilder.UnknownToken, StringComparison.Ordinal);

    public static RecognitionResult Empty(bool isFinal) => new("", [], isFinal);
}
