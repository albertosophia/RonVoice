namespace RonVoice.Core.Audio;

/// <summary>
/// Fonte de áudio a 16 kHz, mono, PCM 16 bits. Duas implementações: o microfone
/// real e um leitor de arquivo, que é o que torna a etapa testável sem falar.
/// </summary>
public interface IAudioCapture : IDisposable
{
    event Action<ReadOnlyMemory<byte>>? OnAudio;
    /// <param name="error">
    /// Por que parou, quando não foi a pedido. O NAudio entrega isso e o código
    /// jogava fora: sem o motivo, uma captura que morreu sozinha era
    /// indistinguível de uma que terminou — e o app seguia dizendo "escutando".
    /// </param>
    event Action<Exception?>? OnStopped;
    void Start();
    void Stop();
}
