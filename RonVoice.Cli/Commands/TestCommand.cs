using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Cli.Commands;

public static class Cli
{
    public static string MapPath =>
        Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json");

    public static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static bool Flag(string[] args, string name) => args.Contains(name);

    public static string Describe(InputToken token) => token switch
    {
        MouseToken m => $"Mouse({m.Button})",
        ScanCodeToken s => $"Scan(0x{s.Scan:X2}{(s.Extended ? ",E0" : "")})",
        _ => token.ToString()!,
    };

    public static void PrintSequence(KeySequence seq)
    {
        foreach (var s in seq.Steps)
            Console.WriteLine(
                $"  {s.Kind,-5} {Describe(s.Token),-18} hold {s.HoldMs,3}  gap {s.GapAfterMs,3}");
    }
}

public static class TestCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("uso: ronvoice test \"<frase>\" [--lang en|pt]");
            return 1;
        }

        var utterance = args[0];
        var lang = Cli.Option(args, "--lang") ?? "en";
        var map = CommandMap.Load(Cli.MapPath);

        var iniPath = KeybindReader.FindDefaultIniPath();
        if (iniPath is null)
            Console.WriteLine("AVISO: Input.ini não encontrado; usando keybind_defaults");
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        var intent = new PhraseMatcher(map, lang).Match(utterance);
        Console.WriteLine($"frase   : {utterance}");
        Console.WriteLine($"idioma  : {lang}");

        if (intent is null)
        {
            Console.WriteLine("intent  : (nada — rejeitada)");
            return 2;
        }

        Console.WriteLine(
            $"intent  : element={intent.Element ?? "-"} order={intent.OrderId ?? "-"} queue={intent.Queue}");

        if (intent.OrderId is { } id && map.Orders.TryGetValue(id, out var order))
            Console.WriteLine(
                $"ordem   : contexto={order.Context} confiança={order.Confidence} "
                + $"close_menu={order.CloseMenu} path=[{string.Join(' ', order.Path)}]");

        try
        {
            Cli.PrintSequence(new CommandResolver(map, binds).Resolve(intent));
            return 0;
        }
        catch (ResolveException ex)
        {
            Console.Error.WriteLine($"ERRO de resolução: {ex.Message}");
            return 3;
        }
    }
}
