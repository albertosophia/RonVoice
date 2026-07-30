using RonVoice.App.ViewModels;
using RonVoice.Core.Commands;
using RonVoice.Core.Config;

namespace RonVoice.Tests;

/// <summary>
/// O modo de envio na interface. O que precisa estar dito na tela é quais
/// ordens o modo escolhido NÃO alcança: no modo do mod, falar uma das que
/// faltam não faz nada, e sem marcação isso pareceria bug do programa.
/// </summary>
public class SendModeUiTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static CommandsViewModel Catalogue(bool viaMod) =>
        new(Map(), null, null, null, "en", sendingViaMod: viaMod);

    [Fact]
    public void TheMenuIsTheDefaultBecauseItNeedsNoModInstalled() =>
        Assert.Equal(SendMode.Menu, AppSettings.Default.SendMode);

    [Fact]
    public void NothingIsMarkedUnavailableOnTheMenuPath()
    {
        var vm = Catalogue(viaMod: false);

        Assert.False(vm.HasUnavailable);
        Assert.Equal(0, vm.UnavailableCount);
    }

    [Fact]
    public void OnTheModPathTheOrdersItDoesNotCoverAreMarked()
    {
        var vm = Catalogue(viaMod: true);

        Assert.True(vm.HasUnavailable);
        Assert.Equal(38, vm.UnavailableCount);
        Assert.Contains("38", vm.UnavailableText);
    }

    [Fact]
    public void AnOrderTheModCoversIsNotMarked()
    {
        var row = Catalogue(viaMod: true).Groups
            .SelectMany(g => g.Orders).First(o => o.Id == "door.open.flashbang");

        Assert.True(row.SupportsRonSpeech);
        Assert.False(row.UnavailableInCurrentMode);
    }

    [Fact]
    public void AnOrderTheModLacksIsMarkedOnlyWhenThatModeIsOn()
    {
        const string id = "door.breach.ram.launcher";

        var viaMod = Catalogue(viaMod: true).Groups
            .SelectMany(g => g.Orders).First(o => o.Id == id);
        var viaMenu = Catalogue(viaMod: false).Groups
            .SelectMany(g => g.Orders).First(o => o.Id == id);

        Assert.False(viaMod.SupportsRonSpeech);
        Assert.True(viaMod.UnavailableInCurrentMode);
        Assert.False(viaMenu.UnavailableInCurrentMode);
    }

    // ---- aba Configuracao ----

    static SettingsViewModel Settings(AppSettings initial) =>
        new(initial, ["Microfone (WIND)"], new Dictionary<string, string>())
        {
            RonSpeechTotal = 70,
            RonSpeechMissing = 38,
        };

    [Fact]
    public void TheCheckboxReflectsTheSavedMode()
    {
        Assert.False(Settings(AppSettings.Default).UseRonSpeech);
        Assert.True(Settings(AppSettings.Default with { SendMode = SendMode.RonSpeech })
            .UseRonSpeech);
    }

    [Fact]
    public void TheModeRoundTripsThroughToSettings()
    {
        var vm = Settings(AppSettings.Default);
        vm.UseRonSpeech = true;

        Assert.Equal(SendMode.RonSpeech, vm.ToSettings().SendMode);
    }

    /// <summary>
    /// O aviso tem que dizer que o mod é necessário E quantas ordens faltam.
    /// Ligar o modo sem o mod instalado não dá erro nenhum: nada acontece.
    /// </summary>
    [Fact]
    public void TurningItOnWarnsAboutTheModAndTheGaps()
    {
        var vm = Settings(AppSettings.Default);
        Assert.Null(vm.RonSpeechWarning);

        vm.UseRonSpeech = true;

        Assert.NotNull(vm.RonSpeechWarning);
        Assert.Contains("RoNSpeech", vm.RonSpeechWarning);
        Assert.Contains("38", vm.RonSpeechWarning);
        Assert.Contains("70", vm.RonSpeechWarning);
    }

    // ---- barra de estado ----

    [Fact]
    public void TheStatusBarSaysWhichPathIsInUse()
    {
        var bar = new StatusBarViewModel { Elevated = true };
        Assert.Contains("envio: menu", bar.Summary);

        bar.SendMode = SendMode.RonSpeech;
        Assert.Contains("envio: mod RoNSpeech", bar.Summary);
    }

    /// <summary>
    /// Trocar o modo tem que valer sem reabrir o app: o pipeline guarda este
    /// mesmo resolvedor, então a propriedade é quem carrega o modo.
    /// </summary>
    [Fact]
    public void TheResolverSwitchesModeWithoutBeingRebuilt()
    {
        var resolver = new CommandResolver(Map(), new Dictionary<string, string>());
        var intent = new RonVoice.Core.Matching.Intent(null, "door.open.flashbang", false);

        var viaMenu = resolver.Resolve(intent);
        resolver.Mode = SendMode.RonSpeech;
        var viaMod = resolver.Resolve(intent);

        Assert.True(viaMenu.Steps.Count > viaMod.Steps.Count);
        Assert.Single(viaMod.Steps);
    }
}
