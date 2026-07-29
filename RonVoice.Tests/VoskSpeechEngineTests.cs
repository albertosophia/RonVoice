using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class VoskSpeechEngineTests
{
    static string ModelsDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "models");
    static string AudioDir => Path.Combine(AppContext.BaseDirectory, "audio");

    static VoskSpeechEngine Engine() => new(
        ModelLocator.Find("en", ModelsDir),
        GrammarBuilder.Build(CommandMap.Load(CommandMapTests.MapPath), "en"));

    static List<RecognitionResult> Run(string wavName)
    {
        var wav = Path.Combine(AudioDir, wavName);
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        var finals = new List<RecognitionResult>();
        using var engine = Engine();
        engine.OnRecognized += r => { if (r.IsFinal) finals.Add(r); };

        using var capture = new WavFileCapture(wav);
        capture.OnAudio += chunk => engine.Feed(chunk);
        capture.OnStopped += () => engine.Flush();
        capture.Start();
        return finals;
    }

    [Fact]
    public void RecognizesAPhraseFromTheGrammar()
    {
        var finals = Run("stack_left.wav");
        Assert.Contains(finals, r => r.Text.Contains("stack left", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsPerWordConfidence()
    {
        var withText = Run("stack_left.wav").First(r => r.Text.Length > 0);
        Assert.NotEmpty(withText.Words);
        Assert.All(withText.Words, w => Assert.InRange(w.Confidence, 0.0, 1.0));
    }

    /// <summary>
    /// É o que acontece quando o portão fecha: uma frase pela metade dita antes
    /// do alt-tab não pode completar depois e virar ordem.
    /// </summary>
    [Fact]
    public void ResetDiscardsAudioAlreadyFed()
    {
        var wav = Path.Combine(AudioDir, "stack_left.wav");
        Assert.True(File.Exists(wav));

        using var engine = Engine();
        var finals = new List<RecognitionResult>();
        engine.OnRecognized += r => { if (r.IsFinal) finals.Add(r); };

        using var capture = new WavFileCapture(wav);
        capture.OnAudio += chunk => engine.Feed(chunk);
        capture.Start();

        engine.Reset();
        engine.Flush();

        Assert.All(finals, r => Assert.Equal("", r.Text));
    }

    [Fact]
    public void MissingModelThrowsBeforeTouchingTheNativeLibrary()
    {
        var ex = Assert.Throws<ModelNotFoundException>(
            () => ModelLocator.Find("en", Path.Combine(Path.GetTempPath(), "sem-modelos-ronvoice")));
        Assert.Contains("sem-modelos-ronvoice", ex.Message);
    }
}
