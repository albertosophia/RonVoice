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

    [Theory]
    // Em PTT, o foco do jogo continua valendo E a tecla precisa estar pressionada.
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void PushToTalkAlsoRequiresTheKey(bool focused, bool keyDown, bool expected)
    {
        var gate = new ListenGate(
            () => focused, () => false, ListenMode.PushToTalk, () => keyDown);
        Assert.Equal(expected, gate.ShouldProcess());
    }

    [Fact]
    public void PushToTalkWithTheKeyUpReportsWaitingForKey()
    {
        var gate = new ListenGate(
            () => true, () => false, ListenMode.PushToTalk, () => false);
        Assert.Equal(ListenState.WaitingForKey, gate.State);
    }

    [Fact]
    public void MuteStillWinsOverPushToTalk()
    {
        var gate = new ListenGate(
            () => true, () => true, ListenMode.PushToTalk, () => true);
        Assert.Equal(ListenState.Muted, gate.State);
        Assert.False(gate.ShouldProcess());
    }

    [Fact]
    public void SwitchingModeAtRuntimeTakesEffect()
    {
        var gate = new ListenGate(() => true, () => false, ListenMode.PushToTalk, () => false);
        Assert.False(gate.ShouldProcess());

        gate.Mode = ListenMode.AlwaysOn;
        Assert.True(gate.ShouldProcess());
    }

    /// <summary>
    /// Na aba de teste quem esta em foco e' a janela do app, nao o jogo. Sem
    /// esta excecao o teste de voz nunca ouviria nada.
    /// </summary>
    [Fact]
    public void TestBypassOpensTheGateRegardlessOfFocusAndMode()
    {
        var gate = new ListenGate(() => false, () => false, ListenMode.PushToTalk, () => false);
        Assert.False(gate.ShouldProcess());

        gate.TestBypass = true;
        Assert.True(gate.ShouldProcess());
        Assert.Equal(ListenState.Listening, gate.State);
    }

    [Fact]
    public void TestBypassDoesNotOverrideMute()
    {
        var gate = new ListenGate(() => true, () => true) { TestBypass = true };
        Assert.False(gate.ShouldProcess());
    }
}
