namespace RonVoice.Core.Speech;

public interface ISpeechEngine : IDisposable
{
    event Action<RecognitionResult>? OnRecognized;

    /// <summary>Entrega áudio PCM 16 bits mono a 16 kHz.</summary>
    void Feed(ReadOnlyMemory<byte> audio);

    /// <summary>Fecha o enunciado corrente e publica o resultado final.</summary>
    void Flush();

    /// <summary>Descarta o enunciado em curso sem publicar nada.</summary>
    void Reset();
}
