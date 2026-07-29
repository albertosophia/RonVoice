using System.Windows;
using RonVoice.App.ViewModels;
using RonVoice.Core.Config;
using RonVoice.Core.Input;

namespace RonVoice.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var (settings, _, portable) = SettingsStore.Load();

        var main = new MainViewModel();
        main.StatusBar.Elevated = ForegroundGuard.IsElevated();
        main.StatusBar.Portable = portable;
        main.StatusBar.Language = settings.Language;

        new MainWindow(main).Show();
    }
}
