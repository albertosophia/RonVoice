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

    public ListenMode Mode { get; set; }

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
            if (Mode == ListenMode.PushToTalk && !(_isTalkKeyDown?.Invoke() ?? false))
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
