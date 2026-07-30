using RonVoice.Core.Commands;
using RonVoice.Core.Config;

namespace RonVoice.Tests;

/// <summary>
/// Perfis de frases, para trocar entre pessoas. O risco todo está na importação:
/// uma frase do perfil de outra pessoa que caia numa ordem diferente da sua
/// deixaria as DUAS sem funcionar, sem erro nenhum.
/// </summary>
public class PhraseProfileTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"ronvoice-perfil-{Guid.NewGuid():N}.json");

    static Dictionary<string, List<string>> Mine() => new()
    {
        ["hold"] = ["fica quieto ai"],
        ["door.open.flashbang"] = ["manda a bang"],
    };

    // ---- exportar ----

    [Fact]
    public void ExportThenImportRoundTrips()
    {
        var path = TempPath();
        try
        {
            PhraseProfiles.Export(path, "pt", Mine());

            var read = PhraseProfiles.TryRead(path, out var problem);
            Assert.Null(problem);
            Assert.NotNull(read);
            Assert.Equal("pt", read.Language);
            Assert.Equal(["fica quieto ai"], read.Phrases["hold"]);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Configuração NÃO entra no perfil. Microfone e caminho do jogo são da
    /// máquina de quem exportou; importar isso apontaria o app de outra pessoa
    /// para um dispositivo que ela não tem.
    /// </summary>
    [Fact]
    public void TheFileCarriesNothingAboutTheMachine()
    {
        var path = TempPath();
        try
        {
            PhraseProfiles.Export(path, "pt", Mine());
            var raw = File.ReadAllText(path);

            foreach (var leak in new[]
                     { "microphone", "Microfone", "GameExecutable", "PushToTalk", "threshold" })
                Assert.DoesNotContain(leak, raw, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AccentsAreWrittenLiterallyBecausePeopleReadThisFile()
    {
        var path = TempPath();
        try
        {
            PhraseProfiles.Export(path, "pt", new() { ["hold"] = ["fica na posição"] });
            Assert.Contains("posição", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OrdersWithNoPhrasesAreNotExported()
    {
        var path = TempPath();
        try
        {
            PhraseProfiles.Export(path, "pt", new()
            {
                ["hold"] = ["fica quieto ai"],
                ["cover"] = [],
            });
            Assert.False(PhraseProfiles.TryRead(path, out _)!.Phrases.ContainsKey("cover"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheSuggestedNameSaysWhichLanguageItIsFor() =>
        Assert.Equal("perfil-ronvoice-pt.json", PhraseProfiles.SuggestedFileName("pt"));

    // ---- ler arquivo errado ----

    /// <summary>
    /// Importar um JSON qualquer tem que falhar DIZENDO isso, em vez de
    /// funcionar pela metade e deixar quem importou sem saber por quê.
    /// </summary>
    [Fact]
    public void SomeOtherJsonIsRefusedWithAReason()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{ "hold": ["fica quieto"] }""");

            Assert.Null(PhraseProfiles.TryRead(path, out var problem));
            Assert.Contains("não é um perfil", problem);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BrokenJsonIsRefusedWithAReason()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ isso nao e json");

            Assert.Null(PhraseProfiles.TryRead(path, out var problem));
            Assert.Contains("JSON", problem);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Um perfil de uma versão futura pede atualizar o programa, em vez de o
    /// programa adivinhar um formato que ele não conhece.
    /// </summary>
    [Fact]
    public void AProfileFromANewerVersionAsksForAnUpdate()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """
                { "ronvoice_profile": 99, "language": "pt", "phrases": {} }
                """);

            Assert.Null(PhraseProfiles.TryRead(path, out var problem));
            Assert.Contains("atualize", problem);
        }
        finally { File.Delete(path); }
    }

    // ---- importar ----

    static PhraseProfile Profile(string language, Dictionary<string, List<string>> phrases) =>
        PhraseProfile.Of(language, phrases);

    [Fact]
    public void ImportingAddsToWhatIsAlreadyThere()
    {
        var mine = Mine();
        var result = PhraseProfiles.Merge(
            Map(), "pt", Profile("pt", new() { ["cover"] = ["me protege"] }), mine);

        Assert.Equal(1, result.Added);
        Assert.Empty(result.Issues);
        Assert.Equal(["fica quieto ai"], mine["hold"]);   // o que era meu ficou
        Assert.Equal(["me protege"], mine["cover"]);
    }

    /// <summary>
    /// O risco central: a frase do outro cai numa ordem diferente da sua e as
    /// duas ficam mudas. Tem que ser recusada e dita.
    /// </summary>
    [Fact]
    public void APhraseThatCollidesWithTheMapIsRefusedAndReported()
    {
        var mine = new Dictionary<string, List<string>>();
        var result = PhraseProfiles.Merge(
            Map(), "pt", Profile("pt", new() { ["hold"] = ["empilha"] }), mine);

        Assert.Equal(0, result.Added);
        Assert.Single(result.Issues);
        Assert.Contains("door.stack.auto", result.Issues[0].Message);
        Assert.Empty(mine);
    }

    [Fact]
    public void APhraseThatCollidesWithMyOwnIsRefusedToo()
    {
        var mine = Mine();
        var result = PhraseProfiles.Merge(
            Map(), "pt", Profile("pt", new() { ["cover"] = ["fica quieto ai"] }), mine);

        Assert.Equal(0, result.Added);
        Assert.Single(result.Issues);
        Assert.Contains("hold", result.Issues[0].Message);
    }

    [Fact]
    public void AnUnknownOrderIsReportedNotSilentlyDropped()
    {
        var result = PhraseProfiles.Merge(
            Map(), "pt", Profile("pt", new() { ["ordem.inventada"] = ["qualquer coisa"] }), []);

        Assert.Equal(0, result.Added);
        Assert.Equal(PhraseIssueKind.UnknownOrder, result.Issues[0].Kind);
        Assert.Contains("ordem.inventada", result.Issues[0].Message);
    }

    /// <summary>
    /// Frases num idioma que o app não está usando nunca serão ouvidas — a
    /// gramática é montada por idioma. Elas entram, mas isso é dito.
    /// </summary>
    [Fact]
    public void ImportingAProfileForAnotherLanguageSaysSo()
    {
        var result = PhraseProfiles.Merge(
            Map(), "en", Profile("pt", new() { ["cover"] = ["me protege"] }), []);

        Assert.True(result.LanguageMismatch);
        Assert.Equal(1, result.Added);
    }

    [Fact]
    public void TheSameLanguageIsNotFlagged() =>
        Assert.False(PhraseProfiles.Merge(
            Map(), "pt", Profile("pt", new() { ["cover"] = ["me protege"] }), []).LanguageMismatch);

    /// <summary>
    /// Importar duas vezes o mesmo perfil não pode duplicar frase: a segunda
    /// passada colide com a primeira e é recusada.
    /// </summary>
    [Fact]
    public void ImportingTwiceDoesNotDuplicate()
    {
        var mine = new Dictionary<string, List<string>>();
        var profile = Profile("pt", new() { ["cover"] = ["me protege"] });

        Assert.Equal(1, PhraseProfiles.Merge(Map(), "pt", profile, mine).Added);
        var again = PhraseProfiles.Merge(Map(), "pt", profile, mine);

        Assert.Equal(0, again.Added);
        Assert.Single(again.Issues);
        Assert.Single(mine["cover"]);
    }

    [Fact]
    public void AnEmptyPhraseIsRefusedRatherThanStored()
    {
        var result = PhraseProfiles.Merge(
            Map(), "pt", Profile("pt", new() { ["cover"] = ["   "] }), []);

        Assert.Equal(0, result.Added);
        Assert.Single(result.Issues);
    }

    /// <summary>
    /// O formal e o informal são a mesma frase depois da dobra do matcher, então
    /// um perfil com "abra com flash" não pode entrar em outra ordem.
    /// </summary>
    [Fact]
    public void TheVerbFoldingIsRespectedOnImport()
    {
        var result = PhraseProfiles.Merge(
            Map(), "pt", Profile("pt", new() { ["hold"] = ["abra com flash"] }), []);

        Assert.Equal(0, result.Added);
        Assert.Contains("door.open.flashbang", result.Issues[0].Message);
    }

    // ---- ida e volta pela tela ----

    /// <summary>
    /// O que a aba Comandos exporta tem que ser o que ela mostra. Se as duas
    /// coisas divergirem, alguem compartilha um perfil sem as frases dele.
    /// </summary>
    [Fact]
    public void TheCatalogueExportsExactlyThePhrasesItShows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-vm-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new RonVoice.App.ViewModels.CommandsViewModel(
                Map(), null, null, path, "pt");

            Assert.False(vm.HasOwnPhrases);

            var row = vm.Groups.SelectMany(g => g.Orders).First(o => o.Id == "hold");
            row.Draft = "fica quieto ai";
            row.AddCommand.Execute(null);

            Assert.True(vm.HasOwnPhrases);
            Assert.Equal(["fica quieto ai"], vm.PhrasesForExport()["hold"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// A copia exportada nao pode ser a lista viva do editor: exportar e depois
    /// mexer nas frases alteraria o que ja' foi entregue.
    /// </summary>
    [Fact]
    public void TheExportedCopyIsDetachedFromTheEditor()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-vm-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new RonVoice.App.ViewModels.CommandsViewModel(
                Map(), null, null, path, "pt");
            var row = vm.Groups.SelectMany(g => g.Orders).First(o => o.Id == "hold");
            row.Draft = "fica quieto ai";
            row.AddCommand.Execute(null);

            var snapshot = vm.PhrasesForExport();
            row.Draft = "para tudo agora";
            row.AddCommand.Execute(null);

            Assert.Single(snapshot["hold"]);
            Assert.Equal(2, vm.PhrasesForExport()["hold"].Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
