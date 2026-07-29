using System.IO;
using System.Net.Http;
using RonVoice.Core.Speech;

namespace RonVoice.App.ViewModels;

/// <summary>
/// A tela que aparece antes de tudo numa máquina limpa. São 73 MB de modelo;
/// sem esta tela o usuário público trava antes de começar.
/// </summary>
public sealed class FirstRunViewModel : ObservableBase
{
    double _progress;
    string _statusText = "";
    bool _failed;
    bool _busy;

    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public bool Failed { get => _failed; private set => Set(ref _failed, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public async Task<bool> DownloadAsync(
        string language, string modelsDir, CancellationToken ct = default)
    {
        if (!ModelDownloader.Specs.TryGetValue(language, out var spec))
        {
            Failed = true;
            StatusText = $"idioma sem modelo configurado: {language}";
            return false;
        }

        Busy = true;
        Failed = false;
        Progress = 0;
        StatusText = $"Baixando o modelo de reconhecimento ({spec.Bytes / 1024 / 1024} MB)...";

        try
        {
            var progress = new Progress<double>(p => Progress = p);
            await ModelDownloader.DownloadAsync(spec, modelsDir, progress, ct);
            StatusText = "Pronto.";
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
                                      or InvalidDataException or TaskCanceledException)
        {
            Failed = true;
            // Falhando, o instalador não deixou nada pela metade: ele extrai
            // para pasta temporária e só move depois de validar.
            StatusText = $"Não foi possível baixar: {ex.Message}";
            return false;
        }
        finally { Busy = false; }
    }
}
