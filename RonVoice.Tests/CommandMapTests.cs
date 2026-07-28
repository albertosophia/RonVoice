namespace RonVoice.Tests;

public class CommandMapTests
{
    public static string MapPath =>
        Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json");

    [Fact]
    public void MapFileIsCopiedToOutput()
    {
        Assert.True(File.Exists(MapPath), $"não encontrado: {MapPath}");
    }
}
