using RonVoice.App.ViewModels;
using RonVoice.Core.Pipeline;

namespace RonVoice.Tests;

public class StatusBarViewModelTests
{
    static StatusBarViewModel Vm() => new();

    [Fact]
    public void SummaryNamesEveryPieceOfStateTheUserNeeds()
    {
        var vm = Vm();
        vm.Elevated = true;
        vm.MicrophoneName = "Microfone (WIND)";
        vm.Language = "en";
        vm.ListenState = ListenState.Idle;

        var s = vm.Summary;
        Assert.Contains("Microfone (WIND)", s);
        Assert.Contains("en", s);
    }

    /// <summary>
    /// Sem elevacao nenhuma tecla chega ao jogo e nao ha erro. E' a falha
    /// numero um e ela precisa estar dita, nao inferida.
    /// </summary>
    [Fact]
    public void NotElevatedIsCalledOutExplicitly()
    {
        var vm = Vm();
        vm.Elevated = false;
        Assert.Contains("sem elevação", vm.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotPortableIsCalledOut()
    {
        var vm = Vm();
        vm.Elevated = true;
        vm.Portable = false;
        Assert.Contains("portable", vm.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ListenState.Listening, "escutando")]
    [InlineData(ListenState.Idle, "fora de foco")]
    [InlineData(ListenState.Muted, "mudo")]
    [InlineData(ListenState.WaitingForKey, "tecla")]
    public void EachListenStateHasItsOwnWords(ListenState state, string fragment)
    {
        var vm = Vm();
        vm.Elevated = true;
        vm.ListenState = state;
        Assert.Contains(fragment, vm.StateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RaisesPropertyChangedSoTheBindingUpdates()
    {
        var vm = Vm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ActiveElement = "red";

        Assert.Contains(nameof(vm.ActiveElement), changed);
        Assert.Contains(nameof(vm.Summary), changed);
    }
}
