using System.Globalization;
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

    /// <summary>
    /// Remove um sufixo ".exe" opcional, sem diferenciar maiúsculas/minúsculas.
    /// Process.ProcessName nunca inclui a extensão, mas quem copia o nome do
    /// Gerenciador de Tarefas para --process provavelmente cola com ela.
    /// </summary>
    public static string StripExeSuffix(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>
    /// Lê e valida --delay. Ausente vira 0 (comportamento atual, sem espera).
    /// Um valor malformado ou negativo é rejeitado explicitamente em vez de
    /// virar 0 em silêncio — quem digitou "--delay abc" quer saber que errou,
    /// não mandar a ordem na hora sem aviso.
    /// </summary>
    public static bool TryParseDelay(string[] args, out double seconds, out string? error)
    {
        var raw = Option(args, "--delay");
        if (raw is null)
        {
            seconds = 0;
            error = null;
            return true;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
            || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
            error = $"--delay inválido: '{raw}' (espere um número >= 0, em segundos)";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Espera com contador visível antes de mandar a ordem. Roda antes do
    /// foreground check de propósito: é a janela pra trocar do terminal pro
    /// jogo com alt-tab. Sai sem imprimir nada se o delay for 0.
    /// </summary>
    public static void CountdownDelay(double totalSeconds)
    {
        if (totalSeconds <= 0) return;

        Console.Error.WriteLine(
            $"aguardando {totalSeconds:0.###}s antes de enviar — troque para o jogo agora...");

        var remainingMs = (int)Math.Round(totalSeconds * 1000);
        while (remainingMs > 0)
        {
            var secondsLeft = (int)Math.Ceiling(remainingMs / 1000.0);
            Console.Error.Write($"\r  enviando em {secondsLeft,3}s...   ");
            var step = Math.Min(1000, remainingMs);
            Thread.Sleep(step);
            remainingMs -= step;
        }
        Console.Error.WriteLine("\r  enviando agora!             ");
    }

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
