using System.Globalization;
using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Cli.Commands;

public static class ListenCommand
{
    public static int Run(string[] args)
    {
        if (Cli.Flag(args, "--list-devices")) return ListDevices();

        var lang = Cli.Option(args, "--lang") ?? "en";
        var fromWav = Cli.Option(args, "--from-wav");
        var dryRun = Cli.Flag(args, "--dry-run");

        if (!TryParseThreshold(Cli.Option(args, "--threshold"), out var threshold, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        if (!TryParseDevice(Cli.Option(args, "--device"), out var device, out error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        var map = Cli.LoadMap(lang);

        var iniPath = KeybindReader.FindDefaultIniPath();
        if (iniPath is null)
            Console.Error.WriteLine("AVISO: Input.ini não encontrado; usando keybind_defaults");
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        string modelPath;
        try { modelPath = ModelLocator.Find(lang); }
        catch (ModelNotFoundException ex) { Console.Error.WriteLine(ex.Message); return 5; }

        // Com --from-wav o portão fica aberto: não há jogo para estar em foco.
        var processName = Cli.Option(args, "--process");
        var gate = new ListenGate(
            fromWav is not null
                ? () => true
                : () => ForegroundGuard.IsGameForeground(
                    processName is null ? null : [processName]));

        using var engine = new VoskSpeechEngine(modelPath, GrammarBuilder.Build(map, lang));
        var pipeline = new VoicePipeline(
            engine, gate,
            new PhraseMatcher(map, lang),
            new CommandResolver(map, binds),
            new SendInputSender(dryRun),
            threshold);

        pipeline.Heard += r => Console.WriteLine(
            $"ouvi     : {r.Text}  (conf {r.AverageConfidence:0.00})");
        pipeline.Matched += i => Console.WriteLine(
            $"casou    : element={i.Element ?? "-"} order={i.OrderId ?? "-"} queue={i.Queue}");
        pipeline.Rejected += r => Console.WriteLine(
            $"rejeitada: {r.Reason} — {r.Text}{(r.Detail is null ? "" : $" ({r.Detail})")}");
        pipeline.Sent += s => Console.WriteLine($"enviada  : {s.Steps.Count} passos");
        gate.StateChanged += s => Console.WriteLine($"estado   : {s}");
        pipeline.Start();

        IAudioCapture capture;
        try
        {
            capture = fromWav is not null
                ? new WavFileCapture(fromWav)
                : new WasapiCapture(device);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            Console.Error.WriteLine(ex.Message);
            return 4;
        }

        using (capture)
        {
            capture.OnAudio += chunk => pipeline.Push(chunk);
            capture.OnStopped += _ => pipeline.Flush();

            if (fromWav is not null)
            {
                capture.Start();                 // síncrono: retorna no fim do arquivo
                return 0;
            }

            Console.WriteLine($"escutando ({lang}) — Ctrl+C para sair");
            if (!ForegroundGuard.IsElevated())
                Console.Error.WriteLine("AVISO: sem elevação, as teclas não chegam ao jogo.");

            using var quit = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Set(); };
            capture.Start();
            quit.Wait();
            capture.Stop();
        }
        return 0;
    }

    static int ListDevices()
    {
        var devices = WasapiCapture.ListDevices();
        if (devices.Count == 0)
        {
            Console.Error.WriteLine("nenhum microfone encontrado");
            return 4;
        }
        for (var i = 0; i < devices.Count; i++) Console.WriteLine($"  {i}: {devices[i]}");
        return 0;
    }

    internal static bool TryParseThreshold(string? text, out double value, out string error)
    {
        value = 0.0;
        error = "";
        if (text is null) return true;

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || value is < 0.0 or > 1.0)
        {
            error = $"--threshold inválido: '{text}' (espere um número entre 0 e 1)";
            return false;
        }
        return true;
    }

    internal static bool TryParseDevice(string? text, out int value, out string error)
    {
        value = 0;
        error = "";
        if (text is null) return true;

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            || value < 0)
        {
            error = $"--device inválido: '{text}' (espere um índice >= 0; use --list-devices)";
            return false;
        }
        return true;
    }
}
