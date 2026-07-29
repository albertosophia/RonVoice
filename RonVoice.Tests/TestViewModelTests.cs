using RonVoice.App.ViewModels;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class TestViewModelTests
{
    static VoiceTestResult Result(
        VoiceTestOutcome outcome, string heard = "", Intent? intent = null,
        double confidence = 1.0, double peak = 0.5) =>
        new(outcome, heard, confidence, peak, intent);

    /// <summary>
    /// Silencio significa microfone, nao pronuncia. Se o veredito nao disser
    /// isso, a pessoa vai passar a tarde ajustando como fala.
    /// </summary>
    [Fact]
    public void NoAudioPointsAtTheMicrophoneNotThePronunciation()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.NoAudio, peak: 0.0));

        Assert.False(vm.Succeeded);
        Assert.Contains("microfone", vm.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuccessNamesTheOrderThatMatched()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.Success, "stack left",
                       new Intent(null, "door.stack.left", false)));

        Assert.True(vm.Succeeded);
        Assert.Contains("door.stack.left", vm.Verdict);
    }

    [Fact]
    public void SuccessMentionsTheElementAndTheQueueWhenPresent()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.Success, "blue team prep stack left",
                       new Intent("blue", "door.stack.left", true)));

        Assert.Contains("blue", vm.Verdict);
        Assert.Contains("enfileirada", vm.Verdict);
    }

    [Fact]
    public void OutOfVocabularyPointsAtTheCommandsTab()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.OutOfVocabulary, "the quarterly report"));

        Assert.False(vm.Succeeded);
        Assert.Contains("comando", vm.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LowConfidenceSuggestsSomethingActionable()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.LowConfidence, "stack left", confidence: 0.2));

        Assert.False(vm.Succeeded);
        Assert.Contains("microfone", vm.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoMatchQuotesWhatWasHeardSoThePersonCanSeeTheMisreading()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.NoMatch, "banana pudding clock"));

        Assert.False(vm.Succeeded);
        Assert.Contains("banana pudding clock", vm.Verdict);
    }

    [Fact]
    public void DetailAlwaysCarriesTheRawTextAndConfidence()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.Success, "stack left",
                       new Intent(null, "door.stack.left", false), confidence: 0.87));

        Assert.Contains("stack left", vm.Detail);
        Assert.Contains("0.87", vm.Detail);
    }

    [Fact]
    public void RecordingResetsThePreviousVerdict()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.Success, "stack left",
                       new Intent(null, "door.stack.left", false)));
        vm.BeginRecording();

        Assert.True(vm.IsRecording);
        Assert.False(vm.HasResult);
        Assert.Equal("", vm.Verdict);
    }
}
