using System.Text.RegularExpressions;

namespace RonVoice.Tests;

/// <summary>
/// Nenhuma tela pode carregar cor própria: o tema é o único lugar onde cor é
/// decidida.
///
/// Existe porque a varredura que moveu as telas para o tema olhou só ATRIBUTOS
/// (Background="#RRGGBB") e passou reto pelas cores dentro de Setter
/// (&lt;Setter Property="Background" Value="#RRGGBB" /&gt;). Sobraram dois painéis
/// claros com a tinta clara do tema por cima — ilegíveis, e justamente na tela
/// que existe para ser lida quando algo não funciona.
/// </summary>
public class ThemeConsistencyTests
{
    /// <summary>
    /// Sobe da pasta de saída dos testes até achar a raiz do repositório. Os
    /// XAML não são copiados para bin, e é o fonte que precisa ser conferido.
    /// </summary>
    static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RonVoice.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir;
    }

    static IEnumerable<FileInfo> AppXaml() =>
        new DirectoryInfo(Path.Combine(RepoRoot().FullName, "RonVoice.App"))
            .EnumerateFiles("*.xaml", SearchOption.AllDirectories)
            .Where(f => !string.Equals(f.Name, "Theme.xaml", StringComparison.Ordinal));

    [Fact]
    public void TheXamlIsWhereIThinkItIs() =>
        Assert.True(AppXaml().Count() >= 5, "não achei os XAML do app");

    /// <summary>
    /// Qualquer #RRGGBB fora do Theme.xaml. Pega tanto atributo quanto Setter,
    /// que é onde os dois que escaparam estavam.
    /// </summary>
    [Fact]
    public void NoScreenCarriesItsOwnColour()
    {
        var pattern = new Regex("\"#[0-9A-Fa-f]{3,8}\"");
        var offenders = new List<string>();

        foreach (var file in AppXaml())
        {
            var lines = File.ReadAllLines(file.FullName);
            for (var i = 0; i < lines.Length; i++)
                if (pattern.IsMatch(lines[i]))
                    offenders.Add($"{file.Name}:{i + 1}  {lines[i].Trim()}");
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Tipos que o tema estiliza. Uma Style local para um deles SEM BasedOn não
    /// estende a do tema: ela a SUBSTITUI, e o controle volta ao visual padrão
    /// do Windows — claro, no meio de um app escuro.
    ///
    /// Foi assim que o botão "Testar minha voz" ficou cinza-claro: o Style local
    /// existia só para trocar o texto conforme a gravação, e de brinde jogou o
    /// tema inteiro fora.
    /// </summary>
    static readonly string[] ThemedTypes =
    [
        "Button", "TextBlock", "TextBox", "ComboBox", "ComboBoxItem",
        "CheckBox", "Slider", "TabItem", "TabControl", "Window",
    ];

    [Fact]
    public void NoLocalStyleThrowsAwayTheThemeStyle()
    {
        var style = new Regex(@"<Style\b[^>]*?>", RegexOptions.Singleline);
        var target = new Regex(@"TargetType=""(?<t>\w+)""");
        var offenders = new List<string>();

        foreach (var file in AppXaml())
        {
            var text = File.ReadAllText(file.FullName);
            foreach (Match m in style.Matches(text))
            {
                var t = target.Match(m.Value);
                if (!t.Success || !ThemedTypes.Contains(t.Groups["t"].Value)) continue;
                if (m.Value.Contains("BasedOn", StringComparison.Ordinal)) continue;

                var line = text[..m.Index].Count(c => c == '\n') + 1;
                offenders.Add(
                    $"{file.Name}:{line} Style TargetType=\"{t.Groups["t"].Value}\" sem BasedOn — "
                    + "substitui a Style do tema em vez de estendê-la");
            }
        }

        Assert.Empty(offenders);
    }

    static double Luminance(string hex)
    {
        var v = hex.TrimStart('#');
        if (v.Length == 8) v = v[2..];          // descarta o alfa
        var r = Convert.ToInt32(v[..2], 16) / 255.0;
        var g = Convert.ToInt32(v.Substring(2, 2), 16) / 255.0;
        var b = Convert.ToInt32(v.Substring(4, 2), 16) / 255.0;
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>
    /// Resolve as DUAS formas do tema: hex direto no pincel, e pincel apontando
    /// para um &lt;Color&gt; nomeado. A primeira versão deste teste só via a
    /// primeira, e reprovava metade da paleta dizendo que as chaves não
    /// existiam — o teste estava errado, não o tema.
    /// </summary>
    static Dictionary<string, string> ThemeBrushes()
    {
        var text = File.ReadAllText(
            Path.Combine(RepoRoot().FullName, "RonVoice.App", "Theme.xaml"));

        var colors = new Regex(@"<Color\s+x:Key=""(?<k>\w+)"">(?<c>#[0-9A-Fa-f]{6,8})</Color>")
            .Matches(text)
            .ToDictionary(m => m.Groups["k"].Value, m => m.Groups["c"].Value);

        var brushes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in new Regex(
            @"<SolidColorBrush\s+x:Key=""(?<k>\w+)""\s+Color=""(?<c>[^""]+)""").Matches(text))
        {
            var value = m.Groups["c"].Value;

            if (value.StartsWith('#')) brushes[m.Groups["k"].Value] = value;
            else if (new Regex(@"StaticResource\s+(?<r>\w+)").Match(value) is { Success: true } r
                     && colors.TryGetValue(r.Groups["r"].Value, out var resolved))
                brushes[m.Groups["k"].Value] = resolved;
        }
        return brushes;
    }

    /// <summary>
    /// Toda superfície do tema tem que ser escura.
    ///
    /// A regra que eu tinha escrito antes — "todo fundo precisa de tinta junto" —
    /// era falsa: o realce de linha ao passar o mouse troca só o fundo, e está
    /// certo. O bug de verdade foi outro: um painel CLARO num app escuro, com a
    /// tinta clara do tema por cima. É isso que dá para provar.
    /// </summary>
    [Theory]
    [InlineData("Ground")]
    [InlineData("Panel")]
    [InlineData("Raised")]
    [InlineData("Deep")]
    [InlineData("Hover")]
    [InlineData("Line")]
    [InlineData("SignalFill")]
    [InlineData("OkFill")]
    [InlineData("CritFill")]
    public void EverySurfaceIsDark(string key)
    {
        var brushes = ThemeBrushes();
        Assert.True(brushes.ContainsKey(key), $"o tema não tem mais a chave {key}");

        var luminance = Luminance(brushes[key]);
        Assert.True(luminance < 0.25,
                    $"{key} = {brushes[key]} tem luminância {luminance:0.00}: claro demais "
                    + "para um tema escuro, e a tinta do tema fica ilegível em cima");
    }

    /// <summary>
    /// E toda tinta tem que ser clara o bastante para ser lida sobre elas.
    /// </summary>
    [Theory]
    [InlineData("Ink")]
    [InlineData("Muted")]
    [InlineData("Signal")]
    [InlineData("OkInk")]
    [InlineData("CritInk")]
    public void EveryInkIsLight(string key)
    {
        var brushes = ThemeBrushes();
        Assert.True(brushes.ContainsKey(key), $"o tema não tem mais a chave {key}");

        var luminance = Luminance(brushes[key]);
        Assert.True(luminance > 0.30,
                    $"{key} = {brushes[key]} tem luminância {luminance:0.00}: escuro demais "
                    + "para ser lido sobre as superfícies do tema");
    }

    /// <summary>
    /// Cor em C# tambem conta. Estas escaparam de todas as redes anteriores
    /// porque a varredura so' olhava XAML — e eram os tons claros de antes do
    /// tema escuro, apagados sobre o fundo novo.
    /// </summary>
    [Fact]
    public void ColoursWrittenInCSharpAreFromThePalette()
    {
        var palette = ThemeBrushes().Values
            .Select(c => c.TrimStart('#')[^6..].ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var pattern = new Regex(
            @"Color\.FromRgb\(\s*0x(?<r>[0-9A-Fa-f]{2})\s*,\s*0x(?<g>[0-9A-Fa-f]{2})\s*,\s*0x(?<b>[0-9A-Fa-f]{2})");

        var offenders = new List<string>();
        var appDir = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "RonVoice.App"));

        foreach (var file in appDir.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            foreach (Match m in pattern.Matches(File.ReadAllText(file.FullName)))
            {
                var hex = (m.Groups["r"].Value + m.Groups["g"].Value + m.Groups["b"].Value)
                    .ToUpperInvariant();
                if (!palette.Contains(hex))
                    offenders.Add($"{file.Name}: #{hex} nao esta na paleta do tema");
            }
        }

        Assert.Empty(offenders);
    }
}
