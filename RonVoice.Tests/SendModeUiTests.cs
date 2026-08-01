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

    static CommandsViewModel Catalogue(SendMode modo) =>
        new(Map(), null, null, null, "en",
            limitedByRonSpeech: modo == SendMode.RonSpeech);

    /// <summary>
    /// Quem instala e nao mexe em nada tem que cair no caminho que alcanca as 70
    /// ordens. O RoNSpeech para nas 32 e nao tem como passar disso — o Windows
    /// acaba no F24 — entao ele deixou de ser o padrao.
    /// </summary>
    [Fact]
    public void TheModIsTheDefaultBecauseItIsNowRequired() =>
        Assert.Equal(SendMode.Mailbox, AppSettings.Default.SendMode);

    /// <summary>
    /// Pelo RonVoiceMod nao falta ordem nenhuma: as 65 que passam pelo menu
    /// estao na tabela dele, e as 5 restantes ja' eram tecla direta. O "38 nao
    /// funcionam" some do catalogo, e some porque deixou de ser verdade.
    /// </summary>
    [Fact]
    public void NothingIsMissingOnTheMailboxPath()
    {
        var vm = Catalogue(SendMode.Mailbox);

        Assert.False(vm.HasUnavailable);
        Assert.Equal(0, vm.UnavailableCount);
    }

    [Fact]
    public void NothingIsMarkedUnavailableOnTheMenuPath()
    {
        var vm = Catalogue(SendMode.Menu);

        Assert.False(vm.HasUnavailable);
        Assert.Equal(0, vm.UnavailableCount);
    }

    [Fact]
    public void OnTheModPathTheOrdersItDoesNotCoverAreMarked()
    {
        var vm = Catalogue(SendMode.RonSpeech);

        Assert.True(vm.HasUnavailable);
        Assert.Equal(38, vm.UnavailableCount);
        Assert.Contains("38", vm.UnavailableText);
    }

    [Fact]
    public void AnOrderTheModCoversIsNotMarked()
    {
        var row = Catalogue(SendMode.RonSpeech).Groups
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
        var pending = Catalogue(SendMode.RonSpeech);
        pending.Shown = Availability.Pending;

        var viaMod = pending.Groups.SelectMany(g => g.Orders).First(o => o.Id == id);
        var viaMenu = Catalogue(SendMode.Menu).Groups
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
        Assert.Equal(SendMode.Mailbox, Settings(AppSettings.Default).ToSettings().SendMode);

    // ---- barra de estado ----

    [Fact]
    public void TheStatusBarSaysWhichPathIsInUse()
    {
        var bar = new StatusBarViewModel { Elevated = true };
        Assert.Contains("envio: menu", bar.Summary);

        bar.SendMode = SendMode.RonSpeech;
        Assert.Contains("envio: mod RoNSpeech", bar.Summary);

        bar.SendMode = SendMode.Mailbox;
        Assert.Contains("envio: mod", bar.Summary);
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
