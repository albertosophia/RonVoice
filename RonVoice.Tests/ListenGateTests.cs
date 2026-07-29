using RonVoice.Core.Pipeline;

namespace RonVoice.Tests;

public class ListenGateTests
{
    [Theory]
    [InlineData(true, false, true)]    // jogo em foco, não mudo -> processa
    [InlineData(false, false, false)]  // jogo fora de foco -> não
    [InlineData(true, true, false)]    // mudo -> não
    [InlineData(false, true, false)]
    public void ProcessesOnlyWhenFocusedAndUnmuted(bool focused, bool muted, bool expected) =>
        Assert.Equal(expected, new ListenGate(() => focused, () => muted).ShouldProcess());

    [Theory]
    [InlineData(true, false, ListenState.Listening)]
    [InlineData(false, false, ListenState.Idle)]
    [InlineData(true, true, ListenState.Muted)]
    [InlineData(false, true, ListenState.Muted)]
    public void ReportsTheStateTheTrayShows(bool focused, bool muted, ListenState expected) =>
        Assert.Equal(expected, new ListenGate(() => focused, () => muted).State);

    [Fact]
    public void ToggleFlipsMuteAndReturnsTheNewValue()
    {
        var gate = new ListenGate(() => true);
        Assert.True(gate.Toggle());
        Assert.True(gate.Muted);
        Assert.False(gate.Toggle());
    }

    [Fact]
    public void RaisesStateChangedOnlyWhenTheStateActuallyChanges()
    {
        var focused = true;
        var gate = new ListenGate(() => focused);
        var states = new List<ListenState>();
        gate.StateChanged += s => states.Add(s);

        gate.Poll();                       // Listening -> sem mudança, nada
        focused = false; gate.Poll();      // -> Idle
        gate.Poll();                       // sem mudança
        focused = true; gate.Poll();       // -> Listening

        Assert.Equal([ListenState.Idle, ListenState.Listening], states);
    }
}
