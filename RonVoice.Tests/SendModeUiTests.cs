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

    /// <summary>
    /// O mod passou a ser requisito do RonVoice, entao e' o padrao e nao ha
    /// interruptor na tela. O caminho do menu sobrevive no codigo porque em VR
    /// ele nao funciona e oferecer os dois convidava a escolher o que quebra.
    /// </summary>
    [Fact]
    public void TheModIsTheDefaultBecauseItIsNowRequired() =>
        Assert.Equal(SendMode.RonSpeech, AppSettings.Default.SendMode);

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

        // O catálogo abre filtrado em Funcionam, então esta ordem só aparece
        // quando se pede a lista das que ainda não têm tecla no mod.
        var pending = Catalogue(viaMod: true);
        pending.Shown = Availability.Pending;

        var viaMod = pending.Groups.SelectMany(g => g.Orders).First(o => o.Id == id);
        var viaMenu = Catalogue(viaMod: false).Groups
            .SelectMany(g => g.Orders).First(o => o.Id == id);

        Assert.False(viaMod.SupportsRonSpeech);
        Assert.True(viaMod.UnavailableInCurrentMode);
        Assert.False(viaMenu.UnavailableInCurrentMode);
    }

    // ---- aba Configuracao ----

    static SettingsViewModel Settings(AppSettings initial) =>
        new(initial, ["Microfone (WIND)"], new Dictionary<string, string>());

    /// <summary>
    /// Nao ha interruptor, mas a escolha de quem editou o settings.json a mao
    /// tem que sobreviver ao salvar — senao o app a sobrescreveria calado.
    /// </summary>
    [Fact]
    public void AHandEditedMenuChoiceSurvivesSaving()
    {
        var vm = Settings(AppSettings.Default with { SendMode = SendMode.Menu });
        Assert.Equal(SendMode.Menu, vm.ToSettings().SendMode);
    }

    [Fact]
    public void TheModeRoundTripsThroughToSettings() =>
        Assert.Equal(SendMode.RonSpeech, Settings(AppSettings.Default).ToSettings().SendMode);

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
