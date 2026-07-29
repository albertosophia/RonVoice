using System.IO;
using System.Windows;
using RonVoice.App.ViewModels;
using RonVoice.App.Views;
using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Config;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.App;

public partial class App : Application
{
    RonVoiceSession? _session;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var (settings, settingsPath, portable) = SettingsStore.Load();
        var modelsDir = ModelLocator.FindModelsDirectory()
                        ?? Path.Combine(AppContext.BaseDirectory, "data", "models");

        // Numa máquina limpa não há modelo: sem esta tela o usuário trava antes
        // de começar, e a lib nativa aborta o processo se receber pasta inválida.
        if (!HasModel(settings.Language, modelsDir))
        {
            var firstRun = new FirstRunView(settings.Language, modelsDir);
            firstRun.ShowDialog();
            if (!firstRun.Succeeded) { Shutdown(); return; }
        }

        try
        {
            _session = RonVoiceSession.Start(settings, settingsPath, portable, modelsDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "RonVoice", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _session.Window.Show();
    }

    static bool HasModel(string language, string modelsDir)
    {
        try { return ModelLocator.LooksLikeAModel(ModelLocator.Find(language, modelsDir)); }
        catch (ModelNotFoundException) { return false; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _session?.Dispose();
        base.OnExit(e);
    }
}
