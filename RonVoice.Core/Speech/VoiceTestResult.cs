using RonVoice.Core.Matching;

namespace RonVoice.Core.Speech;

public enum VoiceTestOutcome
{
    /// <summary>Reconheceu e casou com uma ordem.</summary>
    Success,
    /// <summary>Nenhum áudio acima do silêncio: é problema de microfone.</summary>
    NoAudio,
    /// <summary>Ouviu, mas a fala está fora da gramática.</summary>
    OutOfVocabulary,
    /// <summary>Reconheceu com confiança abaixo do limiar configurado.</summary>
    LowConfidence,
    /// <summary>Ouviu, mas não bate com nenhum comando.</summary>
    NoMatch,
}

public sealed record VoiceTestResult(
    VoiceTestOutcome Outcome,
    string HeardText,
    double Confidence,
    double PeakLevel,
    Intent? Intent);
