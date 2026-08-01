using RonVoice.Core.Commands;

namespace RonVoice.Tests;

public class CommandMapTests
{
    public static string MapPath =>
        Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json");

    static CommandMap Load() => CommandMap.Load(MapPath);

    [Fact]
    public void MapFileIsCopiedToOutput() =>
        Assert.True(File.Exists(MapPath), $"não encontrado: {MapPath}");

    [Fact]
    public void LoadsSeventyOrders() => Assert.Equal(70, Load().Orders.Count);

    [Fact]
    public void OrderIdsAreUnique()
    {
        // Orders é um dicionário por id; conferimos contra o array cru do JSON
        var raw = System.Text.Json.JsonDocument.Parse(File.ReadAllText(MapPath));
        var ids = raw.RootElement.GetProperty("orders").EnumerateArray()
            .Select(o => o.GetProperty("id").GetString()!).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ReadsAKnownOrder()
    {
        var o = Load().Orders["door.open.flashbang"];
        Assert.Equal("door", o.Context);
        Assert.Equal(new[] { "MENU", "2", "2" }, o.Path);
        Assert.Contains("open with flashbang", o.Phrases["en"]);
        Assert.Contains("abre com flash", o.Phrases["pt"]);
    }

    [Fact]
    public void CloseMenuDefaultsToFalseWhenAbsent() =>
        Assert.False(Load().Orders["door.stack.auto"].CloseMenu);

    [Fact]
    public void ReadsElementsWithKeys()
    {
        var map = Load();
        Assert.Equal("F7", map.Elements["red"].Key);
        Assert.Contains("red team", map.Elements["red"].Aliases["en"]);
        Assert.Contains("team", map.Elements["gold"].Aliases["en"]);
    }

    [Fact]
    public void ReadsQueueModifier() =>
        Assert.Contains("prep", Load().Queue.Aliases["en"]);

    [Fact]
    public void ReadsTimingAndDefaults()
    {
        var map = Load();
        Assert.Equal(35, map.Timing.KeyHoldMs);
        Assert.Equal(35, map.Timing.GapBetweenKeysMs);
        Assert.Equal(60, map.Timing.MenuOpenSettleMs);
        Assert.Equal("MiddleMouse", map.Defaults.SwatCommandMenu);
        Assert.Equal("LeftShift", map.Defaults.HoldCommand);
        Assert.Equal(9, map.Defaults.CommandKeys.Count);
    }

    [Fact]
    public void EveryPathTokenIsWellFormed()
    {
        foreach (var o in Load().Orders.Values)
            foreach (var t in o.Path)
                Assert.True(
                    t == "MENU" || (t.Length == 1 && t[0] >= '1' && t[0] <= '9')
                        || t.StartsWith("KEY:", StringComparison.Ordinal),
                    $"token inesperado {t} em {o.Id}");
    }

    [Fact]
    public void DuplicatePhrasesWereRemoved()
    {
        var m = Load();
        Assert.DoesNotContain("drop a chemlight", m.Orders["deploy.chemlight"].Phrases["en"]);
        Assert.DoesNotContain("solta a luz",      m.Orders["deploy.chemlight"].Phrases["pt"]);
        Assert.DoesNotContain("para",             m.Orders["player.yell"].Phrases["pt"]);
        Assert.DoesNotContain("go",               m.Orders["move.to"].Phrases["en"]);
        Assert.DoesNotContain("leader leader and clear",
                              m.Orders["door.breach.leader.leader"].Phrases["en"]);
    }

    [Fact]
    public void SurvivingPhrasesAreStillThere()
    {
        var m = Load();
        Assert.Contains("drop chemlight", m.Orders["player.chemlight"].Phrases["en"]);
        Assert.Contains("solta luz",      m.Orders["player.chemlight"].Phrases["pt"]);
        Assert.Contains("para",           m.Orders["hold"].Phrases["pt"]);
        Assert.Contains("go go go",       m.Orders["confirm.default"].Phrases["en"]);
        Assert.Contains("leader and clear",
                        m.Orders["door.breach.leader.clear"].Phrases["en"]);
    }

    [Fact]
    public void PhraseCountsMatchSpec()
    {
        var m = Load();
        Assert.Equal(438, m.Orders.Values.Sum(o => o.Phrases["en"].Count));
        Assert.Equal(427, m.Orders.Values.Sum(o => o.Phrases["pt"].Count));
    }

    [Fact]
    public void NoOrderLosesAllPhrases()
    {
        foreach (var o in Load().Orders.Values)
        {
            Assert.NotEmpty(o.Phrases["en"]);
            Assert.NotEmpty(o.Phrases["pt"]);
        }
    }

    [Fact]
    public void CloseMenuSeedIsExactlyNineteenOrders()
    {
        var expected = new[]
        {
            "cover", "deploy.chemlight", "deploy.flashbang", "deploy.gas",
            "deploy.shield", "deploy.stinger", "door.disarm", "door.open.flashbang",
            "door.open.gas", "door.open.stinger", "door.stack.left",
            "door.stack.right", "door.stack.split", "door.toggle", "door.wedge",
            "move.fallin", "move.to", "person.restrain", "search",
        };
        var actual = Load().Orders.Values.Where(o => o.CloseMenu)
                         .Select(o => o.Id).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), actual);
    }
}
