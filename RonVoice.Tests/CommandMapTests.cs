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
}
