using System.IO.Compression;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class ModelDownloaderTests
{
    static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"ronvoice-dl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Cria um zip com a forma de um modelo Vosk valido (layout classico).</summary>
    static string MakeModelZip(string dir, string modelName, bool valid)
    {
        var staging = Path.Combine(dir, "staging", modelName);
        Directory.CreateDirectory(staging);
        if (valid)
        {
            Directory.CreateDirectory(Path.Combine(staging, "am"));
            Directory.CreateDirectory(Path.Combine(staging, "conf"));
            File.WriteAllText(Path.Combine(staging, "am", "final.mdl"), "x");
        }
        else
        {
            File.WriteAllText(Path.Combine(staging, "leia-me.txt"), "conteudo errado");
        }

        var zip = Path.Combine(dir, modelName + ".zip");
        ZipFile.CreateFromDirectory(Path.Combine(dir, "staging"), zip);
        Directory.Delete(Path.Combine(dir, "staging"), true);
        return zip;
    }

    [Fact]
    public void KnowsBothLanguages()
    {
        Assert.True(ModelDownloader.Specs.ContainsKey("en"));
        Assert.True(ModelDownloader.Specs.ContainsKey("pt"));
        Assert.All(ModelDownloader.Specs.Values,
            s => Assert.StartsWith("https://", s.Url));
    }

    [Fact]
    public void InstallsAValidModel()
    {
        var dir = TempDir();
        try
        {
            var spec = new ModelSpec("en", "modelo-teste", "https://exemplo", 1);
            var zip = MakeModelZip(dir, "modelo-teste", valid: true);
            var models = Path.Combine(dir, "models");

            var installed = ModelDownloader.InstallFromZip(zip, models, spec);

            Assert.True(Directory.Exists(installed));
            Assert.True(ModelLocator.LooksLikeAModel(installed));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// O caso grave: um zip incompleto ou errado nao pode virar uma pasta de
    /// modelo pela metade. A biblioteca nativa do Vosk ABORTA o processo diante
    /// de um modelo invalido, em vez de lancar excecao — o app fecharia sem
    /// mensagem e voltaria a fechar na abertura seguinte.
    /// </summary>
    [Fact]
    public void RefusesAZipThatIsNotAModel()
    {
        var dir = TempDir();
        try
        {
            var spec = new ModelSpec("en", "modelo-ruim", "https://exemplo", 1);
            var zip = MakeModelZip(dir, "modelo-ruim", valid: false);
            var models = Path.Combine(dir, "models");

            Assert.Throws<InvalidDataException>(
                () => ModelDownloader.InstallFromZip(zip, models, spec));

            Assert.False(Directory.Exists(Path.Combine(models, "modelo-ruim")));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Uma instalacao que falha nao pode destruir o modelo que ja funcionava.
    /// </summary>
    [Fact]
    public void AFailedInstallLeavesTheExistingModelIntact()
    {
        var dir = TempDir();
        try
        {
            var models = Path.Combine(dir, "models");
            var existing = Path.Combine(models, "modelo-ruim");
            Directory.CreateDirectory(Path.Combine(existing, "am"));
            Directory.CreateDirectory(Path.Combine(existing, "conf"));
            File.WriteAllText(Path.Combine(existing, "marcador.txt"), "nao me apague");

            var spec = new ModelSpec("en", "modelo-ruim", "https://exemplo", 1);
            var zip = MakeModelZip(dir, "modelo-ruim", valid: false);

            Assert.Throws<InvalidDataException>(
                () => ModelDownloader.InstallFromZip(zip, models, spec));

            Assert.True(File.Exists(Path.Combine(existing, "marcador.txt")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReplacesAnExistingModelWhenTheNewOneIsValid()
    {
        var dir = TempDir();
        try
        {
            var models = Path.Combine(dir, "models");
            var existing = Path.Combine(models, "modelo-teste");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "antigo.txt"), "velho");

            var spec = new ModelSpec("en", "modelo-teste", "https://exemplo", 1);
            var zip = MakeModelZip(dir, "modelo-teste", valid: true);

            var installed = ModelDownloader.InstallFromZip(zip, models, spec);

            Assert.False(File.Exists(Path.Combine(installed, "antigo.txt")));
            Assert.True(ModelLocator.LooksLikeAModel(installed));
        }
        finally { Directory.Delete(dir, true); }
    }
}
