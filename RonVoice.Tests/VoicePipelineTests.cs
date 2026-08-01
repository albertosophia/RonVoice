using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>Motor falso: emite o texto que o teste mandar, sem áudio nenhum.</summary>
sealed class FakeSpeechEngine : ISpeechEngine
{
    public event Action<RecognitionResult>? OnRecognized;
    public int Resets { get; private set; }
    public void Feed(ReadOnlyMemory<byte> audio) { }
    public void Flush() { }
    public void Reset() => Resets++;
    public void Dispose() { }

    public void Emit(string text, double confidence = 1.0) =>
        OnRecognized?.Invoke(new RecognitionResult(
            text,
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => new WordConfidence(w, confidence)).ToList(),
            IsFinal: true));
}

sealed class RecordingSender : IInputSender
{
    public List<KeySequence> Sent { get; } = [];
    public void Send(KeySequence sequence, CancellationToken ct = default) => Sent.Add(sequence);
}

public class VoicePipelineTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IReadOnlyDictionary<string, string> Binds() => KeybindReader.Read(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

    static (VoicePipeline Pipeline, FakeSpeechEngine Engine, RecordingSender Sender, List<object> Events)
        Build(bool focused = true, bool muted = false, double threshold = 0.0)
    {
        var engine = new FakeSpeechEngine();
        var sender = new RecordingSender();
        var map = Map();

        var pipeline = new VoicePipeline(
            engine,
            new ListenGate(() => focused, () => muted),
            new PhraseMatcher(map, "en"),
            new CommandResolver(map, Binds()),
            sender,
            confidenceThreshold: threshold);

        var events = new List<object>();
        pipeline.Heard += r => events.Add(r);
        pipeline.Matched += i => events.Add(i);
        pipeline.Rejected += r => events.Add(r);
        pipeline.Sent += s => events.Add(s);
        pipeline.Start();
        return (pipeline, engine, sender, events);
    }

    [Fact]
    public void RecognizedPhraseBecomesKeystrokes()
    {
        var (_, engine, sender, _) = Build();
        engine.Emit("red team open with flashbang");

        var seq = Assert.Single(sender.Sent);
        Assert.Equal(4, seq.Steps.Count);                       // F7, MMB, 2, 2
        Assert.Equal(StepKind.Press, seq.Steps[0].Kind);
    }

    [Fact]
    public void PublishesTheStageEventsInOrder()
    {
        var (_, engine, _, events) = Build();
        engine.Emit("stack left");

        Assert.Collection(events,
            e => Assert.IsType<RecognitionResult>(e),
            e => Assert.IsType<Intent>(e),
            e => Assert.IsType<KeySequence>(e));
    }

    [Fact]
    public void UnknownTokenIsRejectedWithoutSending()
    {
        var (_, engine, sender, events) = Build();
        engine.Emit("[unk]");

        Assert.Empty(sender.Sent);
        Assert.Contains(events, e => e is Rejection { Reason: RejectionReason.Unknown });
    }

    [Fact]
    public void LowConfidenceIsRejectedWithoutSending()
    {
        var (_, engine, sender, events) = Build(threshold: 0.8);
        engine.Emit("stack left", confidence: 0.3);

        Assert.Empty(sender.Sent);
        Assert.Contains(events, e => e is Rejection { Reason: RejectionReason.LowConfidence });
    }

    [Fact]
    public void NoiseThatMatchesNothingIsRejected()
    {
        var (_, engine, sender, events) = Build();
        engine.Emit("banana pudding clock");

        Assert.Empty(sender.Sent);
        Assert.Contains(events, e => e is Rejection { Reason: RejectionReason.NoMatch });
    }

    [Fact]
    public void NothingIsProcessedWhileTheGameIsNotInFocus()
    {
        var (_, engine, sender, _) = Build(focused: false);
        engine.Emit("stack left");
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void NothingIsProcessedWhileMuted()
    {
        var (_, engine, sender, _) = Build(muted: true);
        engine.Emit("stack left");
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void ClosingTheGateResetsTheEngineSoAHalfHeardPhraseCannotCompleteLater()
    {
        var focused = true;
        var engine = new FakeSpeechEngine();
        var map = Map();
        var pipeline = new VoicePipeline(
            engine,
            new ListenGate(() => focused, () => false),
            new PhraseMatcher(map, "en"),
            new CommandResolver(map, Binds()),
            new RecordingSender());
        pipeline.Start();

        Assert.Equal(0, engine.Resets);
        focused = false;
        pipeline.Push(new byte[16]);        // primeiro áudio com o portão fechado
        Assert.Equal(1, engine.Resets);
        pipeline.Push(new byte[16]);        // continua fechado: não reseta de novo
        Assert.Equal(1, engine.Resets);
    }

    [Fact]
    public void TwoOrdersInARowAreBothSentInOrder()
    {
        var (_, engine, sender, _) = Build();
        engine.Emit("stack left");
        engine.Emit("hold");

        Assert.Equal(2, sender.Sent.Count);
    }

    /// <summary>
    /// O limiar tem que valer na hora de salvar, e nao so' na proxima abertura.
    ///
    /// Ele era passado ao construtor e ficava la': quem subia o limiar na aba
    /// Configuracao continuava com o antigo, sem uma linha dizendo isso. Pior,
    /// era invisivel nos dois sentidos — subir e nada ficar mais exigente, ou
    /// baixar e o app continuar recusando o que voce acabou de liberar.
    /// </summary>
    [Fact]
    public void RaisingTheThresholdTakesEffectWithoutReopening()
    {
        var (pipeline, engine, sender, _) = Build();

        engine.Emit("open and clear", confidence: 0.5);
        Assert.NotEmpty(sender.Sent);

        pipeline.ConfidenceThreshold = 0.9;
        sender.Sent.Clear();
        engine.Emit("open and clear", confidence: 0.5);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void LoweringItTakesEffectTheSameWay()
    {
        var (pipeline, engine, sender, _) = Build(threshold: 0.9);

        engine.Emit("open and clear", confidence: 0.5);
        Assert.Empty(sender.Sent);

        pipeline.ConfidenceThreshold = 0.0;
        engine.Emit("open and clear", confidence: 0.5);

        Assert.NotEmpty(sender.Sent);
    }
}
