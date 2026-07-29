using System.Diagnostics;
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

/// <summary>
/// Monta e mantém vivo tudo que o app precisa: pipeline, captura, bandeja,
/// hooks e janela. É wiring, não regra — a §9 do brief mantém a lógica nos
/// view models e no Core, que é onde há teste.
/// </summary>
public sealed class RonVoiceSession : IDisposable
{
    const uint VkM = 0x4D;

    readonly CommandMap _map;
    readonly IReadOnlyDictionary<string, string> _binds;
    readonly CommandResolver _resolver;
    readonly ListenGate _gate;
    readonly VoskSpeechEngine _engine;
    readonly VoicePipeline _pipeline;
    readonly WasapiCapture _capture;
    readonly TrayIcon _tray;
    readonly GlobalHotkey? _hotkey;
    readonly ElementHook? _elementHook;
    readonly MainViewModel _main;
    readonly string _settingsPath;

    AppSettings _settings;
    VoiceTestRunner? _testRunner;
    string[]? _processNames;

    public MainWindow Window { get; }

    RonVoiceSession(
        AppSettings settings, string settingsPath, bool portable, string modelsDir)
    {
        _settings = settings;
        _settingsPath = settingsPath;

        _map = CommandMap.Load(
            Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json"));

        _binds = KeybindReader.FindDefaultIniPath() is { } ini
            ? KeybindReader.Read(ini)
            : new Dictionary<string, string>();

        _resolver = new CommandResolver(_map, _binds);
        _processNames = ProcessNamesFrom(settings);

        _gate = new ListenGate(
            () => ForegroundGuard.IsGameForeground(_processNames),
            isMuted: null,
            mode: settings.Mode == ListenModeSetting.PushToTalk
                ? ListenMode.PushToTalk
                : ListenMode.AlwaysOn);

        _engine = new VoskSpeechEngine(
            ModelLocator.Find(settings.Language, modelsDir),
            GrammarBuilder.Build(_map, settings.Language));

        _pipeline = new VoicePipeline(
            _engine, _gate,
            new PhraseMatcher(_map, settings.Language),
            _resolver,
            new SendInputSender(),
            settings.ConfidenceThreshold);
        _pipeline.Start();

        var devices = WasapiCapture.ListDevices();
        _capture = new WasapiCapture(
            settings.MicrophoneDevice < devices.Count ? settings.MicrophoneDevice : 0);
        _capture.OnAudio += OnAudio;
        _capture.Start();

        _main = new MainViewModel();
        _main.StatusBar.Elevated = ForegroundGuard.IsElevated();
        _main.StatusBar.Portable = portable;
        _main.StatusBar.Language = settings.Language;
        _main.StatusBar.MicrophoneName =
            devices.Count > 0 ? devices[Math.Min(settings.MicrophoneDevice, devices.Count - 1)]
                              : "(nenhum)";
        _main.StatusBar.ListenState = _gate.State;
        _gate.StateChanged += s =>
            Application.Current.Dispatcher.Invoke(() => _main.StatusBar.ListenState = s);

        _main.Commands = new CommandsViewModel(_map);
        _main.Test = new TestViewModel();
        _main.Settings = new SettingsViewModel(settings, devices, _binds);

        WireCommands();

        _tray = new TrayIcon();
        _tray.Show(_gate.State);
        _gate.StateChanged += s => Application.Current.Dispatcher.Invoke(() => _tray.Show(s));
        _tray.MuteRequested += ToggleMute;
        _tray.ExitRequested += () => Application.Current.Shutdown();

        try
        {
            _hotkey = new GlobalHotkey(
                GlobalHotkey.ModControl | GlobalHotkey.ModAlt, VkM);
            _hotkey.Pressed += ToggleMute;
        }
        catch (InvalidOperationException)
        {
            // Outro programa já usa Ctrl+Alt+M. O mute pela bandeja continua.
            _hotkey = null;
        }

        _elementHook = BuildElementHook();

        Window = new MainWindow(_main);
        Window.Closing += (_, args) => { args.Cancel = true; Window.Hide(); };
    }

    public static RonVoiceSession Start(
        AppSettings settings, string settingsPath, bool portable, string modelsDir) =>
        new(settings, settingsPath, portable, modelsDir);

    static string[]? ProcessNamesFrom(AppSettings settings) =>
        settings.GameExecutablePath is { } exe && exe.Length > 0
            ? [GameExecutable.ProcessNameOf(exe)]
            : null;

    ElementHook? BuildElementHook()
    {
        var map = new Dictionary<string, string>();
        foreach (var (action, element) in new[]
        {
            (ActionNames.ForElement("gold"), "gold"),
            (ActionNames.ForElement("blue"), "blue"),
            (ActionNames.ForElement("red"), "red"),
        })
            if (_binds.TryGetValue(action, out var key)) map[key] = element;

        if (map.Count == 0) return null;

        var hook = new ElementHook(map);
        hook.ElementSelected += el =>
            Application.Current.Dispatcher.Invoke(() => _main.StatusBar.ActiveElement = el);
        return hook;
    }

    void OnAudio(ReadOnlyMemory<byte> chunk)
    {
        // Durante o teste o áudio vai para o runner, não para o pipeline: o
        // teste de voz nunca envia tecla ao jogo.
        if (_testRunner is { } runner) runner.Feed(chunk);
        else _pipeline.Push(chunk);
    }

    void ToggleMute()
    {
        _gate.Toggle();
        Application.Current.Dispatcher.Invoke(() =>
        {
            _main.StatusBar.ListenState = _gate.State;
            _tray.Show(_gate.State);
        });
    }

    void WireCommands()
    {
        _main.Commands.SendCommand = new RelayCommand(
            p => _ = SendToGameAsync((OrderRowViewModel)p!),
            _ => GameIsRunning());

        _main.Test.ToggleRecordingCommand = new RelayCommand(_ => ToggleVoiceTest());

        _main.Settings.BrowseCommand = new RelayCommand(_ => BrowseForGame());
        _main.Settings.SaveCommand = new RelayCommand(_ => SaveSettings());
    }

    /// <summary>
    /// Com o executável escolhido, procura o nome exato; sem ele, qualquer
    /// processo cujo nome comece com o prefixo do jogo — que é como o
    /// ForegroundGuard lida com as variações por loja.
    /// </summary>
    bool GameIsRunning() =>
        _processNames is { } names
            ? names.Any(n => Process.GetProcessesByName(n).Length > 0)
            : Process.GetProcesses().Any(p =>
                p.ProcessName.StartsWith(
                    ForegroundGuard.GameProcessPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ao clicar, quem está em foco é a janela do app, e o ForegroundGuard —
    /// corretamente — recusaria. Por isso minimiza, devolve o foco ao jogo, e
    /// só então envia.
    /// </summary>
    async Task SendToGameAsync(OrderRowViewModel row)
    {
        Window.WindowState = WindowState.Minimized;
        await Task.Delay(TimeSpan.FromSeconds(3));

        try
        {
            var sequence = _resolver.Resolve(new Intent(null, row.Id, false));
            new SendInputSender().Send(sequence);
        }
        catch (ResolveException ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(ex.Message, "RonVoice",
                    MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    void ToggleVoiceTest()
    {
        if (_testRunner is null)
        {
            // Sem o bypass o portão recusaria todo o áudio: quem está em foco
            // agora é a janela do app, não o jogo.
            _gate.TestBypass = true;
            _main.Test.BeginRecording();

            var runner = new VoiceTestRunner(
                _engine, new PhraseMatcher(_map, _settings.Language),
                _settings.ConfidenceThreshold);
            runner.LevelChanged += level =>
                Application.Current.Dispatcher.Invoke(() => _main.Test.Level = level);
            _testRunner = runner;
        }
        else
        {
            var result = _testRunner.Finish();
            _testRunner = null;
            _gate.TestBypass = false;
            _main.Test.Show(result);
        }
    }

    void BrowseForGame()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o executável do Ready or Not",
            Filter = "Executável (*.exe)|*.exe",
        };
        if (dialog.ShowDialog() == true)
            _main.Settings.GameExecutablePath = dialog.FileName;
    }

    void SaveSettings()
    {
        var updated = _main.Settings.ToSettings();
        SettingsStore.Save(updated, _settingsPath);

        // Aplica a quente o que dá; trocar idioma exigiria recriar modelo e
        // reconhecedor, então isso pede reabrir o app.
        var languageChanged = updated.Language != _settings.Language;
        _settings = updated;
        _processNames = ProcessNamesFrom(updated);
        _gate.Mode = updated.Mode == ListenModeSetting.PushToTalk
            ? ListenMode.PushToTalk : ListenMode.AlwaysOn;
        _main.Commands.SendCommand.RaiseCanExecuteChanged();

        MessageBox.Show(
            languageChanged
                ? "Configuração salva. Reabra o RonVoice para trocar o idioma do reconhecimento."
                : "Configuração salva.",
            "RonVoice", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void Dispose()
    {
        _capture.Stop();
        _capture.Dispose();
        _pipeline.Stop();
        _engine.Dispose();
        _elementHook?.Dispose();
        _hotkey?.Dispose();
        _tray.Dispose();
    }
}
