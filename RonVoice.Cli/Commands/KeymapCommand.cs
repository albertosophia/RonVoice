using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Cli.Commands;

public static class KeymapCommand
{
    public static int Run(string[] args)
    {
        var map = CommandMap.Load(Cli.MapPath);
        var iniPath = Cli.Option(args, "--ini") ?? KeybindReader.FindDefaultIniPath();

        Console.WriteLine($"Input.ini : {iniPath ?? "(não encontrado — só defaults)"}");
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);
        Console.WriteLine($"binds lidos: {binds.Count}");
        Console.WriteLine();

        var resolver = new CommandResolver(map, binds);
        var tokens = new List<string> { "MENU" };
        tokens.AddRange(Enumerable.Range(1, 9).Select(i => i.ToString()));
        tokens.AddRange(map.Orders.Values
            .SelectMany(o => o.Path)
            .Where(t => t.StartsWith("KEY:", StringComparison.Ordinal))
            .Distinct());

        Console.WriteLine($"{"token",-22} {"tecla",-20} origem");
        foreach (var token in tokens)
            PrintToken(map, binds, token);

        foreach (var element in map.Elements.Keys)
        {
            var action = ActionNames.ForElement(element);
            var bound = binds.GetValueOrDefault(action);
            var seq = resolver.Resolve(new Core.Matching.Intent(element, null, false));
            Console.WriteLine($"{"element:" + element,-22} {Cli.Describe(seq.Steps[0].Token),-20} "
                              + $"{(bound is null ? "default" : action + "=" + bound)}");
        }
        return 0;
    }

    static void PrintToken(
        CommandMap map, IReadOnlyDictionary<string, string> binds, string token)
    {
        var action = token switch
        {
            "MENU" => ActionNames.OpenSwatCommand,
            _ when token.Length == 1 && token[0] is >= '1' and <= '9' =>
                ActionNames.ForDigit(token[0]),
            _ => ActionNames.ForKeyToken(token),
        };

        var bound = action is null ? null : binds.GetValueOrDefault(action);
        var resolver = new CommandResolver(map, binds);
        string rendered;
        try
        {
            var order = map.Orders.Values.First(o => o.Path.Contains(token));
            var seq = resolver.Resolve(new Core.Matching.Intent(null, order.Id, false));
            var index = order.Path.ToList().IndexOf(token);
            rendered = Cli.Describe(seq.Steps[index].Token);
        }
        catch (Exception)
        {
            rendered = "(não resolve)";
        }

        Console.WriteLine($"{token,-22} {rendered,-20} "
                          + $"{(bound is null ? "default" : action + "=" + bound)}");
    }
}
