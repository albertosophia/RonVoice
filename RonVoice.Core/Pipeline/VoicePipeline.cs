using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Core.Pipeline;

/// <summary>
/// Liga reconhecimento, casamento, resolução e envio. A UI apenas assina os
/// eventos: nada aqui pode depender dela, ou a latência passa a depender da tela.
/// </summary>
public sealed class VoicePipeline
{
    readonly ISpeechEngine _engine;
    readonly ListenGate _gate;
    readonly PhraseMatcher _matcher;
    readonly CommandResolver _resolver;
    readonly IInputSender _sender;
    readonly double _confidenceThreshold;
    bool _gateWasOpen = true;

    public event Action<RecognitionResult>? Heard;
    public event Action<Intent>? Matched;
    public event Action<Rejection>? Rejected;
    public event Action<KeySequence>? Sent;

    public VoicePipeline(
        ISpeechEngine engine,
        ListenGate gate,
        PhraseMatcher matcher,
        CommandResolver resolver,
        IInputSender sender,
        double confidenceThreshold = 0.0)
    {
        _engine = engine;
        _gate = gate;
        _matcher = matcher;
        _resolver = resolver;
        _sender = sender;
        _confidenceThreshold = confidenceThreshold;
    }

    public void Start() => _engine.OnRecognized += OnRecognized;
    public void Stop() => _engine.OnRecognized -= OnRecognized;

    /// <summary>Entrega áudio. Descarta e reseta enquanto o portão estiver fechado.</summary>
    public void Push(ReadOnlyMemory<byte> audio)
    {
        _gate.Poll();
        if (!_gate.ShouldProcess())
        {
            // Uma frase pela metade dita antes do alt-tab completaria depois e
            // viraria ordem. Reseta uma vez, na transição.
            if (_gateWasOpen) { _engine.Reset(); _gateWasOpen = false; }
            return;
        }
        _gateWasOpen = true;
        _engine.Feed(audio);
    }

    public void Flush()
    {
        if (_gate.ShouldProcess()) _engine.Flush();
    }

    void OnRecognized(RecognitionResult result)
    {
        if (!result.IsFinal) return;
        if (!_gate.ShouldProcess()) return;
        if (result.Text.Length == 0) return;

        Heard?.Invoke(result);

        if (result.ContainsUnknown)
        {
            Rejected?.Invoke(new Rejection(RejectionReason.Unknown, result.Text));
            return;
        }

        if (_confidenceThreshold > 0 && result.AverageConfidence < _confidenceThreshold)
        {
            Rejected?.Invoke(new Rejection(
                RejectionReason.LowConfidence, result.Text,
                result.AverageConfidence.ToString("0.000")));
            return;
        }

        var intent = _matcher.Match(result.Text);
        if (intent is null)
        {
            Rejected?.Invoke(new Rejection(RejectionReason.NoMatch, result.Text));
            return;
        }

        Matched?.Invoke(intent);

        KeySequence sequence;
        try { sequence = _resolver.Resolve(intent); }
        catch (ResolveException ex)
        {
            Rejected?.Invoke(new Rejection(RejectionReason.Unresolvable, result.Text, ex.Message));
            return;
        }

        _sender.Send(sequence);
        Sent?.Invoke(sequence);
    }
}
