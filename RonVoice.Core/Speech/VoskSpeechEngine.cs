using Vosk;

namespace RonVoice.Core.Speech;

/// <summary>
/// Reconhecedor Vosk com gramática fechada. A gramática é imutável: o binding
/// não expõe SetGrammar, então trocar de idioma exige recriar esta instância
/// junto com o modelo.
/// </summary>
public sealed class VoskSpeechEngine : ISpeechEngine
{
    readonly Model _model;
    readonly VoskRecognizer _recognizer;
    readonly Lock _lock = new();
    bool _disposed;

    public event Action<RecognitionResult>? OnRecognized;

    public VoskSpeechEngine(string modelPath, string grammarJson, float sampleRate = 16000f)
    {
        Vosk.Vosk.SetLogLevel(-1);          // a lib nativa é falante demais por padrão
        _model = new Model(modelPath);
        // NÃO passe grammarJson direto: o binding entrega em ANSI e toda palavra
        // acentuada é descartada sem um ruído. Ver VoskGrammar.
        _recognizer = new VoskRecognizer(
            _model, sampleRate, VoskGrammar.ForNativeCall(grammarJson));
        _recognizer.SetWords(true);         // é o que traz confiança por palavra
    }

    public void Feed(ReadOnlyMemory<byte> audio)
    {
        if (audio.Length == 0) return;

        lock (_lock)
        {
            if (_disposed) return;
            var buffer = audio.ToArray();
            var endOfUtterance = _recognizer.AcceptWaveform(buffer, buffer.Length);
            var json = endOfUtterance ? _recognizer.Result() : _recognizer.PartialResult();
            var result = VoskResultParser.Parse(json, endOfUtterance);
            if (endOfUtterance || result.Text.Length > 0)
                OnRecognized?.Invoke(result);
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            if (_disposed) return;
            OnRecognized?.Invoke(VoskResultParser.Parse(_recognizer.FinalResult(), isFinal: true));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            if (!_disposed) _recognizer.Reset();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _recognizer.Dispose();
            _model.Dispose();
        }
    }
}
