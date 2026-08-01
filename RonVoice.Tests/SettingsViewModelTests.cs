using RonVoice.App.ViewModels;
using RonVoice.Core.Commands;
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

    /// <summary>
    /// O caminho de envio nao tem interruptor na tela, entao ele so' sobrevive
    /// se a ida e volta preservar. Achatar em "RoNSpeech ou menu" jogaria fora
    /// justamente o modo novo: quem salvasse qualquer outra coisa — o
    /// microfone, o idioma — voltaria calado para as 32 ordens antigas.
    /// </summary>
    [Theory]
    [InlineData(SendMode.Mailbox)]
    [InlineData(SendMode.RonSpeech)]
    [InlineData(SendMode.Menu)]
    public void TheSendPathSurvivesARoundTrip(SendMode modo)
    {
        var vm = Vm(AppSettings.Default with { SendMode = modo });

        Assert.Equal(modo, vm.ToSettings().SendMode);
    }

    /// <summary>
    /// Quem instala e nao mexe em nada tem que cair no caminho que alcanca as
    /// 70 ordens, nao nas 32 do RoNSpeech.
    /// </summary>
    [Fact]
    public void OutOfTheBoxItSendsThroughTheMod() =>
        Assert.Equal(SendMode.Mailbox, AppSettings.Default.SendMode);

    // ---- saber que ha' coisa por salvar ----

    /// <summary>
    /// Recem-aberta, nao ha' nada para salvar. Um botao sempre clicavel nao
    /// distingue "salvei" de "esqueci de salvar", e quem mexe numa caixa e sai
    /// da aba nao tem como saber em qual dos dois esta'.
    /// </summary>
    [Fact]
    public void NothingToSaveWhenNothingChanged() =>
        Assert.False(Vm().HasUnsavedChanges);

    [Theory]
    [InlineData("idioma")]
    [InlineData("microfone")]
    [InlineData("limiar")]
    [InlineData("jogo")]
    [InlineData("push-to-talk")]
    [InlineData("tecla")]
    public void ChangingAnythingCountsAsUnsaved(string campo)
    {
        var vm = Vm();

        switch (campo)
        {
            case "idioma": vm.Language = "pt"; break;
            case "microfone": vm.MicrophoneDevice = 2; break;
            case "limiar": vm.ConfidenceThreshold = 0.4; break;
            case "jogo": vm.GameExecutablePath = @"C:\jogo\ReadyOrNot.exe"; break;
            case "push-to-talk": vm.UsePushToTalk = true; break;
            case "tecla": vm.PushToTalkKey = "MouseButton4"; break;
        }

        Assert.True(vm.HasUnsavedChanges, $"mexer em {campo} devia contar");
    }

    /// <summary>
    /// E voltar ao que era conta como nao ter mexido: quem desfaz na mao nao
    /// deve ficar com um aviso de pendencia que nao existe mais.
    /// </summary>
    [Fact]
    public void PuttingItBackCountsAsUnchanged()
    {
        var vm = Vm();
        var antes = vm.Language;

        vm.Language = "pt";
        vm.Language = antes;

        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void AfterSavingThereIsNothingPendingAgain()
    {
        var vm = Vm();
        vm.Language = "pt";

        vm.MarkSaved();

        Assert.False(vm.HasUnsavedChanges);
    }

    // ---- avisar do reinicio ANTES de salvar ----

    /// <summary>
    /// Trocar o idioma exige reabrir o app: o modelo de voz e a gramatica sao
    /// montados uma vez, na abertura. Hoje isso so' e' dito DEPOIS de salvar,
    /// numa caixa de mensagem — tarde demais para quem so' queria olhar as
    /// opcoes, e invisivel para quem fecha a caixa no automatico.
    /// </summary>
    [Fact]
    public void ChoosingAnotherLanguageWarnsBeforeSaving()
    {
        var vm = Vm(AppSettings.Default with { Language = "en" });

        Assert.False(vm.LanguageNeedsRestart);

        vm.Language = "pt";

        Assert.True(vm.LanguageNeedsRestart);
    }

    [Fact]
    public void TheWarningGoesAwayIfYouChangeItBack()
    {
        var vm = Vm(AppSettings.Default with { Language = "en" });
        vm.Language = "pt";

        vm.Language = "en";

        Assert.False(vm.LanguageNeedsRestart);
    }

    /// <summary>
    /// Salvar nao apaga o aviso: o idioma novo esta' gravado, mas o
    /// reconhecimento continua no antigo ate' reabrir. Some-lo ali faria a tela
    /// dizer que esta' tudo certo quando ainda nao esta'.
    /// </summary>
    [Fact]
    public void SavingDoesNotSilenceTheRestartWarning()
    {
        var vm = Vm(AppSettings.Default with { Language = "en" });
        vm.Language = "pt";

        vm.MarkSaved();

        Assert.True(vm.LanguageNeedsRestart);
        Assert.False(vm.HasUnsavedChanges);
    }
}
