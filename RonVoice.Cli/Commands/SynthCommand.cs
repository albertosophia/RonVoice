using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using RonVoice.Core.Commands;
using RonVoice.Core.Speech;

namespace RonVoice.Cli.Commands;

/// <summary>
/// Gera WAVs a partir das frases do mapa usando a síntese do Windows. Serve para
/// exercitar a gramática de forma determinística, sem depender de alguém falar.
/// Voz sintética é limpa demais para medir acerto real — isso é o corpus gravado.
/// </summary>
public static class SynthCommand
{
    public static int Run(string[] args)
    {
        var outDir = Cli.Option(args, "--out")
                     ?? Path.Combine(AppContext.BaseDirectory, "audio");
        var lang = Cli.Option(args, "--lang") ?? "en";
        var single = Cli.Option(args, "--phrase");
        var limitText = Cli.Option(args, "--limit");

        if (limitText is not null
            && !int.TryParse(limitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            Console.Error.WriteLine($"--limit inválido: '{limitText}' (espere um inteiro)");
            return 1;
        }
        var limit = limitText is null
            ? int.MaxValue
            : int.Parse(limitText, NumberStyles.Integer, CultureInfo.InvariantCulture);

        Directory.CreateDirectory(outDir);

        var phrases = single is not null
            ? new List<string> { single }
            : GrammarBuilder.Phrases(CommandMap.Load(Cli.MapPath), lang)
                .Where(p => p != GrammarBuilder.UnknownToken)
                .Take(limit)
                .ToList();

        using var synth = new SpeechSynthesizer();

        var installed = synth.GetInstalledVoices().Where(v => v.Enabled).ToList();
        if (installed.Count == 0)
        {
            Console.Error.WriteLine(
                "nenhuma voz de síntese instalada; não é possível gerar áudio de teste");
            return 6;
        }

        var voice = PickVoice(installed, lang);
        if (voice is not null) synth.SelectVoice(voice);
        Console.WriteLine($"voz: {voice ?? "(padrão do sistema)"}");

        if (voice is null || !VoiceMatchesLanguage(installed, voice, lang))
            Console.Error.WriteLine(
                $"AVISO: nenhuma voz de '{lang}' instalada. O áudio sai com a pronúncia errada "
                + "e não serve para medir reconhecimento nesse idioma.");

        var format = new SpeechAudioFormatInfo(
            16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

        var written = 0;
        foreach (var phrase in phrases)
        {
            var path = Path.Combine(outDir, FileNameFor(phrase) + ".wav");
            synth.SetOutputToWaveFile(path, format);
            synth.Speak(phrase);
            written++;
        }
        synth.SetOutputToNull();

        Console.WriteLine($"{written} arquivos em {outDir}");
        return 0;
    }

    /// <summary>Nome estável e seguro para o sistema de arquivos.</summary>
    public static string FileNameFor(string phrase) =>
        string.Join('_', phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    static string? PickVoice(IReadOnlyList<InstalledVoice> installed, string lang)
    {
        var wanted = lang == "pt" ? "pt" : "en";
        foreach (var v in installed)
            if (v.VoiceInfo.Culture.TwoLetterISOLanguageName
                    .Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return v.VoiceInfo.Name;
        return installed[0].VoiceInfo.Name;
    }

    static bool VoiceMatchesLanguage(
        IReadOnlyList<InstalledVoice> installed, string voiceName, string lang)
    {
        var wanted = lang == "pt" ? "pt" : "en";
        return installed.Any(v =>
            v.VoiceInfo.Name == voiceName
            && v.VoiceInfo.Culture.TwoLetterISOLanguageName
                .Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }
}
