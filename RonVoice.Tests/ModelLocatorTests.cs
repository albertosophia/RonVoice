using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class ModelLocatorTests
{
    static string ModelsDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "models");

    [Theory]
    [InlineData("en", "vosk-model-small-en-us-0.15")]
    [InlineData("pt", "vosk-model-small-pt-0.3")]
    public void FindsTheModelForALanguage(string lang, string expectedDir)
    {
        var path = ModelLocator.Find(lang, ModelsDir);
        Assert.EndsWith(expectedDir, path.TrimEnd(Path.DirectorySeparatorChar));
        Assert.True(ModelLocator.LooksLikeAModel(path), $"não parece um modelo Vosk: {path}");
    }

    /// <summary>
    /// Os dois modelos que este projeto usa vêm em formatos diferentes, e ambos
    /// são válidos. Exigir só um deles rejeitaria metade dos idiomas.
    /// </summary>
    [Fact]
    public void AcceptsBothTheClassicAndTheFlatModelLayout()
    {
        var classic = Path.Combine(ModelsDir, "vosk-model-small-en-us-0.15");
        var flat = Path.Combine(ModelsDir, "vosk-model-small-pt-0.3");

        Assert.True(Directory.Exists(Path.Combine(classic, "am")), "en deveria ter am/");
        Assert.False(Directory.Exists(Path.Combine(flat, "am")), "pt não tem am/");
        Assert.True(File.Exists(Path.Combine(flat, "final.mdl")), "pt deveria ter final.mdl");

        Assert.True(ModelLocator.LooksLikeAModel(classic));
        Assert.True(ModelLocator.LooksLikeAModel(flat));
    }

    /// <summary>
    /// Rodando do repositório, a saída fica em bin/Debug/net10.0-windows e os
    /// modelos na raiz. Copiá-los para cada saída custaria 118 MB por projeto.
    /// </summary>
    [Fact]
    public void FindsTheModelsDirectoryByWalkingUpFromTheOutputFolder()
    {
        var found = ModelLocator.FindModelsDirectory();
        Assert.NotNull(found);
        Assert.True(Directory.Exists(found), $"não existe: {found}");
        Assert.EndsWith(Path.Combine("data", "models"), found!.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FindingModelsGivesUpInsteadOfWalkingToTheDriveRoot()
    {
        var deep = Path.Combine(Path.GetTempPath(), $"ronvoice-fundo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(deep);
        try { Assert.Null(ModelLocator.FindModelsDirectory(deep)); }
        finally { Directory.Delete(deep); }
    }

    [Fact]
    public void ResolvesWithoutAnExplicitDirectory() =>
        Assert.True(ModelLocator.LooksLikeAModel(ModelLocator.Find("en")));

    [Fact]
    public void RejectsADirectoryThatIsNotAModel()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"ronvoice-vazio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try { Assert.False(ModelLocator.LooksLikeAModel(empty)); }
        finally { Directory.Delete(empty); }
    }

    [Fact]
    public void UnknownLanguageThrowsNamingIt()
    {
        var ex = Assert.Throws<ModelNotFoundException>(() => ModelLocator.Find("de", ModelsDir));
        Assert.Contains("de", ex.Message);
    }

    [Fact]
    public void MissingDirectoryThrowsWithTheExpectedPath()
    {
        var ex = Assert.Throws<ModelNotFoundException>(
            () => ModelLocator.Find("en", Path.Combine(Path.GetTempPath(), "nao-existe-ronvoice")));
        Assert.Contains("nao-existe-ronvoice", ex.Message);
    }

    [Theory]
    [InlineData("vosk-model-small-en-us-0.15", "en")]
    [InlineData("vosk-model-small-pt-0.3", "pt")]
    [InlineData("qualquer-coisa", null)]
    public void DerivesLanguageFromDirectoryName(string dir, string? expected) =>
        Assert.Equal(expected, ModelLocator.LanguageOf(dir));
}
