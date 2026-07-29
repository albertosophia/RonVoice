using RonVoice.App.ViewModels;
using RonVoice.Core.Commands;
using RonVoice.Core.Config;

namespace RonVoice.Tests;

public class CommandsViewModelTests
{
    static CommandsViewModel Vm() => new(CommandMap.Load(CommandMapTests.MapPath));

    [Fact]
    public void ShowsEveryOrderWhenTheSearchIsEmpty() =>
        Assert.Equal(70, Vm().TotalShown);

    [Fact]
    public void GroupsByContext()
    {
        var contexts = Vm().Groups.Select(g => g.Context).ToList();
        Assert.Contains("door", contexts);
        Assert.Contains("person", contexts);
        Assert.Equal(contexts.Count, contexts.Distinct().Count());
    }

    [Fact]
    public void FindsAnOrderByAnEnglishPhrase()
    {
        var vm = Vm();
        vm.Search = "flashbang";
        Assert.Contains(vm.Groups.SelectMany(g => g.Orders),
                        o => o.Id == "door.open.flashbang");
    }

    /// <summary>
    /// O catalogo e' a tela inicial porque o primeiro problema de quem instala
    /// e' nao saber o que falar — e ele pode nao falar ingles.
    /// </summary>
    [Fact]
    public void FindsAnOrderByAPortuguesePhrase()
    {
        var vm = Vm();
        vm.Search = "empilha";
        Assert.Contains(vm.Groups.SelectMany(g => g.Orders),
                        o => o.Id.StartsWith("door.stack", StringComparison.Ordinal));
    }

    [Fact]
    public void FindsAnOrderById()
    {
        var vm = Vm();
        vm.Search = "door.disarm";
        Assert.Single(vm.Groups.SelectMany(g => g.Orders));
    }

    [Fact]
    public void SearchIgnoresAccentsAndCase()
    {
        var vm = Vm();
        vm.Search = "POSIÇÃO";
        var withAccent = vm.TotalShown;
        vm.Search = "posicao";
        Assert.Equal(withAccent, vm.TotalShown);
    }

    [Fact]
    public void AnUnmatchedSearchShowsNothingRatherThanEverything()
    {
        var vm = Vm();
        vm.Search = "xyzzy-nao-existe";
        Assert.Equal(0, vm.TotalShown);
    }

    /// <summary>
    /// 25 ordens estao marcadas confidence: verify e podem nao funcionar em jogo.
    /// Sem o selo, viram "esse comando esta quebrado".
    /// </summary>
    [Fact]
    public void FlagsTheOrdersThatWereNeverVerifiedInGame()
    {
        var flagged = Vm().Groups.SelectMany(g => g.Orders).Count(o => o.NeedsVerification);
        Assert.Equal(25, flagged);
    }

    [Fact]
    public void EachRowCarriesBothLanguagesAndTheMenuPath()
    {
        var row = Vm().Groups.SelectMany(g => g.Orders).First(o => o.Id == "door.stack.left");
        Assert.NotEmpty(row.PhrasesEn);
        Assert.NotEmpty(row.PhrasesPt);
        Assert.Equal("MENU 1 2", row.PathText);
    }

    [Fact]
    public void ChangingTheSearchRaisesPropertyChanged()
    {
        var vm = Vm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Search = "stack";

        Assert.Contains(nameof(vm.Groups), changed);
        Assert.Contains(nameof(vm.TotalShown), changed);
    }

    [Fact]
    public void GroupHeaderTellsThePlayerWhereToLook()
    {
        var door = Vm().Groups.First(g => g.Context == "door");
        Assert.Contains("porta", door.Header, StringComparison.OrdinalIgnoreCase);
    }

    static Dictionary<string, IReadOnlyList<string>> Custom() => new()
    {
        ["door.stack.left"] = new[] { "cola na esquerda" },
    };

    [Fact]
    public void MarksTheRowsThatCarryUserPhrases()
    {
        var vm = new CommandsViewModel(CommandMap.Load(CommandMapTests.MapPath), Custom());

        var rows = vm.Groups.SelectMany(g => g.Orders).ToList();
        Assert.True(rows.First(o => o.Id == "door.stack.left").HasCustomPhrases);
        Assert.False(rows.First(o => o.Id == "hold").HasCustomPhrases);
    }

    [Fact]
    public void ShowsTheUserPhrasesSeparatelyFromTheFactoryOnes()
    {
        var vm = new CommandsViewModel(CommandMap.Load(CommandMapTests.MapPath), Custom());
        var row = vm.Groups.SelectMany(g => g.Orders).First(o => o.Id == "door.stack.left");

        Assert.Contains("cola na esquerda", row.CustomPhrasesText);
    }

    /// <summary>
    /// Quem escreveu o arquivo precisa VER que uma linha dele foi recusada.
    /// Num log ninguem olha.
    /// </summary>
    [Fact]
    public void SurfacesTheIssuesSoARefusedPhraseIsNotSilent()
    {
        var issues = new List<PhraseIssue>
        {
            new(PhraseIssueKind.Collision, "hold", "empilha",
                "\"empilha\" já pertence a door.stack.auto"),
        };
        var vm = new CommandsViewModel(
            CommandMap.Load(CommandMapTests.MapPath), null, issues);

        Assert.True(vm.HasIssues);
        Assert.Contains("empilha", vm.IssuesText);
        Assert.Contains("door.stack.auto", vm.IssuesText);
    }

    [Fact]
    public void NoIssuesMeansNothingIsShown() => Assert.False(Vm().HasIssues);
}
