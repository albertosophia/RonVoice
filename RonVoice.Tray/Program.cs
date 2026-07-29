using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Tray;

static class Program
{
    const uint VkM = 0x4D;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var lang = args.Length > 0 ? args[0] : "en";
        using var tray = new TrayIcon();

        try
        {
            RunPipeline(lang, tray);
        }
        catch (Exception ex)
        {
            // Sem janela, o ícone é o único canal de erro que o usuário tem.
            tray.ShowFault(ex.Message);
            Application.Run();
        }
    }

    static void RunPipeline(string lang, TrayIcon tray)
    {
        var mapPath = Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json");
        var map = CommandMap.Load(mapPath);

        var iniPath = KeybindReader.FindDefaultIniPath();
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        var gate = new ListenGate(() => ForegroundGuard.IsGameForeground());
        gate.StateChanged += tray.Show;
        tray.Show(gate.State);

        using var engine = new VoskSpeechEngine(
            ModelLocator.Find(lang), GrammarBuilder.Build(map, lang));

        var pipeline = new VoicePipeline(
            engine, gate,
            new PhraseMatcher(map, lang),
            new CommandResolver(map, binds),
            new SendInputSender());
        pipeline.Start();

        using var capture = new WasapiCapture();
        capture.OnAudio += pipeline.Push;
        capture.Start();

        using var hotkey = new GlobalHotkey(
            GlobalHotkey.ModControl | GlobalHotkey.ModAlt, VkM);

        void ToggleMute() { gate.Toggle(); tray.Show(gate.State); }
        hotkey.Pressed += ToggleMute;
        tray.MuteRequested += ToggleMute;
        tray.ExitRequested += Application.Exit;

        Application.Run();
        capture.Stop();
    }
}
