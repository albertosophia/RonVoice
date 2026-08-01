using RonVoice.Core.Commands;
using RonVoice.Core.Pipeline;

namespace RonVoice.App.ViewModels;

/// <summary>
/// A linha que responde "por que não está funcionando" antes de qualquer
/// suporte. As três falhas do sistema — sem elevação, microfone errado, jogo
/// fora de foco — são todas invisíveis; esta barra é onde elas ficam ditas.
/// </summary>
public sealed class StatusBarViewModel : ObservableBase
{
    bool _elevated;
    bool _portable = true;
    string _microphoneName = "(nenhum)";
    string _language = "en";
    string? _activeElement;
    string? _talkKeyProblem;
    string? _microphoneProblem;
    SendMode _sendMode = SendMode.Menu;
    ListenState _listenState = ListenState.Idle;

    public bool Elevated
    {
        get => _elevated;
        set { if (Set(ref _elevated, value)) RaiseStatus(); }
    }

    public bool Portable
    {
        get => _portable;
        set { if (Set(ref _portable, value)) RaiseStatus(); }
    }

    public string MicrophoneName
    {
        get => _microphoneName;
        set { if (Set(ref _microphoneName, value)) RaiseStatus(); }
    }

    public string Language
    {
        get => _language;
        set { if (Set(ref _language, value)) RaiseStatus(); }
    }

    public string? ActiveElement
    {
        get => _activeElement;
        set { if (Set(ref _activeElement, value)) RaiseStatus(); }
    }

    /// <summary>
    /// Por onde as ordens estão saindo. Fica na barra porque é a diferença entre
    /// "não funciona" e "não funciona neste modo" — e sem isso quem ligou o modo
    /// do mod não tem como saber que ligou.
    /// </summary>
    public SendMode SendMode
    {
        get => _sendMode;
        set { if (Set(ref _sendMode, value)) RaiseStatus(); }
    }

    /// <summary>
    /// O microfone pedido não está presente e outro está gravando. Fica dito na
    /// barra porque a falha é muda: gravar do dispositivo errado não dá erro,
    /// só silêncio, e o usuário conclui que o reconhecimento é ruim.
    /// </summary>
    public string? MicrophoneProblem
    {
        get => _microphoneProblem;
        set { if (Set(ref _microphoneProblem, value)) RaiseStatus(); }
    }

    /// <summary>
    /// Por que o push-to-talk não tem como funcionar. Fica na barra porque a
    /// falha dele é muda por natureza: sem tecla legível o portão nunca abre,
    /// e o usuário fala, aperta, e nada acontece nem dá erro.
    /// </summary>
    public string? TalkKeyProblem
    {
        get => _talkKeyProblem;
        set { if (Set(ref _talkKeyProblem, value)) RaiseStatus(); }
    }

    public ListenState ListenState
    {
        get => _listenState;
        set
        {
            if (!Set(ref _listenState, value)) return;
            Raise(nameof(StateText));
            RaiseStatus();
        }
    }

    public string StateText => ListenState switch
    {
        ListenState.Listening => "escutando",
        ListenState.Idle => "jogo fora de foco",
        ListenState.Muted => "mudo",
        ListenState.WaitingForKey => "aguardando a tecla",
        _ => "",
    };

    void RaiseStatus()
    {
        Raise(nameof(Chips));
        Raise(nameof(Summary));
    }

    /// <summary>
    /// As fichas da barra, em ordem de quem responde primeiro "por que não está
    /// funcionando". O que está falhando vem na frente: numa linha corrida de
    /// texto o problema ficava no meio e ninguém achava.
    /// </summary>
    public IReadOnlyList<StatusChip> Chips
    {
        get
        {
            var chips = new List<StatusChip>();

            if (!Elevated)
                chips.Add(new StatusChip(
                    "sem elevação — as teclas não chegam ao jogo", Level: ChipLevel.Bad));

            if (MicrophoneProblem is { } m) chips.Add(new StatusChip(m, Level: ChipLevel.Bad));
            if (TalkKeyProblem is { } t) chips.Add(new StatusChip(t, Level: ChipLevel.Bad));

            chips.Add(new StatusChip(
                StateText,
                Level: ListenState == ListenState.Listening ? ChipLevel.Good : ChipLevel.Neutral));

            if (ActiveElement is { } e) chips.Add(new StatusChip("elemento", e));

            chips.Add(new StatusChip("microfone", MicrophoneName));
            chips.Add(new StatusChip("modelo", Language));
            chips.Add(new StatusChip("envio", SendMode switch
            {
                SendMode.Mailbox => "mod",
                SendMode.RonSpeech => "mod RoNSpeech",
                _ => "menu",
            }));

            if (!Portable)
                chips.Add(new StatusChip("configuração fora da pasta do programa"));

            return chips;
        }
    }

    /// <summary>
    /// A mesma informação em uma linha, para o tooltip do ícone da bandeja e para
    /// os testes. A barra usa <see cref="Chips"/>.
    /// </summary>
    public string Summary =>
        string.Join("   ·   ", Chips.Select(c => c.Value is null ? c.Label : $"{c.Label}: {c.Value}"));
}
