using System.Text;
using RonVoice.Core.Commands;

namespace RonVoice.Cli.Commands;

/// <summary>
/// Gera uma linha por frase do mapa: frase TAB orderId TAB element TAB queue.
/// É a rede de regressão que pega colisão nova de alias quando o mapa mudar.
/// </summary>
public static class CorpusCommand
{
    public static int Run(string[] args)
    {
        var outDir = Cli.Option(args, "--out")
                     ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                                     "RonVoice.Tests", "corpus");
        Directory.CreateDirectory(outDir);
        var map = CommandMap.Load(Cli.MapPath);

        foreach (var lang in new[] { "en", "pt" })
        {
            var sb = new StringBuilder();
            var count = 0;
            foreach (var order in map.Orders.Values.OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                if (!order.Phrases.TryGetValue(lang, out var phrases)) continue;
                foreach (var phrase in phrases)
                {
                    sb.Append(phrase).Append('\t').Append(order.Id).Append("\t-\tfalse\n");
                    count++;
                }
            }

            var path = Path.Combine(outDir, $"{lang}.tsv");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"{path}: {count} linhas");
        }
        return 0;
    }
}
