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

    /// <summary>
    /// Deixa passar o resultado de uma finalização feita ao soltar a tecla do
    /// push-to-talk. Sem isso o portão já está fechado quando a frase fica
    /// pronta, e o OnRecognized descartaria exatamente a fala que o jogador
    /// segurou a tecla para dizer.
    /// </summary>
    bool _finishingDeliberateUtterance;

    public event Action<RecognitionResult>? Heard;
    public event Action<Intent>? Matched;
    public event Action<Rejection>? Rejected;
    public event Action<KeySequence>? Sent;

    /// <summary>
    /// Reconhece, casa e resolve normalmente, mas não aperta tecla nenhuma. É o
    /// que deixa a aba de teste ser usada no meio de uma missão sem risco.
    /// </summary>
    public bool DryRun { get; set; }

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
            if (_gateWasOpen)
            {
                // Duas transições opostas, e tratar as duas igual quebrava uma
                // delas. Soltar a tecla do push-to-talk é o FIM de uma fala
                // deliberada: finaliza e deixa virar ordem. Perder o foco ou
                // mutar é o contrário — uma frase pela metade dita antes do
                // alt-tab completaria depois e viraria ordem sozinha.
                if (_gate.State == ListenState.WaitingForKey) FinishUtterance();
                else _engine.Reset();

                _gateWasOpen = false;
            }
            return;
        }
        _gateWasOpen = true;
        _engine.Feed(audio);
    }

    /// <summary>
    /// Fecha a fala em curso e aceita o resultado apesar do portão fechado.
    /// O Flush do motor dispara OnRecognized de forma sincrona, então a bandeira
    /// cobre exatamente essa chamada.
    /// </summary>
    void FinishUtterance()
    {
        _finishingDeliberateUtterance = true;
        try { _engine.Flush(); }
        finally { _finishingDeliberateUtterance = false; }
    }

    public void Flush()
    {
        if (_gate.ShouldProcess()) _engine.Flush();
    }

    void OnRecognized(RecognitionResult result)
    {
        if (!result.IsFinal) return;
        if (!_finishingDeliberateUtterance && !_gate.ShouldProcess()) return;
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

        var detail = _matcher.Explain(result.Text);
        if (detail.Intent is null)
        {
            // Ambíguo NÃO é "não é um comando": ali ele entendeu e a margem
            // recusou de propósito. Quem está falando precisa saber a
            // diferença — uma pede outra frase, a outra pede falar melhor.
            Rejected?.Invoke(detail.Ambiguous
                ? new Rejection(RejectionReason.Ambiguous, result.Text, detail.Closest)
                : new Rejection(RejectionReason.NoMatch, result.Text));
            return;
        }

        var intent = detail.Intent;

        Matched?.Invoke(intent);

        KeySequence sequence;
        try { sequence = _resolver.Resolve(intent); }
        catch (ResolveException ex)
        {
            Rejected?.Invoke(new Rejection(RejectionReason.Unresolvable, result.Text, ex.Message));
            return;
        }

        // DryRun para a tecla antes do envio, mas NÃO antes do evento: a aba de
        // teste precisa mostrar exatamente o que sairia, e é o único jeito de
        // ela dizer a tecla sem mexer no jogo.
        if (!DryRun) _sender.Send(sequence);
        Sent?.Invoke(sequence);
    }
}
