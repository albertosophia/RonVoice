using RonVoice.Core.Commands;
using RonVoice.Core.Input;

namespace RonVoice.Tests;

public class KeyCatalogTests
{
    static InputToken Resolve(string name)
    {
        Assert.True(KeyCatalog.TryResolve(name, out var t), $"não resolveu: {name}");
        return t;
    }

    [Theory]
    [InlineData("One", 0x02)]
    [InlineData("Two", 0x03)]
    [InlineData("Nine", 0x0A)]
    [InlineData("Zero", 0x0B)]
    public void ResolvesDigits(string name, int scan) =>
        Assert.Equal(new ScanCodeToken((ushort)scan, false), Resolve(name));

    [Theory]
    [InlineData("F5", 0x3F)]
    [InlineData("F6", 0x40)]
    [InlineData("F7", 0x41)]
    [InlineData("F11", 0x57)]
    [InlineData("F12", 0x58)]
    public void ResolvesFunctionKeys(string name, int scan) =>
        Assert.Equal(new ScanCodeToken((ushort)scan, false), Resolve(name));

    [Theory]
    [InlineData("Z", 0x2C)]
    [InlineData("X", 0x2D)]
    [InlineData("C", 0x2E)]
    [InlineData("F", 0x21)]
    public void ResolvesLetters(string name, int scan) =>
        Assert.Equal(new ScanCodeToken((ushort)scan, false), Resolve(name));

    [Theory]
    [InlineData("LeftShift", 0x2A)]
    [InlineData("Tab", 0x0F)]
    [InlineData("SpaceBar", 0x39)]
    [InlineData("BackSpace", 0x0E)]
    [InlineData("CapsLock", 0x3A)]
    [InlineData("Escape", 0x01)]
    public void ResolvesNamedKeys(string name, int scan) =>
        Assert.Equal(new ScanCodeToken((ushort)scan, false), Resolve(name));

    [Theory]
    [InlineData("PageUp", 0x49)]
    [InlineData("PageDown", 0x51)]
    [InlineData("Delete", 0x53)]
    [InlineData("Up", 0x48)]
    [InlineData("Down", 0x50)]
    [InlineData("Left", 0x4B)]
    [InlineData("Right", 0x4D)]
    [InlineData("Divide", 0x35)]
    public void ExtendedKeysAreMarkedExtended(string name, int scan) =>
        Assert.Equal(new ScanCodeToken((ushort)scan, true), Resolve(name));

    [Fact]
    public void NumpadKeysAreNotExtended() =>
        Assert.Equal(new ScanCodeToken(0x50, false), Resolve("NumPadTwo"));

    [Theory]
    [InlineData("LeftMouseButton", MouseButton.Left)]
    [InlineData("RightMouseButton", MouseButton.Right)]
    [InlineData("MiddleMouseButton", MouseButton.Middle)]
    [InlineData("ThumbMouseButton", MouseButton.X1)]
    [InlineData("ThumbMouseButton2", MouseButton.X2)]
    public void ResolvesMouseButtons(string name, MouseButton b) =>
        Assert.Equal(new MouseToken(b), Resolve(name));

    [Fact]
    public void AcceptsTheKeybindDefaultsSpellingOfTheMenuButton() =>
        Assert.Equal(new MouseToken(MouseButton.Middle), Resolve("MiddleMouse"));

    [Fact]
    public void UnknownNameFailsInsteadOfGuessing() =>
        Assert.False(KeyCatalog.TryResolve("Xyzzy", out _));

    [Fact]
    public void ScrollWheelIsNotAKeyWeCanSend() =>
        Assert.False(KeyCatalog.TryResolve("MouseScrollUp", out _));

    /// <summary>
    /// Rede de segurança: todo nome de tecla que aparece no Input.ini de verdade
    /// tem que resolver, exceto eixos e roda de scroll.
    /// </summary>
    [Fact]
    public void ResolvesEveryDesktopKeyNameInTheRealFile()
    {
        var binds = KeybindReader.Read(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));
        var unresolved = binds.Values.Distinct()
            .Where(k => !k.StartsWith("Mouse", StringComparison.Ordinal)
                        || k.EndsWith("MouseButton", StringComparison.Ordinal))
            .Where(k => !KeyCatalog.TryResolve(k, out _))
            .ToList();
        Assert.Empty(unresolved);
    }
}
