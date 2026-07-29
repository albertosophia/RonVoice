namespace RonVoice.Core.Pipeline;

public enum ListenState { Listening, Idle, Muted, WaitingForKey }

public enum ListenMode
{
    /// <summary>Padrão: escuta sempre que o jogo estiver em foco.</summary>
    AlwaysOn,
    /// <summary>Escuta só enquanto a tecla configurada estiver pressionada.</summary>
    PushToTalk,
}

/// <summary>
/// Responde "devo processar este áudio agora?". Existe como classe própria porque
/// no modo padrão o microfone fica sempre ligado: esta é a única mitigação contra
/// conversa virar ordem, e precisa ser testável sem jogo e sem microfone.
/// </summary>
public sealed class ListenGate
{
    readonly Func<bool> _isGameForeground;
    readonly Func<bool>? _externalMute;
    readonly Func<bool>? _isTalkKeyDown;
    bool _muted;
    ListenMode _mode;
    ListenState _last;

    public ListenGate(
        Func<bool> isGameForeground,
        Func<bool>? isMuted = null,
        ListenMode mode = ListenMode.AlwaysOn,
        Func<bool>? isTalkKeyDown = null)
    {
        _isGameForeground = isGameForeground;
        _externalMute = isMuted;
        _isTalkKeyDown = isTalkKeyDown;
        Mode = mode;
        _last = State;
    }

    public event Action<ListenState>? StateChanged;

    /// <summary>
    /// Push-to-talk sem sonda de tecla é recusado de propósito. Sem esta
    /// guarda o portão respondia WaitingForKey para sempre, o app nunca
    /// processava áudio nenhum, e não havia erro em lugar nenhum — apertar a
    /// tecla não tinha como funcionar porque ninguém lia o teclado.
    /// </summary>
    public ListenMode Mode
    {
        get => _mode;
        set
        {
            if (value == ListenMode.PushToTalk && _isTalkKeyDown is null)
                throw new InvalidOperationException(
                    "push-to-talk exige isTalkKeyDown: sem a sonda o portão "
                    + "ficaria fechado para sempre, sem avisar");
            _mode = value;
        }
    }

    /// <summary>
    /// Abre o portão para a aba de teste, onde quem está em foco é a janela do
    /// app e não o jogo. Não vence o mute: silenciar é uma escolha explícita.
    /// </summary>
    public bool TestBypass { get; set; }

    public bool Muted
    {
        get => _externalMute?.Invoke() ?? _muted;
        set { _muted = value; Poll(); }
    }

    public ListenState State
    {
        get
        {
            if (Muted) return ListenState.Muted;
            if (TestBypass) return ListenState.Listening;
            if (!_isGameForeground()) return ListenState.Idle;
            if (Mode == ListenMode.PushToTalk && !_isTalkKeyDown!())
                return ListenState.WaitingForKey;
            return ListenState.Listening;
        }
    }

    public bool ShouldProcess() => State == ListenState.Listening;

    public bool Toggle() { Muted = !Muted; return Muted; }

    /// <summary>Reavalia e publica StateChanged se mudou. Chamado pelo pipeline.</summary>
    public void Poll()
    {
        var now = State;
        if (now == _last) return;
        _last = now;
        StateChanged?.Invoke(now);
    }
}
