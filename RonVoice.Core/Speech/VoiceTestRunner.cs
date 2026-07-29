using RonVoice.Core.Audio;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Speech;

/// <summary>
/// O motor de "Testar minha voz". Reconhece e classifica, sem enviar nada ao
/// jogo: é o único ponto do sistema onde um reconhecimento bem-sucedido não
/// produz tecla. Separa duas perguntas que o usuário não distingue sozinho —
/// o microfone está pegando, e a pronúncia está sendo entendida.
/// </summary>
public sealed class VoiceTestRunner
{
    /// <summary>Abaixo disto consideramos que não houve fala, e sim silêncio.</summary>
    const double SilenceFloor = 0.02;

    readonly ISpeechEngine _engine;
    readonly PhraseMatcher _matcher;
    readonly double _confidenceThreshold;
    RecognitionResult? _last;

    public event Action<double>? LevelChanged;

    public double PeakLevel { get; private set; }

    public VoiceTestRunner(
        ISpeechEngine engine, PhraseMatcher matcher, double confidenceThreshold = 0.0)
    {
        _engine = engine;
        _matcher = matcher;
        _confidenceThreshold = confidenceThreshold;
        _engine.OnRecognized += OnRecognized;
    }

    public void Feed(ReadOnlyMemory<byte> audio)
    {
        var level = AudioLevel.Rms(audio.Span);
        if (level > PeakLevel) PeakLevel = level;
        LevelChanged?.Invoke(level);
        _engine.Feed(audio);
    }

    public VoiceTestResult Finish()
    {
        _engine.Flush();
        _engine.OnRecognized -= OnRecognized;

        var heard = _last?.Text ?? "";
        var confidence = _last?.AverageConfidence ?? 0.0;

        // Silêncio vem primeiro: sem áudio, discutir pronúncia não faz sentido.
        if (PeakLevel < SilenceFloor)
            return new VoiceTestResult(
                VoiceTestOutcome.NoAudio, heard, confidence, PeakLevel, null);

        if (_last?.ContainsUnknown == true)
            return new VoiceTestResult(
                VoiceTestOutcome.OutOfVocabulary, heard, confidence, PeakLevel, null);

        if (heard.Length == 0)
            return new VoiceTestResult(
                VoiceTestOutcome.NoMatch, heard, confidence, PeakLevel, null);

        if (_confidenceThreshold > 0 && confidence < _confidenceThreshold)
            return new VoiceTestResult(
                VoiceTestOutcome.LowConfidence, heard, confidence, PeakLevel, null);

        var intent = _matcher.Match(heard);
        return intent is null
            ? new VoiceTestResult(VoiceTestOutcome.NoMatch, heard, confidence, PeakLevel, null)
            : new VoiceTestResult(VoiceTestOutcome.Success, heard, confidence, PeakLevel, intent);
    }

    void OnRecognized(RecognitionResult result)
    {
        if (result.IsFinal && (result.Text.Length > 0 || _last is null)) _last = result;
    }
}
