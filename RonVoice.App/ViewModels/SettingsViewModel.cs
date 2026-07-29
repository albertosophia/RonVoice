using RonVoice.Core.Config;

namespace RonVoice.App.ViewModels;

public sealed class SettingsViewModel : ObservableBase
{
    readonly IReadOnlyDictionary<string, string> _gameBinds;

    string _language;
    string? _gameExecutablePath;
    int _microphoneDevice;
    bool _usePushToTalk;
    string? _pushToTalkKey;
    double _confidenceThreshold;

    public SettingsViewModel(
        AppSettings initial,
        IReadOnlyList<string> devices,
        IReadOnlyDictionary<string, string> gameBinds)
    {
        _gameBinds = gameBinds;
        Microphones = devices;

        _language = initial.Language;
        _gameExecutablePath = initial.GameExecutablePath;
        _microphoneDevice = initial.MicrophoneDevice;
        _usePushToTalk = initial.Mode == ListenModeSetting.PushToTalk;
        _pushToTalkKey = initial.PushToTalkKey;
        _confidenceThreshold = initial.ConfidenceThreshold;

        SaveCommand = new RelayCommand(_ => { }, _ => false);
        BrowseCommand = new RelayCommand(_ => { }, _ => false);
    }

    public IReadOnlyList<string> Microphones { get; }

    public IReadOnlyList<string> Languages { get; } = ["en", "pt"];

    public string Language { get => _language; set => Set(ref _language, value); }

    public int MicrophoneDevice
    {
        get => _microphoneDevice;
        set => Set(ref _microphoneDevice, value);
    }

    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set => Set(ref _confidenceThreshold, value);
    }

    public string? GameExecutablePath
    {
        get => _gameExecutablePath;
        set
        {
            if (!Set(ref _gameExecutablePath, value)) return;
            Raise(nameof(GameProcessName));
            Raise(nameof(GameWarning));
        }
    }

    /// <summary>Vem do arquivo escolhido: o nome varia por loja.</summary>
    public string? GameProcessName =>
        string.IsNullOrWhiteSpace(GameExecutablePath)
            ? null
            : GameExecutable.ProcessNameOf(GameExecutablePath);

    public string? GameWarning =>
        string.IsNullOrWhiteSpace(GameExecutablePath)
            || GameExecutable.LooksLikeReadyOrNot(GameExecutablePath)
                ? null
                : "Esse arquivo não parece ser o Ready or Not. Se for mesmo, pode ignorar.";

    public bool UsePushToTalk
    {
        get => _usePushToTalk;
        set { if (Set(ref _usePushToTalk, value)) Raise(nameof(PushToTalkWarning)); }
    }

    public string? PushToTalkKey
    {
        get => _pushToTalkKey;
        set { if (Set(ref _pushToTalkKey, value)) Raise(nameof(PushToTalkWarning)); }
    }

    /// <summary>
    /// Avisa se a tecla escolhida já é usada pelo jogo — senão o jogador agacha
    /// toda vez que fala. Avisa, não impede: pode ser intencional.
    /// </summary>
    public string? PushToTalkWarning
    {
        get
        {
            if (!UsePushToTalk || string.IsNullOrWhiteSpace(PushToTalkKey)) return null;

            var clash = _gameBinds
                .FirstOrDefault(b => string.Equals(
                    b.Value, PushToTalkKey, StringComparison.OrdinalIgnoreCase));

            return clash.Key is null
                ? null
                : $"O jogo já usa essa tecla para {clash.Key}.";
        }
    }

    /// <summary>Ligados na integração, que é quem sabe persistir e reaplicar.</summary>
    public RelayCommand SaveCommand { get; set; }
    public RelayCommand BrowseCommand { get; set; }

    public AppSettings ToSettings() => new(
        Language,
        GameExecutablePath,
        MicrophoneDevice,
        UsePushToTalk ? ListenModeSetting.PushToTalk : ListenModeSetting.AlwaysOn,
        PushToTalkKey,
        ConfidenceThreshold);
}
