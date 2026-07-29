using RonVoice.Core.Commands;
using RonVoice.Core.Config;

namespace RonVoice.Tests;

public class CustomPhraseStoreTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"ronvoice-store-{Guid.NewGuid():N}.json");

    [Fact]
    public void ReadingAMissingFileGivesAnEmptySet() =>
        Assert.Empty(CustomPhraseStore.Read(TempPath()));

    [Fact]
    public void WriteThenReadRoundTrips()
    {
        var path = TempPath();
        try
        {
            var content = new Dictionary<string, List<string>>
            {
                ["hold"] = ["fica quieto", "para tudo"],
            };
            CustomPhraseStore.Write(path, content);

            var read = CustomPhraseStore.Read(path);
            Assert.Equal(["fica quieto", "para tudo"], read["hold"]);
        }
        finally { File.Delete(path); }
    }

    /// <summary>O arquivo e' para humanos lerem; acento escapado atrapalharia.</summary>
    [Fact]
    public void AccentsAreWrittenLiterallyNotEscaped()
    {
        var path = TempPath();
        try
        {
            CustomPhraseStore.Write(path, new() { ["hold"] = ["fica na posição"] });
            Assert.Contains("posição", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnEmptySetDeletesTheFileInsteadOfLeavingAnEmptyOne()
    {
        var path = TempPath();
        CustomPhraseStore.Write(path, new() { ["hold"] = ["x"] });
        Assert.True(File.Exists(path));

        CustomPhraseStore.Write(path, []);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void OrdersLeftWithNoPhrasesAreDropped()
    {
        var path = TempPath();
        try
        {
            CustomPhraseStore.Write(path, new()
            {
                ["hold"] = ["fica quieto"],
                ["cover"] = [],
            });
            Assert.False(CustomPhraseStore.Read(path).ContainsKey("cover"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddAppendsAndPersists()
    {
        var path = TempPath();
        try
        {
            var content = new Dictionary<string, List<string>>();
            CustomPhraseStore.Add(path, "hold", "fica quieto", content);

            Assert.Contains("fica quieto", CustomPhraseStore.Read(path)["hold"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AddTrimsSurroundingSpace()
    {
        var path = TempPath();
        try
        {
            var content = new Dictionary<string, List<string>>();
            CustomPhraseStore.Add(path, "hold", "  fica quieto  ", content);
            Assert.Contains("fica quieto", CustomPhraseStore.Read(path)["hold"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RemoveTakesThePhraseOutAndPersists()
    {
        var path = TempPath();
        try
        {
            var content = new Dictionary<string, List<string>>
            {
                ["hold"] = ["fica quieto", "para tudo"],
            };
            CustomPhraseStore.Write(path, content);
            CustomPhraseStore.Remove(path, "hold", "fica quieto", content);

            Assert.Equal(["para tudo"], CustomPhraseStore.Read(path)["hold"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RemovingTheLastPhraseOfAnOrderDropsTheOrder()
    {
        var path = TempPath();
        try
        {
            var content = new Dictionary<string, List<string>> { ["hold"] = ["fica quieto"] };
            CustomPhraseStore.Write(path, content);
            CustomPhraseStore.Remove(path, "hold", "fica quieto", content);

            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- validacao antes de gravar ----

    [Fact]
    public void AGoodPhraseIsAccepted() =>
        Assert.Null(CustomPhraseStore.Reject(Map(), "hold", "fica quieto ai", "pt"));

    [Fact]
    public void AnEmptyPhraseIsRejected() =>
        Assert.NotNull(CustomPhraseStore.Reject(Map(), "hold", "   ", "pt"));

    /// <summary>
    /// A checagem tem que rodar ANTES de gravar: descobrir depois significaria
    /// duas ordens mudas sem erro nenhum.
    /// </summary>
    [Fact]
    public void APhraseFromAnotherOrderIsRejectedNamingIt()
    {
        var reason = CustomPhraseStore.Reject(Map(), "hold", "empilha", "pt");
        Assert.NotNull(reason);
        Assert.Contains("door.stack.auto", reason);
    }

    [Fact]
    public void APhraseAlreadyOnTheSameOrderIsRejectedDifferently()
    {
        var reason = CustomPhraseStore.Reject(Map(), "door.stack.auto", "empilha", "pt");
        Assert.NotNull(reason);
        Assert.Contains("nesta ordem", reason);
    }

    [Fact]
    public void RejectionIgnoresCaseAccentAndPunctuation() =>
        Assert.NotNull(CustomPhraseStore.Reject(Map(), "hold", "Empilha!", "pt"));

    [Fact]
    public void TheOtherLanguageDoesNotCauseAFalseRejection() =>
        Assert.Null(CustomPhraseStore.Reject(Map(), "hold", "empilha", "en"));
}
