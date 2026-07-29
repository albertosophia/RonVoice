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
            Console.Error.WriteLine(
                "uso: ronvoice send \"<frase>\" [--lang en|pt] [--dry-run] [--force] [--delay <segundos>]");
            return 1;
        }

        var utterance = args[0];
        var lang = Cli.Option(args, "--lang") ?? "en";
        var dryRun = Cli.Flag(args, "--dry-run");
        var force = Cli.Flag(args, "--force");

        if (!Cli.TryParseDelay(args, out var delaySeconds, out var delayError))
        {
            Console.Error.WriteLine(delayError);
            return 1;
        }

        if (!ForegroundGuard.IsElevated())
            Console.Error.WriteLine(
                "AVISO: o app não está elevado. Se o jogo estiver, o input não chega e não há erro.");

        // O delay roda antes do foreground check, de propósito: é a janela pra
        // trocar do terminal pro jogo com alt-tab antes da ordem sair.
        Cli.CountdownDelay(delaySeconds);

        var map = CommandMap.Load(Cli.MapPath);
        var iniPath = KeybindReader.FindDefaultIniPath();
        // A §7 da spec pede aviso alto, e `send` é onde ele importa: quem tem a
        // config em lugar inesperado roda tudo nos defaults, com saída de aparência
        // perfeitamente normal, e nenhuma tecla chega no jogo.
        if (iniPath is null)
            Console.Error.WriteLine("AVISO: Input.ini não encontrado; usando keybind_defaults");
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
            // Log e Events são o mesmo dado, na mesma ordem: prosa e struct.
            // A §8.2 da spec dispensou teste unitário do sender com o argumento
            // de que o dry-run imprime o INPUT exato — imprimir só a prosa
            // deixava wVk e dwFlags fora de qualquer olho humano ou automático.
            var lines = sender.Log;
            for (var i = 0; i < lines.Count; i++)
            {
                var e = sender.Events[i];
                Console.WriteLine(
                    $"  {lines[i],-18} type={e.Type} wVk=0x{e.Vk:X4} wScan=0x{e.Scan:X4} "
                    + $"dwFlags=0x{e.Flags:X4} mouseData=0x{e.MouseData:X4}");
            }
        }
        return 0;
    }
}
