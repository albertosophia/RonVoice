using RonVoice.Core.Config;

namespace RonVoice.Tests;

public class SettingsStoreTests
{
    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"ronvoice-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void DefaultsAreTheFactorySettings()
    {
        var d = AppSettings.Default;
        Assert.Equal("en", d.Language);
        Assert.Null(d.GameExecutablePath);
        Assert.Equal(0, d.MicrophoneDevice);
        // Sempre-ligado e' o padrao de fabrica; PTT e' opcional.
        Assert.Equal(ListenModeSetting.AlwaysOn, d.Mode);
        Assert.Null(d.PushToTalkKey);
        Assert.Equal(0.0, d.ConfidenceThreshold);
    }

    [Fact]
    public void SavesAndLoadsARoundTrip()
    {
        var dir = TempDir();
        try
        {
            var settings = AppSettings.Default with
            {
                Language = "pt",
                GameExecutablePath = @"C:\Games\ReadyOrNot.exe",
                MicrophoneDevice = 3,
                Mode = ListenModeSetting.PushToTalk,
                PushToTalkKey = "ThumbMouseButton",
                ConfidenceThreshold = 0.65,
            };
            var (path, _) = SettingsStore.ResolvePath(dir);
            SettingsStore.Save(settings, path);

            var (loaded, _, _) = SettingsStore.Load(dir);
            Assert.Equal(settings, loaded);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var dir = TempDir();
        try
        {
            var (loaded, _, _) = SettingsStore.Load(dir);
            Assert.Equal(AppSettings.Default, loaded);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Um arquivo corrompido nao pode impedir o app de abrir: o usuario ficaria
    /// sem nenhuma forma de corrigir, porque a correcao e' pela propria tela.
    /// </summary>
    [Fact]
    public void CorruptFileFallsBackToDefaultsInsteadOfThrowing()
    {
        var dir = TempDir();
        try
        {
            var (path, _) = SettingsStore.ResolvePath(dir);
            File.WriteAllText(path, "{ isto nao e json valido ");

            var (loaded, _, _) = SettingsStore.Load(dir);
            Assert.Equal(AppSettings.Default, loaded);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnknownFieldsInTheFileAreIgnored()
    {
        var dir = TempDir();
        try
        {
            var (path, _) = SettingsStore.ResolvePath(dir);
            File.WriteAllText(path, """{"language":"pt","campoQueNaoExiste":42}""");

            var (loaded, _, _) = SettingsStore.Load(dir);
            Assert.Equal("pt", loaded.Language);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AWritableDirectoryIsPortable()
    {
        var dir = TempDir();
        try
        {
            var (path, portable) = SettingsStore.ResolvePath(dir);
            Assert.True(portable);
            Assert.Equal(Path.Combine(dir, "settings.json"), path);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Program Files nao e' gravavel sem elevacao. Cair para %APPDATA% mantem o
    /// app funcional, mas ele deixa de ser portable e a tela precisa avisar.
    /// </summary>
    [Fact]
    public void AnUnwritableDirectoryFallsBackToAppDataAndIsNotPortable()
    {
        var unwritable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "RonVoice-nao-existe-de-verdade");

        var (path, portable) = SettingsStore.ResolvePath(unwritable);

        Assert.False(portable);
        Assert.Contains("RonVoice", path);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), path);
    }
}
