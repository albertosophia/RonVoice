using RonVoice.App.ViewModels;
using RonVoice.Core.Config;

namespace RonVoice.Tests;

public class SettingsViewModelTests
{
    static IReadOnlyList<string> Devices() => ["Microfone (WIND)", "CABLE Output", "Voicemeeter"];

    static IReadOnlyDictionary<string, string> Binds() => new Dictionary<string, string>
    {
        ["Crouch"] = "LeftControl",
        ["OpenSwatCommand"] = "MiddleMouseButton",
        ["Walk"] = "LeftShift",
    };

    static SettingsViewModel Vm(AppSettings? initial = null) =>
        new(initial ?? AppSettings.Default, Devices(), Binds());

    [Fact]
    public void StartsFromTheGivenSettings()
    {
        var vm = Vm(AppSettings.Default with { Language = "pt", MicrophoneDevice = 2 });
        Assert.Equal("pt", vm.Language);
        Assert.Equal(2, vm.MicrophoneDevice);
    }

    [Fact]
    public void RoundTripsBackToSettings()
    {
        var vm = Vm();
        vm.Language = "pt";
        vm.MicrophoneDevice = 1;
        vm.ConfidenceThreshold = 0.7;
        vm.UsePushToTalk = true;
        vm.PushToTalkKey = "F8";

        var s = vm.ToSettings();
        Assert.Equal("pt", s.Language);
        Assert.Equal(1, s.MicrophoneDevice);
        Assert.Equal(0.7, s.ConfidenceThreshold);
        Assert.Equal(ListenModeSetting.PushToTalk, s.Mode);
        Assert.Equal("F8", s.PushToTalkKey);
    }

    [Fact]
    public void AlwaysOnIsTheFactoryDefault()
    {
        Assert.False(Vm().UsePushToTalk);
        Assert.Equal(ListenModeSetting.AlwaysOn, Vm().ToSettings().Mode);
    }

    /// <summary>
    /// O nome do processo vem do arquivo escolhido, porque ele varia por loja.
    /// </summary>
    [Fact]
    public void DerivesTheProcessNameFromTheChosenExecutable()
    {
        var vm = Vm();
        vm.GameExecutablePath = @"C:\Steam\ReadyOrNotSteam-Win64-Shipping.exe";
        Assert.Equal("ReadyOrNotSteam-Win64-Shipping", vm.GameProcessName);
    }

    [Fact]
    public void WarnsWhenTheChosenFileDoesNotLookLikeTheGame()
    {
        var vm = Vm();
        vm.GameExecutablePath = @"C:\Windows\notepad.exe";
        Assert.NotNull(vm.GameWarning);
        Assert.Contains("Ready", vm.GameWarning!);
    }

    [Fact]
    public void DoesNotWarnForAPlausibleExecutable()
    {
        var vm = Vm();
        vm.GameExecutablePath = @"C:\Epic\ReadyOrNot-Win64-Shipping.exe";
        Assert.Null(vm.GameWarning);
    }

    /// <summary>
    /// A tecla de PTT nao pode ser uma que o jogo ja usa, ou o jogador agacha
    /// toda vez que fala. Avisa, nao impede: pode ser intencional.
    /// </summary>
    [Fact]
    public void WarnsWhenThePushToTalkKeyCollidesWithAGameBind()
    {
        var vm = Vm();
        vm.UsePushToTalk = true;
        vm.PushToTalkKey = "LeftControl";

        Assert.NotNull(vm.PushToTalkWarning);
        Assert.Contains("Crouch", vm.PushToTalkWarning!);
    }

    [Fact]
    public void DoesNotWarnForAFreeKey()
    {
        var vm = Vm();
        vm.UsePushToTalk = true;
        vm.PushToTalkKey = "F8";
        Assert.Null(vm.PushToTalkWarning);
    }

    [Fact]
    public void NoPushToTalkWarningWhenPushToTalkIsOff()
    {
        var vm = Vm();
        vm.UsePushToTalk = false;
        vm.PushToTalkKey = "LeftControl";
        Assert.Null(vm.PushToTalkWarning);
    }

    [Fact]
    public void ExposesTheDeviceListForTheDropdown() =>
        Assert.Equal(3, Vm().Microphones.Count);
}
