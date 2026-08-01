using RonVoice.App.ViewModels;
using RonVoice.Core.Commands;

namespace RonVoice.Tests;

/// <summary>
/// O filtro de disponibilidade. Existe porque o mod virou requisito e 38 das 70
/// ordens ainda não têm tecla nele: mostrar as 70 de cara deixaria mais da
/// metade da tela inicial marcada sem que nada ali esteja quebrado.
/// </summary>
public class AvailabilityFilterTests
{
    static CommandsViewModel Vm() =>
        new(CommandMap.Load(CommandMapTests.MapPath), null, null, null, "pt",
            limitedByRonSpeech: true);

    static IReadOnlyList<string> Shown(CommandsViewModel vm) =>
        [.. vm.Groups.SelectMany(g => g.Orders).Select(o => o.Id)];

    [Fact]
    public void OpensShowingOnlyWhatWorks()
    {
        var vm = Vm();

        Assert.Equal(Availability.Working, vm.Shown);
        Assert.Equal(32, vm.TotalShown);
        Assert.DoesNotContain("door.breach.ram.clear", Shown(vm));
    }

    [Fact]
    public void ThePendingViewShowsExactlyTheOthers()
    {
        var vm = Vm();
        vm.Shown = Availability.Pending;

        Assert.Equal(38, vm.TotalShown);
        Assert.Contains("door.breach.ram.clear", Shown(vm));
        Assert.DoesNotContain("door.open.flashbang", Shown(vm));
    }

    [Fact]
    public void TheThreeViewsAddUpToEverything()
    {
        var vm = Vm();
        vm.Shown = Availability.Working;
        var working = vm.TotalShown;
        vm.Shown = Availability.Pending;
        var pending = vm.TotalShown;
        vm.Shown = Availability.All;

        Assert.Equal(70, vm.TotalShown);
        Assert.Equal(70, working + pending);
    }

    /// <summary>Busca e filtro se compõem, não competem.</summary>
    [Fact]
    public void SearchNarrowsInsideTheCurrentView()
    {
        var vm = Vm();
        vm.Search = "ariete";

        // "ariete" só existe em ordens que o mod não cobre, então em Funcionam
        // não pode aparecer nada.
        Assert.Empty(Shown(vm));

        vm.Shown = Availability.Pending;
        Assert.NotEmpty(Shown(vm));
        Assert.All(Shown(vm), id => Assert.StartsWith("door.breach.ram", id));
    }

    /// <summary>
    /// A contagem fala do universo do filtro. Em "Ainda não", dizer "3 de 70"
    /// faria pensar que 67 estão escondidas pela busca.
    /// </summary>
    [Fact]
    public void TheCountSpeaksOfTheCurrentViewNotOfAllSeventy()
    {
        var vm = Vm();
        Assert.Equal("32 ordens", vm.CountText);

        vm.Shown = Availability.Pending;
        Assert.Equal("38 ordens", vm.CountText);

        vm.Search = "ariete";
        Assert.Contains("de 38", vm.CountText);
    }

    [Fact]
    public void TheSelectedViewIsReadableByTheScreen()
    {
        var vm = Vm();
        Assert.True(vm.ShowingWorking);

        vm.ShowCommand.Execute("Pending");
        Assert.True(vm.ShowingPending);
        Assert.False(vm.ShowingWorking);

        vm.ShowCommand.Execute("All");
        Assert.True(vm.ShowingAll);
    }

    /// <summary>
    /// Sem ordens fora de alcance o filtro não tem função, e um controle com
    /// "70 / 0 / 70" só ocuparia espaço.
    /// </summary>
    [Fact]
    public void TheFilterIsHiddenWhenEverythingWorks()
    {
        var viaMenu = new CommandsViewModel(
            CommandMap.Load(CommandMapTests.MapPath), null, null, null, "pt");

        Assert.False(viaMenu.CanFilterByAvailability);
        Assert.Equal(70, viaMenu.TotalShown);
    }

    [Fact]
    public void TheKeyThatWillBeSentIsOnTheRow()
    {
        var row = Vm().Groups.SelectMany(g => g.Orders)
            .First(o => o.Id == "door.open.flashbang");

        Assert.Equal("F15", row.KeysText);
    }

    [Fact]
    public void AnOrderWithoutAModKeyShowsNoKey()
    {
        var vm = Vm();
        vm.Shown = Availability.Pending;
        var row = vm.Groups.SelectMany(g => g.Orders)
            .First(o => o.Id == "door.breach.ram.clear");

        Assert.Equal("", row.KeysText);
    }

    [Fact]
    public void AMultiKeyOrderShowsBothKeysInOrder()
    {
        var row = Vm().Groups.SelectMany(g => g.Orders)
            .First(o => o.Id == "deploy.gas");

        Assert.Equal("F13 + F22 + F19", row.KeysText);
    }
}
