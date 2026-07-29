using System.Text.Json;

namespace RonVoice.Core.Speech;

public static class VoskResultParser
{
    public static RecognitionResult Parse(string json, bool isFinal)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return RecognitionResult.Empty(isFinal); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return RecognitionResult.Empty(isFinal);

            var key = isFinal ? "text" : "partial";
            var text = root.TryGetProperty(key, out var t) ? t.GetString() ?? "" : "";

            var words = new List<WordConfidence>();
            if (root.TryGetProperty("result", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var w in arr.EnumerateArray())
                    if (w.TryGetProperty("word", out var word) && w.TryGetProperty("conf", out var conf))
                        words.Add(new WordConfidence(word.GetString() ?? "", conf.GetDouble()));

            return new RecognitionResult(text, words, isFinal);
        }
    }
}
