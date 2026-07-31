namespace RonVoice.Core.Audio;

/// <summary>
/// Toca um WAV como se fosse o microfone. Síncrono de propósito: Start() só
/// retorna quando o arquivo acabou, o que torna os testes determinísticos.
/// </summary>
public sealed class WavFileCapture(string path, int chunkBytes = 4000) : IAudioCapture
{
    public event Action<ReadOnlyMemory<byte>>? OnAudio;
    public event Action<Exception?>? OnStopped;

    bool _stop;

    public void Start()
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"WAV não encontrado: {path}", path);

        using var fs = File.OpenRead(path);
        SkipToData(fs);

        var buffer = new byte[chunkBytes];
        int read;
        while (!_stop && (read = fs.Read(buffer, 0, buffer.Length)) > 0)
            OnAudio?.Invoke(new ReadOnlyMemory<byte>(buffer, 0, read));

        OnStopped?.Invoke(null);
    }

    public void Stop() => _stop = true;
    public void Dispose() => Stop();

    /// <summary>
    /// Percorre os chunks RIFF até 'data'. Não assume 44 bytes: arquivos gerados
    /// por síntese costumam trazer chunks extras antes do áudio.
    /// </summary>
    static void SkipToData(Stream fs)
    {
        using var r = new BinaryReader(fs, System.Text.Encoding.ASCII, leaveOpen: true);
        if (new string(r.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("não é um arquivo RIFF");
        r.ReadInt32();
        if (new string(r.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("não é um arquivo WAVE");

        while (fs.Position < fs.Length)
        {
            var id = new string(r.ReadChars(4));
            var size = r.ReadInt32();
            if (id == "data") return;
            fs.Seek(size, SeekOrigin.Current);
        }
        throw new InvalidDataException("chunk 'data' não encontrado no WAV");
    }
}
