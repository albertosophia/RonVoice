using RonVoice.App.ViewModels;
using RonVoice.Core.Commands;

namespace RonVoice.Tests;

/// <summary>
/// A busca do catálogo. Antes era substring contígua sobre um campo isolado de
/// cada vez, o que falhava no jeito que as pessoas buscam de verdade: palavras
/// soltas, fora de ordem, e espalhadas entre nome, frase e contexto.
/// </summary>
public class SearchTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static CommandsViewModel Vm(string language = "pt") =>
        new(Map(), null, null, null, language);

    static IReadOnlyList<string> Find(string search, string language = "pt")
    {
        var vm = Vm(language);
        vm.Search = search;
        return [.. vm.Groups.SelectMany(g => g.Orders).Select(o => o.Id)];
    }

    [Fact]
    public void AnEmptySearchShowsEverything() => Assert.Equal(70, Find("").Count);

    /// <summary>
    /// O caso que não funcionava: as duas palavras existem, mas fora de ordem e
    /// em campos diferentes.
    /// </summary>
    [Fact]
    public void WordsOutOfOrderStillMatch()
    {
        Assert.Contains("door.open.flashbang", Find("flash porta"));
        Assert.Contains("door.open.flashbang", Find("porta flash"));
    }

    [Fact]
    public void EveryTermHasToAppearNotJustOne()
    {
        var found = Find("escopeta gas");

        Assert.Contains("door.breach.shotgun.gas", found);
        Assert.DoesNotContain("door.breach.shotgun.flashbang", found);
        Assert.DoesNotContain("door.breach.c2.gas", found);
    }

    /// <summary>Quem digita "empil" ainda está no meio da palavra.</summary>
    [Fact]
    public void APrefixIsEnough() => Assert.Contains("door.stack.auto", Find("empil"));

    [Fact]
    public void FindsByTheReadableName() =>
        Assert.Contains("confirm.default", Find("ordem padrao"));

    /// <summary>
    /// O id continua buscável: é a chave do minhas_frases.json, e quem editou
    /// aquele arquivo procura por ela.
    /// </summary>
    [Fact]
    public void StillFindsByTheId() =>
        Assert.Equal(["player.fireselect"], Find("player.fireselect"));

    /// <summary>
    /// "porta" acha as ordens de porta mesmo quando a palavra não está na frase,
    /// porque o contexto entra na busca.
    /// </summary>
    [Fact]
    public void FindsByContext() => Assert.True(Find("door").Count >= 40);

    [Fact]
    public void AccentsDoNotMatter()
    {
        Assert.Equal(Find("gas").Count, Find("gás").Count);
        Assert.Contains("door.open.launcher", Find("lançador"));
    }

    /// <summary>A dobra de formas verbais vale na busca: "abra" acha "abre".</summary>
    [Fact]
    public void TheFormalImperativeFindsTheSameOrders() =>
        Assert.Equal(Find("abre com flash"), Find("abra com flash"));

    [Fact]
    public void SomethingThatDoesNotExistFindsNothing() =>
        Assert.Empty(Find("bazuca nuclear"));

    [Fact]
    public void TheCountTextSaysHowManyOfHowMany()
    {
        var vm = Vm();
        vm.Search = "escopeta";

        Assert.Contains("de 70", vm.CountText);
    }

    /// <summary>Frase própria do usuário também é achável pela busca.</summary>
    [Fact]
    public void FindsAUserPhrase()
    {
        var vm = new CommandsViewModel(
            Map(),
            new Dictionary<string, IReadOnlyList<string>> { ["hold"] = new[] { "fica quieto ai" } },
            null, null, "pt");

        vm.Search = "quieto";
        Assert.Equal(["hold"], vm.Groups.SelectMany(g => g.Orders).Select(o => o.Id));
    }
}
