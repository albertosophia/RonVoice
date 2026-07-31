using RonVoice.Core.Audio;

namespace RonVoice.Tests;

public class WavFileCaptureTests
{
    static string MakeWav(int samples)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-{Guid.NewGuid():N}.wav");
        var data = new byte[samples * 2];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data);
        return path;
    }

    [Fact]
    public void EmitsEveryByteOfAudioSkippingTheHeader()
    {
        var path = MakeWav(1000);
        var got = new List<byte>();
        using (var capture = new WavFileCapture(path, chunkBytes: 256))
        {
            capture.OnAudio += chunk => got.AddRange(chunk.ToArray());
            capture.Start();
        }
        Assert.Equal(2000, got.Count);
        Assert.Equal((byte)0, got[0]);
        File.Delete(path);
    }

    [Fact]
    public void RaisesStoppedWhenTheFileEnds()
    {
        var path = MakeWav(100);
        var stopped = false;
        using (var capture = new WavFileCapture(path))
        {
            capture.OnStopped += _ => stopped = true;
            capture.Start();
        }
        Assert.True(stopped);
        File.Delete(path);
    }

    [Fact]
    public void MissingFileThrowsNamingIt()
    {
        var ex = Assert.Throws<FileNotFoundException>(
            () => new WavFileCapture("c:\\nao\\existe\\x.wav").Start());
        Assert.Contains("x.wav", ex.Message);
    }

    /// <summary>
    /// Os WAVs que a síntese do Windows gera trazem chunks antes do 'data',
    /// então pular 44 bytes fixos entregaria lixo ao reconhecedor.
    /// </summary>
    [Fact]
    public void FindsTheDataChunkEvenWithExtraChunksBeforeIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-{Guid.NewGuid():N}.wav");
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var extra = new byte[] { 9, 9, 9, 9 };

        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write("RIFF"u8.ToArray()); w.Write(0); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
            w.Write("LIST"u8.ToArray()); w.Write(extra.Length); w.Write(extra);
            w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data);
        }

        var got = new List<byte>();
        using (var capture = new WavFileCapture(path))
        {
            capture.OnAudio += chunk => got.AddRange(chunk.ToArray());
            capture.Start();
        }
        Assert.Equal(data, got);
        File.Delete(path);
    }
}
