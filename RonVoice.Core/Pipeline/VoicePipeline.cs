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
    readonly MailboxDelivery? _delivery;
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

    /// <summary>
    /// Abaixo disto a fala é recusada por falta de confiança. Zero desliga.
    ///
    /// Propriedade, e não campo do construtor: quem sobe o limiar na aba
    /// Configuração precisa que valha ao salvar. Preso no construtor, ficava
    /// invisível nos dois sentidos — subir e nada ficar mais exigente, ou baixar
    /// e o app continuar recusando o que a pessoa acabou de liberar.
    /// </summary>
    public double ConfidenceThreshold { get; set; }

    public VoicePipeline(
        ISpeechEngine engine,
        ListenGate gate,
        PhraseMatcher matcher,
        CommandResolver resolver,
        IInputSender sender,
        double confidenceThreshold = 0.0,
        MailboxDelivery? delivery = null)
    {
        _engine = engine;
        _gate = gate;
        _matcher = matcher;
        _resolver = resolver;
        _sender = sender;
        _delivery = delivery;
        ConfidenceThreshold = confidenceThreshold;
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

        if (ConfidenceThreshold > 0 && result.AverageConfidence < ConfidenceThreshold)
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

        if (_resolver.Mode == SendMode.Mailbox && _delivery is not null && !IsAlreadyAKey(intent))
        {
            SendViaMod(intent, result.Text);
            return;
        }

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

    /// <summary>
    /// Ordem que já é uma tecla do jogo, e não um caminho pelo menu. Não há menu
    /// para o mod pular, então ela não está na tabela dele: mandá-la pela caixa
    /// seria pedir uma coisa que o mod não conhece, e a ordem morreria no
    /// recibo. "Execute" é uma dessas, e é das mais faladas que existem.
    /// </summary>
    /// <summary>
    /// Coisas que já são tecla do jogo e continuam sendo, em qualquer modo:
    ///
    /// escolher elemento, que não tem ordem nenhuma junto — é F5, F6, F7, e é
    /// apertar essa tecla que faz o esquadrão responder em voz alta. Pela caixa
    /// seria pedir uma ordem que não existe: o mod recusa e o jogo nunca fica
    /// sabendo, enquanto a barra do app segue mostrando o elemento escolhido;
    ///
    /// e as ordens cujo caminho já é KEY:, que não passam pelo menu. Não há menu
    /// para o mod pular, e elas nem estão na tabela dele.
    /// </summary>
    bool IsAlreadyAKey(Intent intent) =>
        intent.OrderId is null || _resolver.IsDirectKey(intent.OrderId);

    /// <summary>
    /// Manda pelo mod. Não passa pelo resolvedor: aqui não há tecla nenhuma
    /// para resolver, e mandar tecla junto faria a ordem sair duas vezes — uma
    /// pelo menu, outra pelo mod.
    /// </summary>
    void SendViaMod(Intent intent, string heard)
    {
        // Em DryRun não se escreve o arquivo: por este caminho, escrever JÁ é
        // mandar. É o que deixa a aba de teste ser usada no meio da missão.
        if (DryRun)
        {
            Sent?.Invoke(NoKeys);
            return;
        }

        var entrega = _delivery!.Deliver(intent);

        // O mod é a única ponta que sabe se o jogo agiu, então o que ele
        // responde é o que a tela mostra. Engolir isso traria de volta o
        // silêncio que este caminho existe para acabar.
        if (!entrega.Ok)
        {
            Rejected?.Invoke(new Rejection(RejectionReason.Unresolvable, heard, entrega.Problem));
            return;
        }

        Sent?.Invoke(NoKeys);
    }

    /// <summary>
    /// Pelo mod não sai tecla, e o evento carrega teclas. Vazio é a verdade —
    /// quem escuta mostra a ordem, não a fiação.
    /// </summary>
    static readonly KeySequence NoKeys = new([]);
}
