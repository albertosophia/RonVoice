using RonVoice.Core.Commands;
using RonVoice.Core.Input;

namespace RonVoice.Cli.Commands;

/// <summary>
/// Manda teclas cruas, por nome, sem passar por frase, ordem ou menu.
///
/// Existe para testar o caminho do mod UE4SS do RoNSpeech, que registra as suas
/// próprias teclas e chama as funções do jogo direto — sem abrir o menu SWAT.
/// Se isso funcionar em VR, o menu deixa de ser necessário, e é o menu que está
/// quebrado ali.
///
/// Também serve para saber COMO mandar: o resto do projeto manda scan code,
/// porque o jogo lê por RawInput. O UE4SS pode estar lendo virtual-key. São
/// caminhos diferentes e a única forma de saber qual chega é tentar os dois.
/// </summary>
public static class KeyCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "uso: ronvoice key <NOME> [<NOME>...] [--delay <segundos>] [--gap <ms>]\n"
                + "     ex: ronvoice key F15 --delay 20        (breach + flash pelo mod)\n"
                + "         ronvoice key Nine F15 --delay 20   (\"on my command\" + breach flash)");
            return 1;
        }

        var names = args.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var gap = Cli.Option(args, "--gap") is { } g && int.TryParse(g, out var gv) ? gv : 120;

        if (!Cli.TryParseDelay(args, out var delaySeconds, out var delayError))
        {
            Console.Error.WriteLine(delayError);
            return 1;
        }

        var tokens = new List<(string Name, InputToken Token)>();
        foreach (var name in names)
        {
            if (!KeyCatalog.TryResolve(name, out var token))
            {
                Console.Error.WriteLine($"tecla desconhecida: {name}");
                return 2;
            }
            tokens.Add((name, token));
        }

        if (!ForegroundGuard.IsElevated())
            Console.Error.WriteLine("AVISO: sem elevação, o input não chega ao jogo e não há erro.");

        Cli.CountdownDelay(delaySeconds);

        var foreground = ForegroundGuard.ForegroundProcessName() ?? "(nenhum)";
        Console.WriteLine($"foco: {foreground} "
                          + $"({(ForegroundGuard.Matches(foreground) ? "é o jogo" : "NÃO é o jogo")})");

        var sender = new SendInputSender();
        var steps = tokens
            .Select(t => new KeyStep(StepKind.Press, t.Token, CommandResolver.MouseHoldMs, gap))
            .ToList();

        try
        {
            sender.Send(new KeySequence(steps));
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"O WINDOWS RECUSOU: {ex.Message}");
            return 3;
        }

        foreach (var (name, _) in tokens) Console.WriteLine($"mandada: {name}");
        Console.WriteLine("o Windows aceitou todos os eventos");
        return 0;
    }
}
