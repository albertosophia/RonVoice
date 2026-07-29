using RonVoice.Core.Audio;

namespace RonVoice.Tests;

public class AudioLevelTests
{
    static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), samples[i]);
        return bytes;
    }

    [Fact]
    public void SilenceIsZero() =>
        Assert.Equal(0.0, AudioLevel.Rms(Pcm(0, 0, 0, 0)), 6);

    [Fact]
    public void FullScaleIsOne() =>
        Assert.Equal(1.0, AudioLevel.Rms(Pcm(short.MaxValue, short.MinValue + 1)), 3);

    [Fact]
    public void HalfScaleIsAboutAHalf() =>
        Assert.InRange(AudioLevel.Rms(Pcm(16384, -16384, 16384, -16384)), 0.45, 0.55);

    [Fact]
    public void EmptyBufferIsZero() =>
        Assert.Equal(0.0, AudioLevel.Rms([]));

    /// <summary>
    /// A captura entrega blocos de tamanho arbitrario; um byte solto no fim nao
    /// pode derrubar o medidor enquanto o usuario esta falando.
    /// </summary>
    [Fact]
    public void AnOddNumberOfBytesDoesNotThrow() =>
        Assert.Equal(0.0, AudioLevel.Rms(new byte[] { 0 }));
}
