using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>
/// O pipeline completo com o Vosk de verdade, dirigido por WAV. Prova que falar
/// produz o mesmo intent que digitar, e — o mais importante — que fala fora do
/// vocabulário não dispara nada.
/// </summary>
public class SpeechIntegrationTests
{
    static string ModelsDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "models");
    static string AudioDir => Path.Combine(AppContext.BaseDirectory, "audio");

    static (List<Intent> Matched, List<KeySequence> Sent, List<Rejection> Rejected) Run(string wavName)
    {
        var wav = Path.Combine(AudioDir, wavName);
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        var map = CommandMap.Load(CommandMapTests.MapPath);
        var binds = KeybindReader.Read(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

        using var engine = new VoskSpeechEngine(
            ModelLocator.Find("en", ModelsDir), GrammarBuilder.Build(map, "en"));

        var sender = new RecordingSender();
        var pipeline = new VoicePipeline(
            engine,
            new ListenGate(() => true, () => false),
            new PhraseMatcher(map, "en"),
            new CommandResolver(map, binds),
            sender);

        var matched = new List<Intent>();
        var rejected = new List<Rejection>();
        pipeline.Matched += matched.Add;
        pipeline.Rejected += rejected.Add;
        pipeline.Start();

        using var capture = new WavFileCapture(wav);
        capture.OnAudio += chunk => pipeline.Push(chunk);
        capture.OnStopped += () => pipeline.Flush();
        capture.Start();

        return (matched, sender.Sent, rejected);
    }

    [Fact]
    public void SpokenPhraseProducesTheSameIntentAsTypedText()
    {
        var (matched, sent, _) = Run("stack_left.wav");

        var typed = new PhraseMatcher(CommandMap.Load(CommandMapTests.MapPath), "en")
            .Match("stack left");

        Assert.Contains(matched, i => i.OrderId == typed!.OrderId);
        Assert.NotEmpty(sent);
    }

    [Fact]
    public void SpokenElementAndOrderResolveTogether()
    {
        var (matched, _, _) = Run("red_team_open_with_flashbang.wav");
        Assert.Contains(matched, i => i is { Element: "red", OrderId: "door.open.flashbang" });
    }

    [Fact]
    public void SpokenQueueModifierResolvesTogetherWithElementAndOrder()
    {
        var (matched, _, _) = Run("blue_team_prep_stack_left.wav");
        Assert.Contains(matched, i => i is { Element: "blue", OrderId: "door.stack.left", Queue: true });
    }

    /// <summary>
    /// O teste mais importante desta etapa. Sem [unk] na gramática, o Vosk força
    /// qualquer áudio para dentro dela e o app passa a mandar ordens sozinho —
    /// e com o microfone sempre ligado isso aconteceria o dia inteiro.
    /// </summary>
    [Fact]
    public void SpeechOutsideTheVocabularySendsNothing()
    {
        var (_, sent, _) = Run("the_quarterly_earnings_report_was_disappointing.wav");
        Assert.Empty(sent);
    }
}
