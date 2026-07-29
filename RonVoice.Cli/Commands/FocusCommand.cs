using System.Diagnostics;
using RonVoice.Core.Input;

namespace RonVoice.Cli.Commands;

/// <summary>
/// Registra qual processo está com a janela em foco, ao longo do tempo, num
/// arquivo.
///
/// Existe por causa do VR. O portão de escuta exige que a janela em foco seja a
/// do jogo, e em VR isso frequentemente não é verdade: quem está em foco pode
/// ser o SteamVR, o injetor, ou o compositor. O sintoma é o pior possível — o
/// app simplesmente não escuta e não há erro nenhum.
///
/// Ler a barra de estado não serve para descobrir isso: alt-tabear para olhar a
/// tela já muda o foco que se quer medir. Por isso grava em arquivo, para poder
/// ser lido depois de tirar o headset.
/// </summary>
public static class FocusCommand
{
    public static int Run(string[] args)
    {
        var seconds = IntArg(args, "--seconds") ?? 60;
        var path = StringArg(args, "--out")
            ?? Path.Combine(AppContext.BaseDirectory, "foco.txt");

        using var log = new StreamWriter(path, append: false);
        log.AutoFlush = true;

        void Both(string line)
        {
            Console.WriteLine(line);
            log.WriteLine(line);
        }

        Both($"Gravando o foco por {seconds}s em {path}");
        Both($"Prefixo que o portão procura: {ForegroundGuard.GameProcessPrefix}*");
        Both("");
        Both("Ponha o headset e jogue normalmente. Depois volte e leia o arquivo.");
        Both("");

        var clock = Stopwatch.StartNew();
        string? previous = null;
        var counts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var lastChange = 0.0;

        while (clock.Elapsed.TotalSeconds < seconds)
        {
            var now = ForegroundGuard.ForegroundProcessName() ?? "(nenhum)";
            var t = clock.Elapsed.TotalSeconds;

            if (now != previous)
            {
                if (previous is not null)
                    counts[previous] = counts.GetValueOrDefault(previous) + (t - lastChange);

                var verdict = ForegroundGuard.Matches(now) ? "ESCUTA" : "nao escuta";
                Both($"{t,6:0.0}s  {now,-42} {verdict}");

                previous = now;
                lastChange = t;
            }

            Thread.Sleep(250);
        }

        if (previous is not null)
            counts[previous] = counts.GetValueOrDefault(previous)
                               + (clock.Elapsed.TotalSeconds - lastChange);

        Both("");
        Both("=== tempo total em foco ===");
        foreach (var (name, total) in counts.OrderByDescending(kv => kv.Value))
            Both($"{total,7:0.0}s  {name,-42} "
                 + (ForegroundGuard.Matches(name) ? "ESCUTA" : "nao escuta"));

        var listening = counts.Where(kv => ForegroundGuard.Matches(kv.Key)).Sum(kv => kv.Value);
        Both("");
        Both(listening <= 0.5
            ? "O jogo NUNCA esteve em foco. É por isso que nenhum comando funciona: "
              + "o portão de escuta nunca abriu."
            : $"O jogo esteve em foco {listening:0.0}s de {seconds}s. "
              + "Nesses instantes o app escutava.");

        Console.WriteLine();
        Console.WriteLine($"Arquivo: {path}");
        return 0;
    }

    static string? StringArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static int? IntArg(string[] args, string name) =>
        int.TryParse(StringArg(args, name), out var v) ? v : null;
}
