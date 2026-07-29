using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>
/// Motor que só entrega a frase quando mandam finalizar, como o Vosk faz com
/// FinalResult. É o que o push-to-talk depende: a fala fica pendente enquanto
/// a tecla está segurada e fecha quando ela é solta.
/// </summary>
sealed class PendingSpeechEngine : ISpeechEngine
{
    string _pending = "";

    public event Action<RecognitionResult>? OnRecognized;
    public int Resets { get; private set; }
    public int Flushes { get; private set; }

    public void Speak(string text) => _pending = text;

    public void Feed(ReadOnlyMemory<byte> audio) { }

    public void Flush()
    {
        Flushes++;
        var text = _pending;
        _pending = "";
        OnRecognized?.Invoke(new RecognitionResult(
            text,
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => new WordConfidence(w, 1.0)).ToList(),
            IsFinal: true));
    }

    public void Reset() { Resets++; _pending = ""; }
    public void Dispose() { }
}

public class PushToTalkTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IReadOnlyDictionary<string, string> Binds() => KeybindReader.Read(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

    static readonly ReadOnlyMemory<byte> Audio = new byte[320];

    // ---- o bug: o portão sem sonda de tecla ----

    /// <summary>
    /// Era o bug relatado: o app criava o portão em push-to-talk e esquecia a
    /// sonda. State respondia WaitingForKey para sempre, nada era processado, e
    /// não havia erro nenhum — apertar a tecla não tinha como funcionar.
    /// </summary>
    [Fact]
    public void PushToTalkWithoutAKeyProbeIsRefusedLoudly()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ListenGate(() => true, mode: ListenMode.PushToTalk));

        Assert.Contains("isTalkKeyDown", ex.Message);
    }

    [Fact]
    public void SwitchingToPushToTalkLaterIsRefusedToo()
    {
        var gate = new ListenGate(() => true);
        Assert.Throws<InvalidOperationException>(() => gate.Mode = ListenMode.PushToTalk);
    }

    [Fact]
    public void WithAProbeTheGateFollowsTheKey()
    {
        var down = false;
        var gate = new ListenGate(
            () => true, mode: ListenMode.PushToTalk, isTalkKeyDown: () => down);

        Assert.Equal(ListenState.WaitingForKey, gate.State);
        Assert.False(gate.ShouldProcess());

        down = true;
        Assert.Equal(ListenState.Listening, gate.State);
        Assert.True(gate.ShouldProcess());
    }

    // ---- a segunda metade: soltar a tecla nao pode jogar a fala fora ----

    static (VoicePipeline Pipeline, PendingSpeechEngine Engine, RecordingSender Sender)
        Build(Func<bool> focused, Func<bool> keyDown)
    {
        var engine = new PendingSpeechEngine();
        var sender = new RecordingSender();
        var map = Map();

        var pipeline = new VoicePipeline(
            engine,
            new ListenGate(focused, mode: ListenMode.PushToTalk, isTalkKeyDown: keyDown),
            new PhraseMatcher(map, "en"),
            new CommandResolver(map, Binds()),
            sender);
        pipeline.Start();
        return (pipeline, engine, sender);
    }

    /// <summary>
    /// O jogador segura a tecla, fala, solta. A frase tem que sair. Antes o
    /// fechamento do portão chamava Reset e descartava exatamente a fala que
    /// ele segurou a tecla para dizer.
    /// </summary>
    [Fact]
    public void ReleasingTheKeyFinishesTheUtteranceInsteadOfDiscardingIt()
    {
        var down = true;
        var (pipeline, engine, sender) = Build(() => true, () => down);

        pipeline.Push(Audio);
        engine.Speak("stack up");

        down = false;
        pipeline.Push(Audio);

        Assert.Single(sender.Sent);
        Assert.Equal(0, engine.Resets);
    }

    /// <summary>
    /// Perder o foco é o contrário: a frase pela metade dita antes do alt-tab
    /// não pode completar depois e virar ordem sozinha.
    /// </summary>
    [Fact]
    public void LosingFocusStillDiscardsTheUtterance()
    {
        var focused = true;
        var (pipeline, engine, sender) = Build(() => focused, () => true);

        pipeline.Push(Audio);
        engine.Speak("stack up");

        focused = false;
        pipeline.Push(Audio);

        Assert.Empty(sender.Sent);
        Assert.Equal(1, engine.Resets);
    }

    [Fact]
    public void HoldingTheKeyThroughSeveralChunksFinishesOnlyOnce()
    {
        var down = true;
        var (pipeline, engine, sender) = Build(() => true, () => down);

        pipeline.Push(Audio);
        pipeline.Push(Audio);
        engine.Speak("stack up");

        down = false;
        pipeline.Push(Audio);
        pipeline.Push(Audio);

        Assert.Single(sender.Sent);
        Assert.Equal(1, engine.Flushes);
    }

    [Fact]
    public void ReleasingWithNothingSaidSendsNothing()
    {
        var down = true;
        var (pipeline, _, sender) = Build(() => true, () => down);

        pipeline.Push(Audio);
        down = false;
        pipeline.Push(Audio);

        Assert.Empty(sender.Sent);
    }

    // ---- a tecla configurada tem que ser legivel ----

    /// <summary>
    /// Toda tecla que sabemos MANDAR pode ser escolhida como push-to-talk. Se
    /// alguma não for legível, escolhê-la deixa o portão fechado para sempre.
    /// </summary>
    [Fact]
    public void EveryKeyWeCanSendCanAlsoBeRead()
    {
        var unreadable = KeyCatalog.Names()
            .Where(n => !VirtualKeys.TryResolve(n, out _))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unreadable);
    }

    [Theory]
    [InlineData("Tab", 0x09)]
    [InlineData("SpaceBar", 0x20)]
    [InlineData("ThumbMouseButton", 0x05)]
    [InlineData("ThumbMouseButton2", 0x06)]
    [InlineData("F5", 0x74)]
    [InlineData("Z", 0x5A)]
    [InlineData("Five", 0x35)]
    [InlineData("NumPadFive", 0x65)]
    [InlineData("LeftControl", 0xA2)]
    public void ResolvesTheVirtualKey(string name, int expected)
    {
        Assert.True(VirtualKeys.TryResolve(name, out var vk));
        Assert.Equal(expected, vk);
    }

    [Fact]
    public void AnEmptyOrUnknownKeyNameGivesNoProbe()
    {
        Assert.Null(TalkKeyProbe.For(null));
        Assert.Null(TalkKeyProbe.For(""));
        Assert.Null(TalkKeyProbe.For("TeclaQueNaoExiste"));
    }

    [Fact]
    public void AKnownKeyNameGivesAProbe() =>
        Assert.NotNull(TalkKeyProbe.For("Tab"));
}
