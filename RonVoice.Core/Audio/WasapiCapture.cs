using NAudio.Wave;

namespace RonVoice.Core.Audio;

/// <summary>
/// Microfone real a 16 kHz mono 16 bits — o formato que o modelo espera, pedido
/// direto ao driver para não precisar reamostrar.
/// </summary>
public sealed class WasapiCapture : IAudioCapture
{
    readonly WaveInEvent _waveIn;
    bool _disposed;

    public event Action<ReadOnlyMemory<byte>>? OnAudio;
    public event Action? OnStopped;

    public WasapiCapture(int deviceNumber = 0)
    {
        if (WaveInEvent.DeviceCount == 0)
            throw new InvalidOperationException(
                "nenhum dispositivo de entrada de áudio encontrado");

        if (deviceNumber < 0 || deviceNumber >= WaveInEvent.DeviceCount)
            throw new ArgumentOutOfRangeException(
                nameof(deviceNumber),
                $"dispositivo {deviceNumber} não existe (há {WaveInEvent.DeviceCount}); "
                + "use --list-devices");

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50,
        };
        _waveIn.DataAvailable += (_, e) =>
            OnAudio?.Invoke(new ReadOnlyMemory<byte>(e.Buffer, 0, e.BytesRecorded));
        _waveIn.RecordingStopped += (_, _) => OnStopped?.Invoke();
    }

    public static IReadOnlyList<string> ListDevices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount)
                  .Select(i => WaveInEvent.GetCapabilities(i).ProductName)
                  .ToList();

    public void Start() => _waveIn.StartRecording();
    public void Stop() => _waveIn.StopRecording();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _waveIn.StopRecording(); }
        catch (Exception) { /* já parado; nada a fazer no descarte */ }
        _waveIn.Dispose();
    }
}
