namespace RonVoice.Core.Audio;

/// <summary>
/// Fonte de áudio a 16 kHz, mono, PCM 16 bits. Duas implementações: o microfone
/// real e um leitor de arquivo, que é o que torna a etapa testável sem falar.
/// </summary>
public interface IAudioCapture : IDisposable
{
    event Action<ReadOnlyMemory<byte>>? OnAudio;
    event Action? OnStopped;
    void Start();
    void Stop();
}
