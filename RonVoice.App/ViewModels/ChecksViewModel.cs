using RonVoice.Core.Startup;

namespace RonVoice.App.ViewModels;

public sealed class ChecksViewModel : ObservableBase
{
    IReadOnlyList<CheckResult> _results = [];
    string _summary = "";
    bool _ready;
    bool _listening;
    double _level;

    public IReadOnlyList<CheckResult> Results
    {
        get => _results;
        private set => Set(ref _results, value);
    }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public bool Ready { get => _ready; private set => Set(ref _ready, value); }
    public bool Listening { get => _listening; private set => Set(ref _listening, value); }
    public double Level { get => _level; set => Set(ref _level, value); }

    public bool HasResults => Results.Count > 0;

    /// <summary>Ligado na integração, que é quem grava e monta as entradas.</summary>
    public RelayCommand RunCommand { get; set; } = new(_ => { }, _ => false);

    /// <summary>
    /// A checagem do microfone é a única que exige a pessoa falar, e é a que mais
    /// importa: responde antes do fato a pergunta "ele está me ouvindo?".
    /// </summary>
    public void BeginMicrophoneTest()
    {
        Listening = true;
        Results = [];
        Raise(nameof(HasResults));
        Summary = "Fale alguma coisa...";
        Ready = false;
        Level = 0;
    }

    public void Show(IReadOnlyList<CheckResult> results)
    {
        Listening = false;
        Results = results;
        Raise(nameof(HasResults));
        Summary = StartupChecks.Summarize(results);
        Ready = results.All(r => r.Status != CheckStatus.Failed);
    }
}
