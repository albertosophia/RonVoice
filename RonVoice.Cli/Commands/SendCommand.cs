using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Cli.Commands;

public static class SendCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("uso: ronvoice send \"<frase>\" [--lang en|pt] [--dry-run] [--force]");
            return 1;
        }

        var utterance = args[0];
        var lang = Cli.Option(args, "--lang") ?? "en";
        var dryRun = Cli.Flag(args, "--dry-run");
        var force = Cli.Flag(args, "--force");

        if (!ForegroundGuard.IsElevated())
            Console.Error.WriteLine(
                "AVISO: o app não está elevado. Se o jogo estiver, o input não chega e não há erro.");

        var map = CommandMap.Load(Cli.MapPath);
        var iniPath = KeybindReader.FindDefaultIniPath();
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        var intent = new PhraseMatcher(map, lang).Match(utterance);
        if (intent is null)
        {
            Console.Error.WriteLine("rejeitada: nenhuma ordem casou");
            return 2;
        }

        KeySequence seq;
        try
        {
            seq = new CommandResolver(map, binds).Resolve(intent);
        }
        catch (ResolveException ex)
        {
            Console.Error.WriteLine($"ERRO de resolução: {ex.Message}");
            return 3;
        }

        if (!dryRun && !force && !ForegroundGuard.IsGameForeground())
        {
            Console.Error.WriteLine(
                $"descartada: o jogo não está em foco (em foco: {ForegroundGuard.ForegroundProcessName() ?? "?"}). "
                + "Use --force para mandar mesmo assim.");
            return 4;
        }

        Console.WriteLine(
            $"intent  : element={intent.Element ?? "-"} order={intent.OrderId ?? "-"} queue={intent.Queue}");
        Cli.PrintSequence(seq);

        var sender = new SendInputSender(dryRun);
        sender.Send(seq);

        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("--- eventos INPUT que sairiam ---");
            foreach (var line in sender.Log) Console.WriteLine("  " + line);
        }
        return 0;
    }
}
