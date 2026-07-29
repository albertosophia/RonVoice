using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Speech;
using Vosk;

namespace RonVoice.Tests;

/// <summary>
/// Mede se o Vosk compõe livremente entre entradas da gramática ou casa cada
/// entrada como frase inteira. A resposta decide entre a lista plana e o
/// produto cartesiano. Ver seção 5.3 da spec.
/// </summary>
public class GrammarCompositionTests
{
    static string ModelsDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "models");
    static string AudioDir => Path.Combine(AppContext.BaseDirectory, "audio");

    internal static string Recognize(string wavPath, string grammarJson)
    {
        Vosk.Vosk.SetLogLevel(-1);
        using var model = new Model(ModelLocator.Find("en", ModelsDir));
        using var rec = new VoskRecognizer(model, 16000.0f, grammarJson);
        rec.SetWords(true);

        using var fs = File.OpenRead(wavPath);
        fs.Seek(44, SeekOrigin.Begin);           // pula o cabeçalho WAV
        var buffer = new byte[4096];
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            rec.AcceptWaveform(buffer, read);

        using var doc = JsonDocument.Parse(rec.FinalResult());
        return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
    }

    static string Grammar() =>
        GrammarBuilder.Build(CommandMap.Load(CommandMapTests.MapPath), "en");

    [Fact]
    public void ALiteralGrammarEntryIsRecognized()
    {
        var wav = Path.Combine(AudioDir, "stack_left.wav");
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        Assert.Equal("stack left", Recognize(wav, Grammar()));
    }

    /// <summary>
    /// A pergunta que decidiu o design: o Vosk compõe entre entradas da gramática,
    /// ou casa cada entrada como frase inteira? Medido: compõe. Um enunciado com
    /// elemento + modificador de fila + ordem — três entradas independentes —
    /// volta inteiro. Por isso a lista plana basta e o produto cartesiano de
    /// ~3.200 combinações não é necessário.
    /// </summary>
    [Fact]
    public void ComposesThreeSeparateGrammarEntriesIntoOneUtterance()
    {
        var wav = Path.Combine(AudioDir, "blue_team_prep_stack_left.wav");
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        Assert.Equal("blue team prep stack left", Recognize(wav, Grammar()));
    }

    /// <summary>
    /// O reconhecedor pode quebrar uma palavra composta em duas quando as duas
    /// existem no vocabulário: "flashbang" volta como "flash bang", porque a
    /// gramática também contém "bang" isolado. Não é defeito de composição, e o
    /// PhraseMatcher absorve — mas fica registrado, porque uma quebra dessas num
    /// token que fosse o único discriminante custaria a ordem.
    /// </summary>
    [Fact]
    public void MaySplitACompoundWordThatAlsoExistsAsTwoWords()
    {
        var wav = Path.Combine(AudioDir, "red_team_open_with_flashbang.wav");
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        var heard = Recognize(wav, Grammar());

        Assert.StartsWith("red team", heard);
        Assert.Contains("open with flash", heard);
    }
}
