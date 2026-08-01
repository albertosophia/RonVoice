using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
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
        // O nome manda sobre a posição: se o dispositivo salvo ainda existe, a
        // lista aponta para ELE, mesmo que a enumeração tenha se deslocado.
        _microphoneDevice = MicrophoneResolver
            .Resolve(devices, initial.MicrophoneName, initial.MicrophoneDevice).Index;
        _usePushToTalk = initial.Mode == ListenModeSetting.PushToTalk;
        _pushToTalkKey = initial.PushToTalkKey;
        _confidenceThreshold = initial.ConfidenceThreshold;
        SendMode = initial.SendMode;

        // O estado JÁ NORMALIZADO, não o `initial` cru: o view model resolve o
        // microfone por nome, então recém-aberto ele pode divergir do arquivo
        // sem ninguém ter mexido em nada — e a tela abriria dizendo que há coisa
        // por salvar.
        _salvo = ToSettings();
        _idiomaEmUso = initial.Language;

        SaveCommand = new RelayCommand(_ => { }, _ => false);
        BrowseCommand = new RelayCommand(_ => { }, _ => false);
    }

    /// <summary>
    /// O que está gravado no arquivo. A pendência é a DIFERENÇA entre isto e a
    /// tela, e não um sinalizador ligado a cada mexida: um sinalizador não sabe
    /// que você desfez, e ficaria avisando de pendência que não existe mais.
    /// </summary>
    AppSettings _salvo;

    /// <summary>
    /// O idioma com que o reconhecimento está rodando AGORA. Não muda ao salvar
    /// — o modelo e a gramática são montados uma vez, na abertura — então é ele,
    /// e não o arquivo, que diz se ainda falta reabrir.
    /// </summary>
    readonly string _idiomaEmUso;

    /// <summary>Há coisa na tela que ainda não foi para o arquivo.</summary>
    public bool HasUnsavedChanges => ToSettings() != _salvo;

    /// <summary>
    /// O idioma escolhido não é o que está rodando. Aparece assim que a pessoa
    /// escolhe, e não depois de salvar: quem só estava olhando as opções merece
    /// saber o preço antes de pagar.
    ///
    /// Continua aparecendo DEPOIS de salvar, porque salvar não resolve — o
    /// arquivo já tem o idioma novo e o reconhecimento continua no velho.
    /// </summary>
    public bool LanguageNeedsRestart =>
        !string.Equals(Language, _idiomaEmUso, StringComparison.Ordinal);

    /// <summary>
    /// Uma mexida qualquer. Só avisa que a pendência mudou — quem decide se há
    /// pendência é a comparação, não esta chamada.
    /// </summary>
    void Mexeram()
    {
        Raise(nameof(HasUnsavedChanges));
        SaveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Chamado depois de gravar: a tela passa a ser o novo "salvo".</summary>
    public void MarkSaved()
    {
        _salvo = ToSettings();
        Raise(nameof(HasUnsavedChanges));
        SaveCommand.RaiseCanExecuteChanged();
    }

    public IReadOnlyList<string> Microphones { get; }

    public IReadOnlyList<string> Languages { get; } = ["en", "pt"];

    public string Language
    {
        get => _language;
        set { if (Set(ref _language, value)) { Mexeram(); Raise(nameof(LanguageNeedsRestart)); } }
    }

    public int MicrophoneDevice
    {
        get => _microphoneDevice;
        set { if (Set(ref _microphoneDevice, value)) Mexeram(); }
    }

    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set { if (Set(ref _confidenceThreshold, value)) Mexeram(); }
    }

    public string? GameExecutablePath
    {
        get => _gameExecutablePath;
        set
        {
            if (!Set(ref _gameExecutablePath, value)) return;
            Mexeram();
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
        set { if (Set(ref _usePushToTalk, value)) { Mexeram(); Raise(nameof(PushToTalkWarning)); } }
    }

    public string? PushToTalkKey
    {
        get => _pushToTalkKey;
        set { if (Set(ref _pushToTalkKey, value)) { Mexeram(); Raise(nameof(PushToTalkWarning)); } }
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

    /// <summary>
    /// O caminho de envio, só para ida e volta ao arquivo. Não há interruptor na
    /// tela: o mod é requisito. Guardado inteiro, e não achatado num "usa mod ou
    /// não" — são três caminhos, e reduzir a dois jogaria fora justamente o novo.
    /// Quem editar o settings.json à mão tem a escolha preservada.
    /// </summary>
    public SendMode SendMode { get; }

    /// <summary>Ligados na integração, que é quem sabe persistir e reaplicar.</summary>
    public RelayCommand SaveCommand { get; set; }
    public RelayCommand BrowseCommand { get; set; }

    /// <summary>
    /// O nome do dispositivo escolhido, que é o que vale ao reabrir. A posição
    /// vai junto só como desempate: ela se desloca quando um dispositivo entra
    /// ou sai da enumeração, e aí gravaria do microfone do vizinho.
    /// </summary>
    public string? SelectedMicrophoneName =>
        MicrophoneDevice >= 0 && MicrophoneDevice < Microphones.Count
            ? Microphones[MicrophoneDevice]
            : null;

    public AppSettings ToSettings() => new(
        Language,
        GameExecutablePath,
        MicrophoneDevice,
        UsePushToTalk ? ListenModeSetting.PushToTalk : ListenModeSetting.AlwaysOn,
        PushToTalkKey,
        ConfidenceThreshold,
        SelectedMicrophoneName,
        SendMode);
}
