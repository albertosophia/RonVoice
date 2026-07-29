using RonVoice.App.ViewModels;
using RonVoice.Core.Startup;

namespace RonVoice.Tests;

public class ChecksViewModelTests
{
    static IReadOnlyList<CheckResult> AllOk() => StartupChecks.Run(new(
        Elevated: true, ModelPresent: true, Language: "en",
        MicrophonePeak: 0.5, GameFound: true, InputIniFound: true));

    static IReadOnlyList<CheckResult> WithFailure() => StartupChecks.Run(new(
        Elevated: false, ModelPresent: true, Language: "en",
        MicrophonePeak: 0.5, GameFound: true, InputIniFound: true));

    [Fact]
    public void ShowsEveryCheck()
    {
        var vm = new ChecksViewModel();
        vm.Show(AllOk());
        Assert.Equal(5, vm.Results.Count);
    }

    [Fact]
    public void EverythingOkMeansReady()
    {
        var vm = new ChecksViewModel();
        vm.Show(AllOk());
        Assert.True(vm.Ready);
        Assert.Contains("pronto", vm.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFailureMeansNotReadyAndTheSummarySaysWhy()
    {
        var vm = new ChecksViewModel();
        vm.Show(WithFailure());
        Assert.False(vm.Ready);
        Assert.Contains("administrador", vm.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartingTheMicrophoneTestClearsThePreviousResult()
    {
        var vm = new ChecksViewModel();
        vm.Show(AllOk());
        vm.BeginMicrophoneTest();

        Assert.True(vm.Listening);
        Assert.Empty(vm.Results);
        Assert.Equal(0, vm.Level);
    }

    [Fact]
    public void ShowingAResultStopsTheListening()
    {
        var vm = new ChecksViewModel();
        vm.BeginMicrophoneTest();
        vm.Show(AllOk());
        Assert.False(vm.Listening);
    }
}
