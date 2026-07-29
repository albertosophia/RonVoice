using RonVoice.Core.Speech;

namespace RonVoice.App.ViewModels;

/// <summary>
/// Traduz o resultado interno do teste para o que a pessoa lê. O veredito diz o
/// que fazer, não o que houve: os nomes internos são termos nossos e não ajudam
/// quem acabou de instalar.
/// </summary>
public sealed class TestViewModel : ObservableBase
{
    bool _isRecording;
    double _level;
    string _verdict = "";
    string _detail = "";
    bool _succeeded;
    bool _hasResult;

    public bool IsRecording { get => _isRecording; private set => Set(ref _isRecording, value); }
    public double Level { get => _level; set => Set(ref _level, value); }
    public string Verdict { get => _verdict; private set => Set(ref _verdict, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public bool Succeeded { get => _succeeded; private set => Set(ref _succeeded, value); }
    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }

    /// <summary>
    /// Alterna gravar e parar. Nasce inerte e é substituído na integração, que
    /// é quem sabe abrir o portão de escuta — durante o teste quem está em foco
    /// é a janela do app, e sem essa exceção nada seria ouvido.
    /// </summary>
    public RelayCommand ToggleRecordingCommand { get; set; } =
        new(_ => { }, _ => false);

    public void BeginRecording()
    {
        IsRecording = true;
        HasResult = false;
        Verdict = "";
        Detail = "";
        Succeeded = false;
        Level = 0;
    }

    public void Show(VoiceTestResult result)
    {
        IsRecording = false;
        HasResult = true;
        Succeeded = result.Outcome == VoiceTestOutcome.Success;

        Verdict = result.Outcome switch
        {
            VoiceTestOutcome.Success =>
                $"Funcionou: {result.Intent!.OrderId}"
                + (result.Intent.Element is { } el ? $"  (elemento {el})" : "")
                + (result.Intent.Queue ? "  (enfileirada)" : ""),

            VoiceTestOutcome.NoAudio =>
                "Não ouvi nada. Confira o microfone selecionado na aba Configuração "
                + "e o volume de entrada do Windows.",

            VoiceTestOutcome.OutOfVocabulary =>
                "Ouvi você, mas não era um comando conhecido. "
                + "Veja a aba Comandos para as frases aceitas.",

            VoiceTestOutcome.LowConfidence =>
                "Entendi, mas com pouca certeza. Tente falar mais perto do microfone "
                + "ou num ambiente mais silencioso.",

            VoiceTestOutcome.NoMatch =>
                $"Ouvi \"{result.HeardText}\", mas isso não bate com nenhum comando.",

            _ => "",
        };

        Detail = $"texto reconhecido: \"{result.HeardText}\"   ·   "
               + $"confiança: {result.Confidence:0.00}   ·   "
               + $"pico de áudio: {result.PeakLevel:0.00}";
    }
}
