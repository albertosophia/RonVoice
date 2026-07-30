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
using RonVoice.Core.Startup;

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

    /// <summary>
    /// Sonda da tecla de falar, ou null quando o nome configurado não é
    /// legível. Trocada junto com a configuração.
    /// </summary>
    Func<bool>? _talkKey;

    public MainWindow Window { get; }

    RonVoiceSession(
        AppSettings settings, string settingsPath, bool portable, string modelsDir)
    {
        _settings = settings;
        _settingsPath = settingsPath;

        // A ORDEM IMPORTA: as frases do usuário entram antes da gramática ser
        // construída. Invertido, tudo parece funcionar — o catálogo mostra a
        // frase, os testes passam — e o Vosk simplesmente nunca a ouve.
        var custom = CustomPhrases.Apply(LoadRawMap(), CustomPhrasesPath(settingsPath),
                                         settings.Language);
        _map = custom.Map;

        _binds = KeybindReader.FindDefaultIniPath() is { } ini
            ? KeybindReader.Read(ini)
            : new Dictionary<string, string>();

        _resolver = new CommandResolver(_map, _binds) { Mode = settings.SendMode };
        _processNames = ProcessNamesFrom(settings);

        // O modo já nasce certo em vez de virar push-to-talk depois: entre abrir
        // o microfone e ajustar o modo, quem pediu PTT ficaria com o microfone
        // aberto sem ter pedido.
        _talkKey = TalkKeyProbe.For(settings.PushToTalkKey);

        // A sonda é indireta de propósito: a tecla pode mudar na aba
        // Configuração, e o portão captura o delegate uma única vez.
        _gate = new ListenGate(
            () => ForegroundGuard.IsGameForeground(_processNames),
            isMuted: null,
            mode: settings.Mode == ListenModeSetting.PushToTalk
                ? ListenMode.PushToTalk
                : ListenMode.AlwaysOn,
            isTalkKeyDown: () => _talkKey?.Invoke() ?? false);

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

        // Pelo NOME, não pela posição: a enumeração se desloca quando um
        // dispositivo entra ou sai, e entrar em VR faz exatamente isso.
        var devices = WasapiCapture.ListDevices();
        var microphone = MicrophoneResolver.Resolve(
            devices, settings.MicrophoneName, settings.MicrophoneDevice);
        _capture = new WasapiCapture(microphone.Index);
        _capture.OnAudio += OnAudio;
        _capture.Start();

        _main = new MainViewModel();
        _main.StatusBar.Elevated = ForegroundGuard.IsElevated();
        _main.StatusBar.Portable = portable;
        _main.StatusBar.Language = settings.Language;
        _main.StatusBar.SendMode = settings.SendMode;
        // Uma fonte só para o que grava e para o que a barra diz. Antes eram
        // duas contas diferentes, e a barra podia nomear um dispositivo que
        // não era o que estava gravando.
        _main.StatusBar.MicrophoneName = microphone.Name;
        _main.StatusBar.MicrophoneProblem = microphone.Problem;
        // Agora que a barra existe, um push-to-talk sem tecla legível pode ser
        // dito em voz alta em vez de virar um app que simplesmente não escuta.
        ApplyListenMode(settings);

        _main.StatusBar.ListenState = _gate.State;
        _gate.StateChanged += s =>
            Application.Current.Dispatcher.Invoke(() => _main.StatusBar.ListenState = s);

        _main.Commands = new CommandsViewModel(
            _map, custom.Accepted, custom.Issues,
            CustomPhrasesPath(settingsPath), settings.Language,
            sendingViaMod: settings.SendMode == SendMode.RonSpeech);
        _main.Test = new TestViewModel();
        _main.Checks = new ChecksViewModel();
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

    static CommandMap LoadRawMap() => CommandMap.Load(
        Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json"));

    /// <summary>Ao lado do settings.json, que é onde o modo portable guarda tudo.</summary>
    static string CustomPhrasesPath(string settingsPath) =>
        Path.Combine(Path.GetDirectoryName(settingsPath)!, CustomPhrases.FileName);

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

        _main.Commands.ReloadCommand = new RelayCommand(_ => ReloadCustomPhrases());
        _main.Commands.ExportCommand = new RelayCommand(
            _ => ExportProfile(), _ => _main.Commands.HasOwnPhrases);
        _main.Commands.ImportCommand = new RelayCommand(_ => ImportProfile());
        _main.Checks.RunCommand = new RelayCommand(_ => _ = RunChecksAsync());
    }

    void ExportProfile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = PhraseProfiles.SuggestedFileName(_settings.Language),
            Filter = "Perfil do RonVoice (*.json)|*.json",
            Title = "Exportar suas frases",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            PhraseProfiles.Export(
                dialog.FileName, _settings.Language, _main.Commands.PhrasesForExport());
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Não deu para gravar:\n\n{ex.Message}",
                            "RonVoice", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(
            "Perfil exportado.\n\nEle leva só as suas frases e o idioma. Microfone, "
            + "caminho do jogo e tecla de push-to-talk ficaram de fora de propósito: "
            + "são da sua máquina.",
            "RonVoice", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Soma o perfil ao que já existe. A validação de colisão é obrigatória: uma
    /// frase do perfil de outra pessoa que caia numa ordem diferente da sua
    /// deixaria as DUAS ordens sem funcionar, sem erro nenhum.
    /// </summary>
    void ImportProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Perfil do RonVoice (*.json)|*.json|Todos os arquivos|*.*",
            Title = "Importar perfil de frases",
        };
        if (dialog.ShowDialog() != true) return;

        if (PhraseProfiles.TryRead(dialog.FileName, out var problem) is not { } profile)
        {
            MessageBox.Show($"Não deu para importar:\n\n{problem}",
                            "RonVoice", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var store = CustomPhraseStore.Read(CustomPhrasesPath(_settingsPath));
        var result = PhraseProfiles.Merge(_map, _settings.Language, profile, store);

        if (result.Added > 0)
            CustomPhraseStore.Write(CustomPhrasesPath(_settingsPath), store);

        var message = $"{result.Added} frase(s) importada(s).";

        if (result.Issues.Count > 0)
            message += $"\n\n{result.Issues.Count} não entrou(entraram):\n"
                       + string.Join('\n', result.Issues.Take(8).Select(i => $"· {i.Message}"))
                       + (result.Issues.Count > 8 ? "\n· ..." : "");

        if (result.LanguageMismatch)
            message += $"\n\nATENÇÃO: o perfil é para \"{profile.Language}\" e o app está em "
                       + $"\"{_settings.Language}\". Essas frases não serão ouvidas até você "
                       + "trocar o idioma — a gramática é montada por idioma.";

        if (result.Added > 0)
            message += "\n\nReabra o RonVoice para o reconhecimento passar a ouvi-las.";

        MessageBox.Show(message, "RonVoice", MessageBoxButton.OK,
                        result.Issues.Count > 0 || result.LanguageMismatch
                            ? MessageBoxImage.Warning
                            : MessageBoxImage.Information);

        if (result.Added > 0) ReloadCustomPhrases(silent: true);
    }

    /// <summary>
    /// Relê o minhas_frases.json sem fechar o app. O reconhecedor NÃO é recriado:
    /// a gramática é imutável na vida de um VoskRecognizer, então as frases novas
    /// só passam a ser ouvidas ao reabrir. A mensagem diz isso — esconder seria pior.
    /// </summary>
    /// <param name="silent">
    /// Sem a caixa de mensagem. É o caminho de quando quem pediu a recarga foi a
    /// troca de modo, não o botão Recarregar: duas caixas seguidas confundiriam.
    /// </param>
    void ReloadCustomPhrases(bool silent = false)
    {
        var custom = CustomPhrases.Apply(
            LoadRawMap(), CustomPhrasesPath(_settingsPath), _settings.Language);

        _main.Commands = new CommandsViewModel(
            custom.Map, custom.Accepted, custom.Issues,
            CustomPhrasesPath(_settingsPath), _settings.Language,
            sendingViaMod: _settings.SendMode == SendMode.RonSpeech);
        _main.Commands.ReloadCommand = new RelayCommand(_ => ReloadCustomPhrases());
        _main.Commands.SendCommand = new RelayCommand(
            p => _ = SendToGameAsync((OrderRowViewModel)p!), _ => GameIsRunning());
        _main.RaiseCommandsChanged();

        if (silent) return;

        var accepted = custom.Accepted.Values.Sum(v => v.Count);
        MessageBox.Show(
            $"{accepted} frase(s) aceita(s), {custom.Issues.Count} aviso(s).\n\n"
            + "Reabra o RonVoice para o reconhecimento passar a ouvir as frases novas.",
            "RonVoice", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    async Task RunChecksAsync()
    {
        _main.Checks.BeginMicrophoneTest();

        double peak = 0;
        void Measure(ReadOnlyMemory<byte> chunk)
        {
            var level = AudioLevel.Rms(chunk.Span);
            if (level > peak) peak = level;
            Application.Current.Dispatcher.Invoke(() => _main.Checks.Level = level);
        }

        _capture.OnAudio += Measure;
        await Task.Delay(TimeSpan.FromSeconds(3));
        _capture.OnAudio -= Measure;

        var modelsDir = ModelLocator.FindModelsDirectory();
        var modelPresent = modelsDir is not null && ModelPresent(_settings.Language, modelsDir);

        _main.Checks.Show(StartupChecks.Run(new CheckInputs(
            Elevated: ForegroundGuard.IsElevated(),
            ModelPresent: modelPresent,
            Language: _settings.Language,
            MicrophonePeak: peak,
            GameFound: GameIsRunning() || _settings.GameExecutablePath is not null,
            InputIniFound: KeybindReader.FindDefaultIniPath() is not null)));
    }

    static bool ModelPresent(string language, string modelsDir)
    {
        try { return ModelLocator.LooksLikeAModel(ModelLocator.Find(language, modelsDir)); }
        catch (ModelNotFoundException) { return false; }
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

    /// <summary>
    /// Liga o modo de escuta e a sonda da tecla juntos, que é a única forma de
    /// não repetir o bug: o push-to-talk só é aceito quando existe uma tecla
    /// que dá para ler. Sem isso o portão respondia "aguardando a tecla" para
    /// sempre e o app nunca processava áudio, sem erro em lugar nenhum.
    /// </summary>
    /// <returns>Motivo de o push-to-talk não ter sido aceito, ou null.</returns>
    string? ApplyListenMode(AppSettings settings)
    {
        _talkKey = TalkKeyProbe.For(settings.PushToTalkKey);

        if (settings.Mode != ListenModeSetting.PushToTalk)
        {
            _gate.Mode = ListenMode.AlwaysOn;
            _main.StatusBar.TalkKeyProblem = null;
            return null;
        }

        if (_talkKey is null)
        {
            // Continua em push-to-talk, sem escutar. Cair para sempre-ligado
            // seria pior: quem escolheu PTT não quer o microfone aberto, e a
            // troca calada trairia a escolha. O motivo fica dito na barra.
            _gate.Mode = ListenMode.PushToTalk;
            var problem = settings.PushToTalkKey is { Length: > 0 } key
                ? $"PUSH-TO-TALK PARADO — não sei ler a tecla \"{key}\""
                : "PUSH-TO-TALK PARADO — nenhuma tecla escolhida";
            _main.StatusBar.TalkKeyProblem = problem;
            return problem;
        }

        _gate.Mode = ListenMode.PushToTalk;
        _main.StatusBar.TalkKeyProblem = null;
        return null;
    }

    void SaveSettings()
    {
        var updated = _main.Settings.ToSettings();
        SettingsStore.Save(updated, _settingsPath);

        // Aplica a quente o que dá; trocar idioma exigiria recriar modelo e
        // reconhecedor, então isso pede reabrir o app.
        var languageChanged = updated.Language != _settings.Language;
        var sendModeChanged = updated.SendMode != _settings.SendMode;
        _settings = updated;
        _processNames = ProcessNamesFrom(updated);
        var talkKeyProblem = ApplyListenMode(updated);

        // A quente: o resolvedor é o mesmo objeto que o pipeline guarda.
        _resolver.Mode = updated.SendMode;
        _main.StatusBar.SendMode = updated.SendMode;

        // O catálogo marca as ordens que o modo novo não alcança, então precisa
        // ser reconstruído — senão os selos ficam falando do modo anterior.
        if (sendModeChanged) ReloadCustomPhrases(silent: true);

        _main.Commands.SendCommand.RaiseCanExecuteChanged();

        if (talkKeyProblem is not null)
        {
            MessageBox.Show(
                $"Configuração salva, mas o push-to-talk não vai funcionar:\n\n"
                + $"{talkKeyProblem}\n\n"
                + "Clique no campo da tecla e aperte a tecla que você quer usar.",
                "RonVoice", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
