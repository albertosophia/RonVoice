using RonVoice.Core.Commands;

namespace RonVoice.Tests;

public class KeybindReaderTests
{
    static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void ReadsSwatBindsFromRealFile()
    {
        var b = KeybindReader.Read(Fixture("Input.full.ini"));
        Assert.Equal("MiddleMouseButton", b["OpenSwatCommand"]);
        Assert.Equal("LeftShift", b["HoldGoCode"]);
        Assert.Equal("Z", b["IssueDefaultCommand"]);
        Assert.Equal("F7", b["SelectElementRed"]);
        Assert.Equal("Two", b["SwatInputKeyTwo"]);
        Assert.Equal("Nine", b["SwatInputKeyNine"]);
    }

    [Fact]
    public void PrefersKeyboardOrMouseOverGamepad() =>
        Assert.Equal("LeftMouseButton", KeybindReader.Read(Fixture("Input.full.ini"))["Fire"]);

    [Fact]
    public void IgnoresAxisMappings() =>
        Assert.False(KeybindReader.Read(Fixture("Input.full.ini")).ContainsKey("MoveForward"));

    [Fact]
    public void OmitsActionsBoundToNone() =>
        Assert.False(KeybindReader.Read(Fixture("Input.none.ini")).ContainsKey("Yell"));

    [Fact]
    public void MissingActionsAreSimplyAbsent() =>
        Assert.False(KeybindReader.Read(Fixture("Input.missing.ini")).ContainsKey("OpenSwatCommand"));

    [Fact]
    public void NonexistentFileYieldsEmptyMap() =>
        Assert.Empty(KeybindReader.Read(Fixture("nao-existe.ini")));
}
