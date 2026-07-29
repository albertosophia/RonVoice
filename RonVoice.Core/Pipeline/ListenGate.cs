namespace RonVoice.Core.Pipeline;

public enum ListenState { Listening, Idle, Muted }

/// <summary>
/// Responde "devo processar este áudio agora?". Existe como classe própria porque
/// o microfone fica sempre ligado: esta é a única mitigação contra conversa virar
/// ordem, e precisa ser testável sem jogo e sem microfone.
/// </summary>
public sealed class ListenGate
{
    readonly Func<bool> _isGameForeground;
    readonly Func<bool>? _externalMute;
    bool _muted;
    ListenState _last;

    public ListenGate(Func<bool> isGameForeground, Func<bool>? isMuted = null)
    {
        _isGameForeground = isGameForeground;
        _externalMute = isMuted;
        _last = State;
    }

    public event Action<ListenState>? StateChanged;

    public bool Muted
    {
        get => _externalMute?.Invoke() ?? _muted;
        set { _muted = value; Poll(); }
    }

    public ListenState State =>
        Muted ? ListenState.Muted
        : _isGameForeground() ? ListenState.Listening
        : ListenState.Idle;

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
