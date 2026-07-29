namespace RonVoice.Core.Audio;

/// <summary>
/// Nível de áudio de um bloco PCM 16 bits. É o que responde "o microfone está
/// pegando?" sem envolver reconhecimento nenhum — se a barra não se mexe
/// enquanto a pessoa fala, a investigação termina aí.
/// </summary>
public static class AudioLevel
{
    public static double Rms(ReadOnlySpan<byte> pcm16)
    {
        var samples = pcm16.Length / 2;
        if (samples == 0) return 0.0;

        double sum = 0;
        for (var i = 0; i < samples; i++)
        {
            double s = BitConverter.ToInt16(pcm16.Slice(i * 2, 2));
            sum += s * s;
        }

        return Math.Min(1.0, Math.Sqrt(sum / samples) / short.MaxValue);
    }
}
