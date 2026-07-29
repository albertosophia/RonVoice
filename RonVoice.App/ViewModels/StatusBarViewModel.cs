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
    ListenState _listenState = ListenState.Idle;

    public bool Elevated
    {
        get => _elevated;
        set { if (Set(ref _elevated, value)) Raise(nameof(Summary)); }
    }

    public bool Portable
    {
        get => _portable;
        set { if (Set(ref _portable, value)) Raise(nameof(Summary)); }
    }

    public string MicrophoneName
    {
        get => _microphoneName;
        set { if (Set(ref _microphoneName, value)) Raise(nameof(Summary)); }
    }

    public string Language
    {
        get => _language;
        set { if (Set(ref _language, value)) Raise(nameof(Summary)); }
    }

    public string? ActiveElement
    {
        get => _activeElement;
        set { if (Set(ref _activeElement, value)) Raise(nameof(Summary)); }
    }

    public ListenState ListenState
    {
        get => _listenState;
        set
        {
            if (!Set(ref _listenState, value)) return;
            Raise(nameof(StateText));
            Raise(nameof(Summary));
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

    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                Elevated ? "elevado" : "SEM ELEVAÇÃO — as teclas não chegam ao jogo",
                $"microfone: {MicrophoneName}",
                $"modelo: {Language}",
                StateText,
            };
            if (ActiveElement is { } e) parts.Add($"elemento: {e}");
            if (!Portable) parts.Add("configuração fora da pasta — modo portable desligado");
            return string.Join("   ·   ", parts);
        }
    }
}
