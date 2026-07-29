using RonVoice.Core.Commands;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class VoiceTestRunnerTests
{
    static PhraseMatcher Matcher() =>
        new(CommandMap.Load(CommandMapTests.MapPath), "en");

    static byte[] Loud(int samples = 800)
    {
        var b = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
            BitConverter.TryWriteBytes(b.AsSpan(i * 2), (short)(i % 2 == 0 ? 12000 : -12000));
        return b;
    }

    static byte[] Silence(int samples = 800) => new byte[samples * 2];

    [Fact]
    public void RecognizedCommandIsASuccess()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Loud());
        engine.Emit("stack left");

        var result = runner.Finish();
        Assert.Equal(VoiceTestOutcome.Success, result.Outcome);
        Assert.Equal("door.stack.left", result.Intent!.OrderId);
    }

    /// <summary>
    /// Silencio absoluto significa microfone, nao pronuncia. E' a distincao que
    /// a aba de teste existe para fazer.
    /// </summary>
    [Fact]
    public void SilenceIsReportedAsNoAudioEvenIfNothingWasRecognized()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Silence());

        Assert.Equal(VoiceTestOutcome.NoAudio, runner.Finish().Outcome);
    }

    [Fact]
    public void AudioWithoutRecognitionIsNotConfusedWithSilence()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Loud());
        engine.Emit("");

        Assert.Equal(VoiceTestOutcome.NoMatch, runner.Finish().Outcome);
    }

    [Fact]
    public void UnknownTokenIsOutOfVocabulary()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Loud());
        engine.Emit("[unk]");

        Assert.Equal(VoiceTestOutcome.OutOfVocabulary, runner.Finish().Outcome);
    }

    [Fact]
    public void ConfidenceBelowTheThresholdIsReportedAsSuch()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher(), confidenceThreshold: 0.8);
        runner.Feed(Loud());
        engine.Emit("stack left", confidence: 0.2);

        Assert.Equal(VoiceTestOutcome.LowConfidence, runner.Finish().Outcome);
    }

    [Fact]
    public void SpeechThatMatchesNothingIsNoMatch()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Loud());
        engine.Emit("banana pudding clock");

        var result = runner.Finish();
        Assert.Equal(VoiceTestOutcome.NoMatch, result.Outcome);
        Assert.Equal("banana pudding clock", result.HeardText);
    }

    [Fact]
    public void ReportsThePeakLevelSoTheMeterHasSomethingToShow()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        var levels = new List<double>();
        runner.LevelChanged += levels.Add;

        runner.Feed(Silence());
        runner.Feed(Loud());

        Assert.Equal(2, levels.Count);
        Assert.True(runner.PeakLevel > 0.3, $"pico baixo demais: {runner.PeakLevel}");
    }
}
