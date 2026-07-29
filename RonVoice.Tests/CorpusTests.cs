using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class CorpusTests
{
    static string CorpusPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "corpus", name);

    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IEnumerable<(string Text, string? OrderId, string? Element, bool Queue)> ReadTsv(string name)
    {
        foreach (var line in File.ReadAllLines(CorpusPath(name)))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            yield return (
                f[0],
                f[1] == "-" ? null : f[1],
                f[2] == "-" ? null : f[2],
                bool.Parse(f[3]));
        }
    }

    [Theory]
    [InlineData("en", "adversarial.tsv")]
    [InlineData("pt", "adversarial.pt.tsv")]
    public void AdversarialCorpusPasses(string lang, string file)
    {
        var matcher = new PhraseMatcher(Map(), lang);
        var failures = new List<string>();

        foreach (var (text, orderId, element, queue) in ReadTsv(file))
        {
            var intent = matcher.Match(text);
            var gotOrder = intent?.OrderId;
            var gotElement = intent?.Element;
            var gotQueue = intent?.Queue ?? false;

            if (gotOrder != orderId || gotElement != element || gotQueue != queue)
                failures.Add(
                    $"\"{text}\" esperado (order={orderId}, el={element}, q={queue}) "
                    + $"veio (order={gotOrder}, el={gotElement}, q={gotQueue})");
        }
        Assert.Empty(failures);
    }

    /// <summary>
    /// A asserção que realmente protege o sistema: nenhuma frase do mapa pode
    /// resolver para uma ordem diferente da sua. Mandar a ordem errada
    /// compromete a missão; rejeitar custa uma repetição.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void NoPhraseResolvesToTheWrongOrder(string lang)
    {
        var matcher = new PhraseMatcher(Map(), lang);
        var wrong = new List<string>();

        foreach (var (text, orderId, _, _) in ReadTsv($"{lang}.tsv"))
        {
            var got = matcher.Match(text)?.OrderId;
            if (got is not null && got != orderId)
                wrong.Add($"{text}: {orderId} -> {got}");
        }
        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("en", 399)]
    [InlineData("pt", 371)]
    public void GeneratedCorpusHasExpectedSize(string lang, int expected) =>
        Assert.Equal(expected, ReadTsv($"{lang}.tsv").Count());

    /// <summary>Nenhuma ordem pode ficar inalcançável em nenhum idioma.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void EveryOrderIsReachable(string lang)
    {
        var matcher = new PhraseMatcher(Map(), lang);
        var reachable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (text, orderId, _, _) in ReadTsv($"{lang}.tsv"))
            if (matcher.Match(text)?.OrderId == orderId)
                reachable.Add(orderId!);

        var unreachable = Map().Orders.Keys.Except(reachable).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Empty(unreachable);
    }

    /// <summary>
    /// Confere as quatro colunas, não só a ordem. O corpus gerado registra
    /// element e queue em todas as 770 linhas e ninguém os checava — foi
    /// exatamente por isso que duas frases PT passaram a enfileirar sem que
    /// nada acusasse. Além da ordem certa, isto prende duas regras de domínio
    /// que o corpus gerado é a única rede a cobrir: nenhum elemento inventado
    /// quando a frase não menciona um, e nenhum envelope de fila inventado
    /// quando a frase não pede.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void EveryGeneratedPhraseResolves(string lang)
    {
        var matcher = new PhraseMatcher(Map(), lang);
        var wrong = new List<string>();

        foreach (var (text, orderId, element, queue) in ReadTsv($"{lang}.tsv"))
        {
            var m = matcher.Match(text);
            if (m is not null && m.OrderId == orderId && m.Element == element && m.Queue == queue)
                continue;

            wrong.Add(
                $"\"{text}\" esperado (order={orderId}, el={element ?? "-"}, q={queue}) "
                + $"veio (order={m?.OrderId ?? "-"}, el={m?.Element ?? "-"}, q={m?.Queue ?? false})");
        }
        Assert.Empty(wrong);
    }
}
