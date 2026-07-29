# RonVoice — camada de fala (etapa 5) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Falar ao microfone produz o mesmo intent que `ronvoice test` produz por texto, e ruído ou fala fora do vocabulário não dispara nada.

**Architecture:** A camada de fala entrega texto ao `PhraseMatcher` que já existe e está validado — nada das etapas 1–4 muda. O áudio flui microfone → `ListenGate` → Vosk → matcher → resolver → `SendInput`, com três filas de um consumidor cada. Um ícone de bandeja mostra o estado e permite mutar, porque o microfone fica sempre ligado.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), Vosk 0.3.38, NAudio, System.Speech (só para gerar áudio de teste), WinForms (só pelo `NotifyIcon`), xUnit.

**Spec:** `docs/superpowers/specs/2026-07-29-ronvoice-speech-design.md` — leia antes de começar. Se algo aqui divergir dela, a spec vence e o conflito deve ser levantado, não resolvido em silêncio.

## Global Constraints

- **Invoque o SDK pelo caminho absoluto.** O `dotnet` do PATH é runtime 7 sem SDK e resolve para `C:\Program Files\dotnet\dotnet.exe`, que não acha SDK nenhum. Use sempre:

  ```powershell
  $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
  & $dotnet build
  & $dotnet test
  ```

  Onde este plano escreve `dotnet ...`, leia `& $dotnet ...`.

- **PowerShell 5.1**: não existe `&&` nem `||`; use `;` ou chamadas separadas.
- **`RonVoice.Core` não referencia WPF, WinForms nem `System.Windows`.** Só o `RonVoice.Tray` toca WinForms, e apenas pelo `NotifyIcon`.
- **Nada das etapas 1–4 muda.** `PhraseMatcher`, `PhraseIndex`, `TextNormalizer`, `CommandResolver`, `KeyCatalog`, `KeybindReader`, `SendInputSender` e `ForegroundGuard` ficam intactos. Se você achar que precisa alterá-los, pare e pergunte.
- **A suíte tem 145 testes hoje e deve continuar verde**, além dos que você acrescentar.
- **Build warning-clean**: `Directory.Build.props` liga `TreatWarningsAsErrors`. Um `using` não usado é erro de build.
- **Não ligue `InvariantGlobalization`** — em modo invariante `string.Normalize(FormD)` vira no-op e o `TextNormalizer` para de dobrar acentos, levando junto o modo português.
- **Áudio é sempre 16 kHz, mono, PCM 16 bits.** É o que o modelo espera e evita reamostragem.
- **O token `[unk]` é obrigatório na gramática.** Sem ele o Vosk força qualquer áudio para dentro da gramática e ruído vira comando.
- **Modelos não são versionados.** `data/models/` já está no `.gitignore`.
- Código, identificadores e commits em **inglês**. Documentação em português.

**API real do Vosk 0.3.38**, confirmada por reflexão sobre o assembly — não invente assinaturas:

```csharp
new Vosk.Model(string modelPath)
Vosk.Vosk.SetLogLevel(int level)
new Vosk.VoskRecognizer(Model model, float sampleRate, string grammarJson)
  recognizer.SetWords(true)                       // confiança por palavra no resultado
  bool end = recognizer.AcceptWaveform(byte[] data, int len)
  string json = recognizer.Result()               // quando AcceptWaveform devolve true
  string json = recognizer.PartialResult()
  string json = recognizer.FinalResult()
  recognizer.Reset()
```

**Não existe `SetGrammar`.** A gramática é imutável no reconhecedor.

---

## File Structure

```
RonVoice.Core/
  Speech/
    RecognitionResult.cs    records RecognitionResult e WordConfidence
    ISpeechEngine.cs        Start/Stop/Reset, evento OnRecognized
    GrammarBuilder.cs       CommandMap + idioma -> JSON da gramática
    ModelLocator.cs         acha e valida a pasta do modelo
    VoskSpeechEngine.cs     implementação
    VoskResultParser.cs     JSON do Vosk -> RecognitionResult
  Audio/
    IAudioCapture.cs
    WavFileCapture.cs       lê de arquivo; usado nos testes
    WasapiCapture.cs        NAudio, microfone real
  Pipeline/
    ListenGate.cs           foco do jogo + mute
    PipelineEvents.cs       Heard, Matched, Rejected, Sent
    VoicePipeline.cs        orquestra

RonVoice.Cli/Commands/
  SynthCommand.cs           gera WAVs de teste
  ListenCommand.cs          roda o pipeline (microfone ou --from-wav)
  RecordCommand.cs          grava corpus real

RonVoice.Tray/              projeto novo, WinForms
  TrayApp.cs  TrayIcon.cs  GlobalHotkey.cs

tools/fetch-models.ps1      baixa os modelos
data/models/                não versionado
RonVoice.Tests/
  GrammarBuilderTests.cs  ListenGateTests.cs  VoskResultParserTests.cs
  VoicePipelineTests.cs   SpeechIntegrationTests.cs
  audio/                  WAVs gerados, não versionados
```

---

## Task 1: Modelos, `ModelLocator` e prova de vida do Vosk

Sem isto nada mais é verificável. A tarefa termina quando o Vosk carrega o modelo nesta máquina e reconhece alguma coisa.

**Files:**
- Create: `tools/fetch-models.ps1`, `RonVoice.Core/Speech/ModelLocator.cs`
- Modify: `RonVoice.Core/RonVoice.Core.csproj` (pacote Vosk)
- Test: `RonVoice.Tests/ModelLocatorTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `ModelLocator.Find(string language, string? overridePath = null)` → `string` (caminho da pasta) ou lança `ModelNotFoundException`; `ModelLocator.LanguageOf(string modelDir)` → `string?`.

- [ ] **Step 1: Adicionar o pacote Vosk**

```
& $dotnet add RonVoice.Core package Vosk --version 0.3.38
```

- [ ] **Step 2: Escrever o script de download**

Create `tools/fetch-models.ps1`:

```powershell
param([string]$Dest = "$PSScriptRoot\..\data\models")

$models = @{
  "en" = @{ Name = "vosk-model-small-en-us-0.15"; Size = 41205931 }
  "pt" = @{ Name = "vosk-model-small-pt-0.3";     Size = 32453112 }
}

New-Item -ItemType Directory -Force -Path $Dest | Out-Null

foreach ($lang in $models.Keys) {
  $m = $models[$lang]
  $target = Join-Path $Dest $m.Name
  if (Test-Path $target) { Write-Host "$($m.Name): ja existe"; continue }

  $zip = Join-Path $env:TEMP "$($m.Name).zip"
  $url = "https://alphacephei.com/vosk/models/$($m.Name).zip"
  Write-Host "baixando $($m.Name) ($([math]::Round($m.Size/1MB,1)) MB)..."
  curl.exe -sSL --fail --max-time 900 -o $zip $url
  if ($LASTEXITCODE -ne 0) { throw "falha ao baixar $url" }

  Expand-Archive -Path $zip -DestinationPath $Dest -Force
  Remove-Item $zip -Force
  Write-Host "$($m.Name): ok"
}

Get-ChildItem $Dest -Directory | Select-Object Name
```

- [ ] **Step 3: Baixar os modelos**

```
powershell -ExecutionPolicy Bypass -File tools\fetch-models.ps1
```

Esperado: duas pastas em `data/models/`. Cada uma contém `am/`, `conf/`, `graph/` e `README`.

- [ ] **Step 4: Escrever os testes que falham**

Create `RonVoice.Tests/ModelLocatorTests.cs`:

```csharp
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
        Assert.True(Directory.Exists(Path.Combine(path, "am")), "pasta am/ ausente");
        Assert.True(Directory.Exists(Path.Combine(path, "conf")), "pasta conf/ ausente");
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
```

- [ ] **Step 5: Rodar e ver falhar**

```
& $dotnet test --filter ModelLocatorTests
```

Esperado: erro de compilação — `ModelLocator` não existe.

- [ ] **Step 6: Implementar**

Create `RonVoice.Core/Speech/ModelLocator.cs`:

```csharp
namespace RonVoice.Core.Speech;

public sealed class ModelNotFoundException(string message) : Exception(message);

/// <summary>
/// Acha a pasta do modelo Vosk. Os modelos não são versionados: são baixados
/// por tools/fetch-models.ps1 para data/models/.
/// </summary>
public static class ModelLocator
{
    /// <summary>Prefixo da pasta de cada idioma suportado.</summary>
    static readonly Dictionary<string, string> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "vosk-model-small-en",
        ["pt"] = "vosk-model-small-pt",
    };

    public static string Find(string language, string? modelsDir = null)
    {
        if (!Prefixes.TryGetValue(language, out var prefix))
            throw new ModelNotFoundException(
                $"idioma sem modelo configurado: {language} (suportados: {string.Join(", ", Prefixes.Keys)})");

        var dir = modelsDir ?? Path.Combine(AppContext.BaseDirectory, "data", "models");
        if (!Directory.Exists(dir))
            throw new ModelNotFoundException(
                $"pasta de modelos não encontrada: {dir}. Rode tools/fetch-models.ps1.");

        var hit = Directory.GetDirectories(dir)
            .FirstOrDefault(d => Path.GetFileName(d)
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (hit is null)
            throw new ModelNotFoundException(
                $"nenhum modelo de '{language}' em {dir} (esperado algo começando com '{prefix}'). "
                + "Rode tools/fetch-models.ps1.");

        // Um modelo Vosk válido tem am/ e conf/. Sem isso a lib nativa aborta o processo.
        foreach (var required in new[] { "am", "conf" })
            if (!Directory.Exists(Path.Combine(hit, required)))
                throw new ModelNotFoundException(
                    $"modelo em {hit} parece incompleto: falta a pasta {required}/");

        return hit;
    }

    /// <summary>Idioma inferido do nome da pasta, ou null se não reconhecido.</summary>
    public static string? LanguageOf(string modelDir)
    {
        var name = Path.GetFileName(modelDir.TrimEnd(Path.DirectorySeparatorChar));
        foreach (var (lang, prefix) in Prefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return lang;
        return null;
    }
}
```

- [ ] **Step 7: Rodar e ver passar**

```
& $dotnet test --filter ModelLocatorTests
```

- [ ] **Step 8: Prova de vida do Vosk**

Este passo não é um teste automatizado — é a verificação de que a biblioteca nativa carrega nesta máquina. Crie um projeto descartável fora do repositório:

```powershell
$probe = "$env:TEMP\voskprobe"
& $dotnet new console -o $probe
& $dotnet add $probe package Vosk --version 0.3.38
```

`Program.cs` do probe:

```csharp
using Vosk;
Vosk.Vosk.SetLogLevel(-1);
var model = new Model(args[0]);
var rec = new VoskRecognizer(model, 16000.0f, "[\"stack up\", \"[unk]\"]");
rec.SetWords(true);
Console.WriteLine("modelo e reconhecedor criados sem erro");
Console.WriteLine(rec.FinalResult());
```

```powershell
& $dotnet run --project $probe -- "<caminho absoluto do modelo en>"
```

Esperado: as duas linhas, sem exceção e sem o processo morrer. Se a DLL nativa não carregar, o erro aparece aqui e **não** adianta seguir — anote no relatório e pare.

Apague o probe depois: `Remove-Item $probe -Recurse -Force`.

- [ ] **Step 9: Commit**

```bash
git add tools/fetch-models.ps1 RonVoice.Core RonVoice.Tests/ModelLocatorTests.cs
git commit -m "feat: locate Vosk models and add the model fetch script"
```

---

## Task 2: `GrammarBuilder`

**Files:**
- Create: `RonVoice.Core/Speech/GrammarBuilder.cs`
- Test: `RonVoice.Tests/GrammarBuilderTests.cs`

**Interfaces:**
- Consumes: `CommandMap.Load(path)` com `Orders`, `Elements`, `Queue`.
- Produces: `GrammarBuilder.Build(CommandMap map, string language)` → `string` (JSON), e `GrammarBuilder.Phrases(CommandMap map, string language)` → `IReadOnlyList<string>`.

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/GrammarBuilderTests.cs`:

```csharp
using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class GrammarBuilderTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static string[] Parse(string json) =>
        JsonSerializer.Deserialize<string[]>(json)!;

    [Fact]
    public void ProducesValidJsonArray() =>
        Assert.NotEmpty(Parse(GrammarBuilder.Build(Map(), "en")));

    [Theory]
    [InlineData("en", 399)]
    [InlineData("pt", 371)]
    public void ContainsEveryOrderPhraseOfTheLanguage(string lang, int expected)
    {
        var grammar = Parse(GrammarBuilder.Build(Map(), lang));
        var orderPhrases = Map().Orders.Values.SelectMany(o => o.Phrases[lang]).ToList();
        Assert.Equal(expected, orderPhrases.Count);
        foreach (var p in orderPhrases)
            Assert.Contains(p.ToLowerInvariant(), grammar);
    }

    [Fact]
    public void ContainsElementAndQueueAliases()
    {
        var grammar = Parse(GrammarBuilder.Build(Map(), "en"));
        Assert.Contains("red team", grammar);
        Assert.Contains("prep", grammar);
    }

    [Fact]
    public void AlwaysContainsTheUnknownToken()
    {
        // Sem [unk] o Vosk força qualquer áudio para dentro da gramática:
        // ruído vira comando porque foi a opção menos improvável.
        Assert.Contains("[unk]", Parse(GrammarBuilder.Build(Map(), "en")));
        Assert.Contains("[unk]", Parse(GrammarBuilder.Build(Map(), "pt")));
    }

    [Fact]
    public void HasNoDuplicates()
    {
        var grammar = Parse(GrammarBuilder.Build(Map(), "en"));
        Assert.Equal(grammar.Length, grammar.Distinct().Count());
    }

    [Fact]
    public void EveryEntryIsLowercaseAndFreeOfPunctuation()
    {
        foreach (var entry in Parse(GrammarBuilder.Build(Map(), "en")))
        {
            if (entry == "[unk]") continue;
            Assert.Equal(entry.ToLowerInvariant(), entry);
            Assert.DoesNotContain(',', entry);
            Assert.DoesNotContain('!', entry);
        }
    }

    [Fact]
    public void TheTwoLanguagesDiffer() =>
        Assert.NotEqual(GrammarBuilder.Build(Map(), "en"), GrammarBuilder.Build(Map(), "pt"));

    [Fact]
    public void UnknownLanguageThrows() =>
        Assert.Throws<ArgumentException>(() => GrammarBuilder.Build(Map(), "de"));
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter GrammarBuilderTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.Core/Speech/GrammarBuilder.cs`:

```csharp
using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Speech;

/// <summary>
/// Monta a gramática fechada que o Vosk recebe. Lista plana composicional: frases
/// de ordem, aliases de elemento e de fila como entradas independentes. O
/// PhraseMatcher já sabe extrair elemento e fila de qualquer arranjo de palavras,
/// então não geramos o produto cartesiano.
/// </summary>
public static class GrammarBuilder
{
    /// <summary>
    /// Obrigatório. Sem ele o Vosk força qualquer áudio para dentro da gramática
    /// e ruído vira comando — o app passa a mandar ordens sozinho.
    /// </summary>
    public const string UnknownToken = "[unk]";

    public static IReadOnlyList<string> Phrases(CommandMap map, string language)
    {
        if (language is not ("en" or "pt"))
            throw new ArgumentException($"idioma não suportado: {language}", nameof(language));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        void Add(string raw)
        {
            // O reconhecedor devolve minúsculas sem pontuação; normalizamos a
            // gramática do mesmo jeito para o matcher receber o que espera.
            var normalized = string.Join(' ', TextNormalizer.Tokenize(raw));
            if (normalized.Length > 0 && seen.Add(normalized)) result.Add(normalized);
        }

        foreach (var order in map.Orders.Values)
            if (order.Phrases.TryGetValue(language, out var phrases))
                foreach (var p in phrases) Add(p);

        foreach (var element in map.Elements.Values)
            if (element.Aliases.TryGetValue(language, out var aliases))
                foreach (var a in aliases) Add(a);

        if (map.Queue.Aliases.TryGetValue(language, out var queueAliases))
            foreach (var a in queueAliases) Add(a);

        result.Add(UnknownToken);
        return result;
    }

    public static string Build(CommandMap map, string language) =>
        JsonSerializer.Serialize(Phrases(map, language));
}
```

- [ ] **Step 4: Rodar e ver passar**

```
& $dotnet test --filter GrammarBuilderTests
```

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Speech/GrammarBuilder.cs RonVoice.Tests/GrammarBuilderTests.cs
git commit -m "feat: build the closed Vosk grammar from the command map"
```

---

## Task 3: `ronvoice synth` — gerar áudio de teste

Sem isto, medir qualquer coisa depende do autor falar ao microfone. Com isto, a etapa inteira fica testável de forma determinística.

**Files:**
- Create: `RonVoice.Cli/Commands/SynthCommand.cs`
- Modify: `RonVoice.Cli/RonVoice.Cli.csproj`, `RonVoice.Cli/Program.cs`

**Interfaces:**
- Consumes: `Cli.Option`/`Cli.Flag` de `RonVoice.Cli/Commands/TestCommand.cs`.
- Produces: `ronvoice synth --out <pasta> [--lang en|pt] [--phrase "<texto>"] [--limit N]`, gerando WAVs 16 kHz mono 16 bits, um por frase, nomeados de forma determinística.

- [ ] **Step 1: Adicionar o pacote**

```
& $dotnet add RonVoice.Cli package System.Speech --version 9.0.0
```

Se a versão 9.0.0 não existir, use a última estável listada por
`curl.exe -s https://api.nuget.org/v3-flatcontainer/system.speech/index.json`
e registre no relatório qual você usou.

- [ ] **Step 2: Implementar**

Create `RonVoice.Cli/Commands/SynthCommand.cs`:

```csharp
using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using RonVoice.Core.Commands;
using RonVoice.Core.Speech;

namespace RonVoice.Cli.Commands;

/// <summary>
/// Gera WAVs a partir das frases do mapa usando a síntese do Windows. Serve para
/// exercitar a gramática de forma determinística, sem depender de alguém falar.
/// Voz sintética é limpa demais para medir acerto real — isso é o corpus gravado.
/// </summary>
public static class SynthCommand
{
    public static int Run(string[] args)
    {
        var outDir = Cli.Option(args, "--out")
                     ?? Path.Combine(AppContext.BaseDirectory, "audio");
        var lang = Cli.Option(args, "--lang") ?? "en";
        var single = Cli.Option(args, "--phrase");
        var limitText = Cli.Option(args, "--limit");

        if (limitText is not null
            && !int.TryParse(limitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            Console.Error.WriteLine($"--limit inválido: '{limitText}'");
            return 1;
        }
        var limit = limitText is null ? int.MaxValue : int.Parse(limitText, CultureInfo.InvariantCulture);

        Directory.CreateDirectory(outDir);

        var phrases = single is not null
            ? [single]
            : GrammarBuilder.Phrases(CommandMap.Load(Cli.MapPath), lang)
                .Where(p => p != GrammarBuilder.UnknownToken)
                .Take(limit)
                .ToList();

        using var synth = new SpeechSynthesizer();
        var voice = PickVoice(synth, lang);
        if (voice is not null) synth.SelectVoice(voice);
        Console.WriteLine($"voz: {voice ?? "(padrão do sistema)"}");

        var format = new SpeechAudioFormatInfo(
            16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

        var written = 0;
        foreach (var phrase in phrases)
        {
            var path = Path.Combine(outDir, FileNameFor(phrase) + ".wav");
            synth.SetOutputToWaveFile(path, format);
            synth.Speak(phrase);
            written++;
        }
        synth.SetOutputToNull();

        Console.WriteLine($"{written} arquivos em {outDir}");
        return 0;
    }

    /// <summary>Nome estável e seguro para o sistema de arquivos.</summary>
    public static string FileNameFor(string phrase) =>
        string.Join('_', phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    static string? PickVoice(SpeechSynthesizer synth, string lang)
    {
        var wanted = lang == "pt" ? "pt" : "en";
        foreach (var v in synth.GetInstalledVoices().Where(v => v.Enabled))
            if (v.VoiceInfo.Culture.TwoLetterISOLanguageName
                    .Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return v.VoiceInfo.Name;
        return null;
    }
}
```

- [ ] **Step 3: Ligar no despacho**

Em `RonVoice.Cli/Program.cs`, acrescente ao `switch`:

```csharp
    "synth" => SynthCommand.Run(rest),
```

E ao texto de `Help()`:

```
ronvoice synth --out <pasta> [--lang en|pt] [--limit N]   gera WAVs de teste
```

- [ ] **Step 4: Verificar à mão**

```
& $dotnet run --project RonVoice.Cli -- synth --out audio-probe --limit 3
```

Esperado: três `.wav` e a linha da voz escolhida. Confirme o formato:

```powershell
$b = [IO.File]::ReadAllBytes((Get-ChildItem audio-probe\*.wav)[0].FullName)
"canais: $([BitConverter]::ToInt16($b,22))  taxa: $([BitConverter]::ToInt32($b,24))  bits: $([BitConverter]::ToInt16($b,34))"
```

Esperado: `canais: 1  taxa: 16000  bits: 16`. Se não bater, o resto do plano não funciona — pare e ajuste o `SpeechAudioFormatInfo`.

Se **nenhuma voz** estiver instalada, `Speak` lança. Registre no relatório e pare: sem síntese, as tarefas 4 e 11 precisam ser replanejadas em cima de gravações reais.

- [ ] **Step 5: Limpar e commitar**

```bash
rm -rf audio-probe
git add RonVoice.Cli
git commit -m "feat: synthesize 16 kHz test audio from the command map"
```

---

## Task 4: Medir se o Vosk compõe entre entradas da gramática

**Esta tarefa decide o desenho da gramática. Não avance sem responder.** É a pendência 1 da spec.

**Files:**
- Create: `RonVoice.Tests/GrammarCompositionTests.cs`

**Interfaces:**
- Consumes: `ModelLocator.Find`, `GrammarBuilder.Build`, `SynthCommand.FileNameFor`, o pacote Vosk.
- Produces: a resposta registrada no relatório, e um teste que documenta o comportamento observado.

- [ ] **Step 1: Gerar o áudio da medição**

Três enunciados compostos, que só existem se o reconhecedor combinar entradas:

```
& $dotnet run --project RonVoice.Cli -- synth --out RonVoice.Tests/audio --phrase "red team open with flashbang"
& $dotnet run --project RonVoice.Cli -- synth --out RonVoice.Tests/audio --phrase "blue team prep stack left"
& $dotnet run --project RonVoice.Cli -- synth --out RonVoice.Tests/audio --phrase "stack left"
```

O terceiro é o controle: é uma entrada literal da gramática e deve ser reconhecido sob qualquer hipótese.

- [ ] **Step 2: Escrever o teste de medição**

Create `RonVoice.Tests/GrammarCompositionTests.cs`:

```csharp
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

    static string Recognize(string wavPath, string grammarJson)
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

    [Fact]
    public void ALiteralGrammarEntryIsRecognized()
    {
        var grammar = GrammarBuilder.Build(CommandMap.Load(CommandMapTests.MapPath), "en");
        var wav = Path.Combine(AudioDir, "stack_left.wav");
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        Assert.Equal("stack left", Recognize(wav, grammar));
    }

    [Theory]
    [InlineData("red_team_open_with_flashbang.wav", "red team", "open with flashbang")]
    [InlineData("blue_team_prep_stack_left.wav", "blue team", "stack left")]
    public void ComposesAcrossGrammarEntries(string wavName, string firstPart, string lastPart)
    {
        var grammar = GrammarBuilder.Build(CommandMap.Load(CommandMapTests.MapPath), "en");
        var wav = Path.Combine(AudioDir, wavName);
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        var heard = Recognize(wav, grammar);

        // Se compõe, o texto contém as duas partes. Se casa entrada inteira,
        // virá só uma delas — e o produto cartesiano passa a ser necessário.
        Assert.Contains(firstPart, heard);
        Assert.Contains(lastPart, heard);
    }
}
```

- [ ] **Step 3: Rodar a medição**

```
& $dotnet test --filter GrammarCompositionTests
```

- [ ] **Step 4: Registrar o resultado e decidir**

**Se `ComposesAcrossGrammarEntries` passar:** a hipótese da spec está confirmada. A lista plana fica. Registre no relatório o texto exato que o reconhecedor devolveu para cada arquivo e siga para a Task 5.

**Se falhar:** o Vosk não compõe. **Pare e reporte** — não improvise o produto cartesiano por conta própria. O plano precisa ser revisado, porque o fallback muda a Task 2, acrescenta uma tarefa de geração e desloca a lógica de composição do matcher para o gerador. Inclua no relatório, para cada arquivo, o texto devolvido.

- [ ] **Step 5: Ignorar o áudio gerado no git**

Acrescente ao `.gitignore`:

```
# audio de teste, gerado por `ronvoice synth`
RonVoice.Tests/audio/
```

- [ ] **Step 6: Commit**

```bash
git add .gitignore RonVoice.Tests/GrammarCompositionTests.cs
git commit -m "test: measure whether Vosk composes across grammar entries"
```

---

## Task 5: `VoskResultParser`

**Files:**
- Create: `RonVoice.Core/Speech/RecognitionResult.cs`, `RonVoice.Core/Speech/VoskResultParser.cs`
- Test: `RonVoice.Tests/VoskResultParserTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `record RecognitionResult(string Text, IReadOnlyList<WordConfidence> Words, bool IsFinal)`
  - `record WordConfidence(string Word, double Confidence)`
  - `VoskResultParser.Parse(string json, bool isFinal)` → `RecognitionResult`
  - `RecognitionResult.AverageConfidence` → `double` (1.0 quando não há palavras)
  - `RecognitionResult.ContainsUnknown` → `bool`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/VoskResultParserTests.cs`:

```csharp
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class VoskResultParserTests
{
    const string WithWords = """
        {"result":[
          {"conf":0.98,"end":1.02,"start":0.75,"word":"stack"},
          {"conf":0.86,"end":1.31,"start":1.02,"word":"left"}],
         "text":"stack left"}
        """;

    [Fact]
    public void ReadsTextAndWords()
    {
        var r = VoskResultParser.Parse(WithWords, isFinal: true);
        Assert.Equal("stack left", r.Text);
        Assert.True(r.IsFinal);
        Assert.Collection(r.Words,
            w => Assert.Equal(new WordConfidence("stack", 0.98), w),
            w => Assert.Equal(new WordConfidence("left", 0.86), w));
    }

    [Fact]
    public void AveragesConfidence() =>
        Assert.Equal(0.92, VoskResultParser.Parse(WithWords, true).AverageConfidence, 3);

    [Fact]
    public void HandlesPartialResults()
    {
        var r = VoskResultParser.Parse("""{"partial":"stack"}""", isFinal: false);
        Assert.Equal("stack", r.Text);
        Assert.False(r.IsFinal);
        Assert.Empty(r.Words);
    }

    [Fact]
    public void HandlesEmptyResult()
    {
        var r = VoskResultParser.Parse("""{"text":""}""", isFinal: true);
        Assert.Equal("", r.Text);
        Assert.Empty(r.Words);
    }

    [Fact]
    public void ConfidenceOfAnEmptyResultIsOneSoItIsNotRejectedByTheGate() =>
        Assert.Equal(1.0, VoskResultParser.Parse("""{"text":""}""", true).AverageConfidence);

    [Fact]
    public void DetectsTheUnknownToken()
    {
        Assert.True(VoskResultParser.Parse("""{"text":"[unk] left"}""", true).ContainsUnknown);
        Assert.False(VoskResultParser.Parse(WithWords, true).ContainsUnknown);
    }

    [Fact]
    public void MalformedJsonYieldsAnEmptyResultInsteadOfThrowing()
    {
        // O que vem da lib nativa não é nosso; um resultado vazio é descartado
        // pelo pipeline, e derrubar o reconhecimento por causa disso seria pior.
        var r = VoskResultParser.Parse("nao e json", isFinal: true);
        Assert.Equal("", r.Text);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter VoskResultParserTests
```

- [ ] **Step 3: Implementar os records**

Create `RonVoice.Core/Speech/RecognitionResult.cs`:

```csharp
namespace RonVoice.Core.Speech;

public sealed record WordConfidence(string Word, double Confidence);

public sealed record RecognitionResult(
    string Text,
    IReadOnlyList<WordConfidence> Words,
    bool IsFinal)
{
    /// <summary>Média simples. 1.0 quando não há palavras, para não rejeitar vazio.</summary>
    public double AverageConfidence =>
        Words.Count == 0 ? 1.0 : Words.Average(w => w.Confidence);

    /// <summary>
    /// O token [unk] é como o Vosk diz "isto está fora da gramática". Resultado
    /// que o contenha é descartado sem exceção.
    /// </summary>
    public bool ContainsUnknown =>
        Text.Contains(GrammarBuilder.UnknownToken, StringComparison.Ordinal);

    public static RecognitionResult Empty(bool isFinal) => new("", [], isFinal);
}
```

- [ ] **Step 4: Implementar o parser**

Create `RonVoice.Core/Speech/VoskResultParser.cs`:

```csharp
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
```

- [ ] **Step 5: Rodar e ver passar**

```
& $dotnet test --filter VoskResultParserTests
```

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Speech RonVoice.Tests/VoskResultParserTests.cs
git commit -m "feat: parse Vosk result JSON into a typed recognition result"
```

---

## Task 6: `ListenGate`

**Files:**
- Create: `RonVoice.Core/Pipeline/ListenGate.cs`
- Test: `RonVoice.Tests/ListenGateTests.cs`

**Interfaces:**
- Consumes: nada — os predicados são injetados.
- Produces: `ListenGate(Func<bool> isGameForeground, Func<bool> isMuted)` com `ShouldProcess()` → `bool`, `Muted` → `bool` (get/set), `Toggle()` → `bool`, e evento `StateChanged` publicando `ListenState`.
  `enum ListenState { Listening, Idle, Muted }`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/ListenGateTests.cs`:

```csharp
using RonVoice.Core.Pipeline;

namespace RonVoice.Tests;

public class ListenGateTests
{
    [Theory]
    [InlineData(true, false, true)]    // jogo em foco, não mudo -> processa
    [InlineData(false, false, false)]  // jogo fora de foco -> não
    [InlineData(true, true, false)]    // mudo -> não
    [InlineData(false, true, false)]
    public void ProcessesOnlyWhenFocusedAndUnmuted(bool focused, bool muted, bool expected) =>
        Assert.Equal(expected, new ListenGate(() => focused, () => muted).ShouldProcess());

    [Theory]
    [InlineData(true, false, ListenState.Listening)]
    [InlineData(false, false, ListenState.Idle)]
    [InlineData(true, true, ListenState.Muted)]
    [InlineData(false, true, ListenState.Muted)]
    public void ReportsTheStateTheTrayShows(bool focused, bool muted, ListenState expected) =>
        Assert.Equal(expected, new ListenGate(() => focused, () => muted).State);

    [Fact]
    public void ToggleFlipsMuteAndReturnsTheNewValue()
    {
        var gate = new ListenGate(() => true, null);
        Assert.True(gate.Toggle());
        Assert.True(gate.Muted);
        Assert.False(gate.Toggle());
    }

    [Fact]
    public void RaisesStateChangedOnlyWhenTheStateActuallyChanges()
    {
        var focused = true;
        var gate = new ListenGate(() => focused, null);
        var states = new List<ListenState>();
        gate.StateChanged += s => states.Add(s);

        gate.Poll();                       // Listening -> sem mudança, nada
        focused = false; gate.Poll();      // -> Idle
        gate.Poll();                       // sem mudança
        focused = true; gate.Poll();       // -> Listening

        Assert.Equal([ListenState.Idle, ListenState.Listening], states);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter ListenGateTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.Core/Pipeline/ListenGate.cs`:

```csharp
namespace RonVoice.Core.Pipeline;

public enum ListenState { Listening, Idle, Muted }

/// <summary>
/// Responde "devo processar este áudio agora?". Existe como classe própria porque
/// o microfone fica sempre ligado: esta é a única mitigação contra conversa virar
/// ordem, e precisa ser testável sem jogo e sem microfone.
/// </summary>
public sealed class ListenGate
{
    readonly Func<bool> _isGameForeground;
    readonly Func<bool>? _externalMute;
    bool _muted;
    ListenState _last;

    public ListenGate(Func<bool> isGameForeground, Func<bool>? isMuted = null)
    {
        _isGameForeground = isGameForeground;
        _externalMute = isMuted;
        _last = State;
    }

    public event Action<ListenState>? StateChanged;

    public bool Muted
    {
        get => _externalMute?.Invoke() ?? _muted;
        set { _muted = value; Poll(); }
    }

    public ListenState State =>
        Muted ? ListenState.Muted
        : _isGameForeground() ? ListenState.Listening
        : ListenState.Idle;

    public bool ShouldProcess() => State == ListenState.Listening;

    public bool Toggle() { Muted = !Muted; return Muted; }

    /// <summary>Reavalia e publica StateChanged se mudou. Chamado pelo pipeline.</summary>
    public void Poll()
    {
        var now = State;
        if (now == _last) return;
        _last = now;
        StateChanged?.Invoke(now);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

```
& $dotnet test --filter ListenGateTests
```

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Pipeline/ListenGate.cs RonVoice.Tests/ListenGateTests.cs
git commit -m "feat: gate listening on game focus and mute"
```

---

## Task 7: `IAudioCapture` e `WavFileCapture`

**Files:**
- Create: `RonVoice.Core/Audio/IAudioCapture.cs`, `RonVoice.Core/Audio/WavFileCapture.cs`
- Test: `RonVoice.Tests/WavFileCaptureTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `interface IAudioCapture : IDisposable` com `event Action<ReadOnlyMemory<byte>>? OnAudio`, `event Action? OnStopped`, `void Start()`, `void Stop()`
  - `WavFileCapture(string path, int chunkBytes = 4000)`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/WavFileCaptureTests.cs`:

```csharp
using RonVoice.Core.Audio;

namespace RonVoice.Tests;

public class WavFileCaptureTests
{
    static string MakeWav(int samples)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-{Guid.NewGuid():N}.wav");
        var data = new byte[samples * 2];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data);
        return path;
    }

    [Fact]
    public void EmitsEveryByteOfAudioSkippingTheHeader()
    {
        var path = MakeWav(1000);
        var got = new List<byte>();
        using (var capture = new WavFileCapture(path, chunkBytes: 256))
        {
            capture.OnAudio += chunk => got.AddRange(chunk.ToArray());
            capture.Start();
        }
        Assert.Equal(2000, got.Count);
        Assert.Equal((byte)0, got[0]);
        File.Delete(path);
    }

    [Fact]
    public void RaisesStoppedWhenTheFileEnds()
    {
        var path = MakeWav(100);
        var stopped = false;
        using (var capture = new WavFileCapture(path))
        {
            capture.OnStopped += () => stopped = true;
            capture.Start();
        }
        Assert.True(stopped);
        File.Delete(path);
    }

    [Fact]
    public void MissingFileThrowsNamingIt()
    {
        var ex = Assert.Throws<FileNotFoundException>(
            () => new WavFileCapture("c:\\nao\\existe\\x.wav").Start());
        Assert.Contains("x.wav", ex.Message);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter WavFileCaptureTests
```

- [ ] **Step 3: Implementar a interface**

Create `RonVoice.Core/Audio/IAudioCapture.cs`:

```csharp
namespace RonVoice.Core.Audio;

/// <summary>
/// Fonte de áudio a 16 kHz, mono, PCM 16 bits. Duas implementações: o microfone
/// real e um leitor de arquivo, que é o que torna a etapa testável sem falar.
/// </summary>
public interface IAudioCapture : IDisposable
{
    event Action<ReadOnlyMemory<byte>>? OnAudio;
    event Action? OnStopped;
    void Start();
    void Stop();
}
```

- [ ] **Step 4: Implementar o leitor de arquivo**

Create `RonVoice.Core/Audio/WavFileCapture.cs`:

```csharp
namespace RonVoice.Core.Audio;

/// <summary>
/// Toca um WAV como se fosse o microfone. Síncrono de propósito: Start() só
/// retorna quando o arquivo acabou, o que torna os testes determinísticos.
/// </summary>
public sealed class WavFileCapture(string path, int chunkBytes = 4000) : IAudioCapture
{
    public event Action<ReadOnlyMemory<byte>>? OnAudio;
    public event Action? OnStopped;

    bool _stop;

    public void Start()
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"WAV não encontrado: {path}", path);

        using var fs = File.OpenRead(path);
        SkipToData(fs);

        var buffer = new byte[chunkBytes];
        int read;
        while (!_stop && (read = fs.Read(buffer, 0, buffer.Length)) > 0)
            OnAudio?.Invoke(new ReadOnlyMemory<byte>(buffer, 0, read));

        OnStopped?.Invoke();
    }

    public void Stop() => _stop = true;
    public void Dispose() => Stop();

    /// <summary>
    /// Percorre os chunks RIFF até 'data'. Não assume 44 bytes: arquivos gerados
    /// por síntese costumam trazer chunks extras antes do áudio.
    /// </summary>
    static void SkipToData(Stream fs)
    {
        using var r = new BinaryReader(fs, System.Text.Encoding.ASCII, leaveOpen: true);
        if (new string(r.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("não é um arquivo RIFF");
        r.ReadInt32();
        if (new string(r.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("não é um arquivo WAVE");

        while (fs.Position < fs.Length)
        {
            var id = new string(r.ReadChars(4));
            var size = r.ReadInt32();
            if (id == "data") return;
            fs.Seek(size, SeekOrigin.Current);
        }
        throw new InvalidDataException("chunk 'data' não encontrado no WAV");
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

```
& $dotnet test --filter WavFileCaptureTests
```

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Audio RonVoice.Tests/WavFileCaptureTests.cs
git commit -m "feat: add the audio capture interface and a WAV-driven source"
```

---

## Task 8: `VoskSpeechEngine`

**Files:**
- Create: `RonVoice.Core/Speech/ISpeechEngine.cs`, `RonVoice.Core/Speech/VoskSpeechEngine.cs`
- Test: `RonVoice.Tests/VoskSpeechEngineTests.cs`

**Interfaces:**
- Consumes: `ModelLocator.Find`, `GrammarBuilder.Build`, `VoskResultParser.Parse`, o pacote Vosk.
- Produces:
  - `interface ISpeechEngine : IDisposable` com `event Action<RecognitionResult>? OnRecognized`, `void Feed(ReadOnlyMemory<byte> audio)`, `void Flush()`, `void Reset()`
  - `VoskSpeechEngine(string modelPath, string grammarJson, float sampleRate = 16000f)`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/VoskSpeechEngineTests.cs`:

```csharp
using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class VoskSpeechEngineTests
{
    static string ModelsDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "models");
    static string AudioDir => Path.Combine(AppContext.BaseDirectory, "audio");

    static VoskSpeechEngine Engine() => new(
        ModelLocator.Find("en", ModelsDir),
        GrammarBuilder.Build(CommandMap.Load(CommandMapTests.MapPath), "en"));

    static List<RecognitionResult> Run(string wavName)
    {
        var wav = Path.Combine(AudioDir, wavName);
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        var finals = new List<RecognitionResult>();
        using var engine = Engine();
        engine.OnRecognized += r => { if (r.IsFinal) finals.Add(r); };

        using var capture = new WavFileCapture(wav);
        capture.OnAudio += chunk => engine.Feed(chunk);
        capture.OnStopped += () => engine.Flush();
        capture.Start();
        return finals;
    }

    [Fact]
    public void RecognizesAPhraseFromTheGrammar()
    {
        var finals = Run("stack_left.wav");
        Assert.Contains(finals, r => r.Text.Contains("stack left", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsPerWordConfidence()
    {
        var withText = Run("stack_left.wav").First(r => r.Text.Length > 0);
        Assert.NotEmpty(withText.Words);
        Assert.All(withText.Words, w => Assert.InRange(w.Confidence, 0.0, 1.0));
    }

    [Fact]
    public void ResetDiscardsAudioAlreadyFed()
    {
        var wav = Path.Combine(AudioDir, "stack_left.wav");
        Assert.True(File.Exists(wav));

        using var engine = Engine();
        var finals = new List<RecognitionResult>();
        engine.OnRecognized += r => { if (r.IsFinal) finals.Add(r); };

        using var capture = new WavFileCapture(wav);
        capture.OnAudio += chunk => engine.Feed(chunk);
        capture.Start();

        engine.Reset();     // é o que acontece quando o portão fecha
        engine.Flush();

        Assert.All(finals, r => Assert.Equal("", r.Text));
    }

    [Fact]
    public void MissingModelThrowsBeforeTouchingTheNativeLibrary()
    {
        var ex = Assert.Throws<ModelNotFoundException>(
            () => ModelLocator.Find("en", Path.Combine(Path.GetTempPath(), "sem-modelos-ronvoice")));
        Assert.Contains("sem-modelos-ronvoice", ex.Message);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter VoskSpeechEngineTests
```

- [ ] **Step 3: Implementar a interface**

Create `RonVoice.Core/Speech/ISpeechEngine.cs`:

```csharp
namespace RonVoice.Core.Speech;

public interface ISpeechEngine : IDisposable
{
    event Action<RecognitionResult>? OnRecognized;

    /// <summary>Entrega áudio PCM 16 bits mono a 16 kHz.</summary>
    void Feed(ReadOnlyMemory<byte> audio);

    /// <summary>Fecha o enunciado corrente e publica o resultado final.</summary>
    void Flush();

    /// <summary>Descarta o enunciado em curso sem publicar nada.</summary>
    void Reset();
}
```

- [ ] **Step 4: Implementar o motor**

Create `RonVoice.Core/Speech/VoskSpeechEngine.cs`:

```csharp
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
    readonly object _lock = new();
    bool _disposed;

    public event Action<RecognitionResult>? OnRecognized;

    public VoskSpeechEngine(string modelPath, string grammarJson, float sampleRate = 16000f)
    {
        Vosk.Vosk.SetLogLevel(-1);          // a lib nativa é falante demais por padrão
        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, sampleRate, grammarJson);
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
```

- [ ] **Step 5: Rodar e ver passar**

```
& $dotnet test --filter VoskSpeechEngineTests
```

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Speech RonVoice.Tests/VoskSpeechEngineTests.cs
git commit -m "feat: recognize speech with Vosk under a closed grammar"
```

---

## Task 9: `VoicePipeline`

**Files:**
- Create: `RonVoice.Core/Pipeline/PipelineEvents.cs`, `RonVoice.Core/Pipeline/VoicePipeline.cs`
- Test: `RonVoice.Tests/VoicePipelineTests.cs`

**Interfaces:**
- Consumes: `ISpeechEngine`, `ListenGate`, `PhraseMatcher`, `CommandResolver`, `IInputSender`.
- Produces:
  - `VoicePipeline(ISpeechEngine engine, ListenGate gate, PhraseMatcher matcher, CommandResolver resolver, IInputSender sender)`
  - `void Push(ReadOnlyMemory<byte> audio)`, `void Flush()`, `void Start()`, `void Stop()`
  - eventos `Heard`, `Matched`, `Rejected`, `Sent`
  - `record RejectionReason` com valores `Unknown`, `LowConfidence`, `NoMatch`, `Unresolvable`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/VoicePipelineTests.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>Motor falso: emite o texto que o teste mandar, sem áudio nenhum.</summary>
sealed class FakeSpeechEngine : ISpeechEngine
{
    public event Action<RecognitionResult>? OnRecognized;
    public int Resets { get; private set; }
    public void Feed(ReadOnlyMemory<byte> audio) { }
    public void Flush() { }
    public void Reset() => Resets++;
    public void Dispose() { }

    public void Emit(string text, double confidence = 1.0) =>
        OnRecognized?.Invoke(new RecognitionResult(
            text,
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => new WordConfidence(w, confidence)).ToList(),
            IsFinal: true));
}

sealed class RecordingSender : IInputSender
{
    public List<KeySequence> Sent { get; } = [];
    public void Send(KeySequence sequence, CancellationToken ct = default) => Sent.Add(sequence);
}

public class VoicePipelineTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static (VoicePipeline Pipeline, FakeSpeechEngine Engine, RecordingSender Sender, List<object> Events)
        Build(bool focused = true, bool muted = false, double threshold = 0.0)
    {
        var engine = new FakeSpeechEngine();
        var sender = new RecordingSender();
        var map = Map();
        var binds = KeybindReader.Read(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

        var pipeline = new VoicePipeline(
            engine,
            new ListenGate(() => focused, () => muted),
            new PhraseMatcher(map, "en"),
            new CommandResolver(map, binds),
            sender,
            confidenceThreshold: threshold);

        var events = new List<object>();
        pipeline.Heard += r => events.Add(r);
        pipeline.Matched += i => events.Add(i);
        pipeline.Rejected += r => events.Add(r);
        pipeline.Sent += s => events.Add(s);
        pipeline.Start();
        return (pipeline, engine, sender, events);
    }

    [Fact]
    public void RecognizedPhraseBecomesKeystrokes()
    {
        var (_, engine, sender, _) = Build();
        engine.Emit("red team open with flashbang");

        var seq = Assert.Single(sender.Sent);
        Assert.Equal(4, seq.Steps.Count);                       // F7, MMB, 2, 2
        Assert.Equal(StepKind.Press, seq.Steps[0].Kind);
    }

    [Fact]
    public void PublishesTheStageEventsInOrder()
    {
        var (_, engine, _, events) = Build();
        engine.Emit("stack left");

        Assert.Collection(events,
            e => Assert.IsType<RecognitionResult>(e),
            e => Assert.IsType<Intent>(e),
            e => Assert.IsType<KeySequence>(e));
    }

    [Fact]
    public void UnknownTokenIsRejectedWithoutSending()
    {
        var (_, engine, sender, events) = Build();
        engine.Emit("[unk]");

        Assert.Empty(sender.Sent);
        Assert.Contains(events, e => e is Rejection { Reason: RejectionReason.Unknown });
    }

    [Fact]
    public void LowConfidenceIsRejectedWithoutSending()
    {
        var (_, engine, sender, events) = Build(threshold: 0.8);
        engine.Emit("stack left", confidence: 0.3);

        Assert.Empty(sender.Sent);
        Assert.Contains(events, e => e is Rejection { Reason: RejectionReason.LowConfidence });
    }

    [Fact]
    public void NoiseThatMatchesNothingIsRejected()
    {
        var (_, engine, sender, events) = Build();
        engine.Emit("banana pudding clock");

        Assert.Empty(sender.Sent);
        Assert.Contains(events, e => e is Rejection { Reason: RejectionReason.NoMatch });
    }

    [Fact]
    public void NothingIsProcessedWhileTheGameIsNotInFocus()
    {
        var (_, engine, sender, _) = Build(focused: false);
        engine.Emit("stack left");
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void NothingIsProcessedWhileMuted()
    {
        var (_, engine, sender, _) = Build(muted: true);
        engine.Emit("stack left");
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void ClosingTheGateResetsTheEngineSoAHalfHeardPhraseCannotCompleteLater()
    {
        var focused = true;
        var engine = new FakeSpeechEngine();
        var map = Map();
        var pipeline = new VoicePipeline(
            engine,
            new ListenGate(() => focused, () => false),
            new PhraseMatcher(map, "en"),
            new CommandResolver(map, KeybindReader.Read(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"))),
            new RecordingSender());
        pipeline.Start();

        Assert.Equal(0, engine.Resets);
        focused = false;
        pipeline.Push(new byte[16]);        // primeiro áudio com o portão fechado
        Assert.Equal(1, engine.Resets);
    }

    [Fact]
    public void TwoOrdersInARowAreBothSentInOrder()
    {
        var (_, engine, sender, _) = Build();
        engine.Emit("stack left");
        engine.Emit("hold");

        Assert.Equal(2, sender.Sent.Count);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter VoicePipelineTests
```

- [ ] **Step 3: Implementar os eventos**

Create `RonVoice.Core/Pipeline/PipelineEvents.cs`:

```csharp
using RonVoice.Core.Speech;

namespace RonVoice.Core.Pipeline;

public enum RejectionReason
{
    /// <summary>O resultado continha [unk]: fala fora da gramática.</summary>
    Unknown,
    /// <summary>Confiança média abaixo do limiar configurado.</summary>
    LowConfidence,
    /// <summary>O matcher não casou nada, ou casou de forma ambígua.</summary>
    NoMatch,
    /// <summary>Casou, mas alguma tecla não pôde ser resolvida.</summary>
    Unresolvable,
}

public sealed record Rejection(RejectionReason Reason, string Text, string? Detail = null);
```

- [ ] **Step 4: Implementar o pipeline**

Create `RonVoice.Core/Pipeline/VoicePipeline.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Core.Pipeline;

/// <summary>
/// Liga reconhecimento, casamento, resolução e envio. A UI apenas assina os
/// eventos: nada aqui pode depender dela, ou a latência passa a depender da tela.
/// </summary>
public sealed class VoicePipeline
{
    readonly ISpeechEngine _engine;
    readonly ListenGate _gate;
    readonly PhraseMatcher _matcher;
    readonly CommandResolver _resolver;
    readonly IInputSender _sender;
    readonly double _confidenceThreshold;
    bool _gateWasOpen = true;

    public event Action<RecognitionResult>? Heard;
    public event Action<Intent>? Matched;
    public event Action<Rejection>? Rejected;
    public event Action<KeySequence>? Sent;

    public VoicePipeline(
        ISpeechEngine engine,
        ListenGate gate,
        PhraseMatcher matcher,
        CommandResolver resolver,
        IInputSender sender,
        double confidenceThreshold = 0.0)
    {
        _engine = engine;
        _gate = gate;
        _matcher = matcher;
        _resolver = resolver;
        _sender = sender;
        _confidenceThreshold = confidenceThreshold;
    }

    public void Start() => _engine.OnRecognized += OnRecognized;
    public void Stop() => _engine.OnRecognized -= OnRecognized;

    /// <summary>Entrega áudio. Descarta e reseta enquanto o portão estiver fechado.</summary>
    public void Push(ReadOnlyMemory<byte> audio)
    {
        _gate.Poll();
        if (!_gate.ShouldProcess())
        {
            // Uma frase pela metade dita antes do alt-tab completaria depois e
            // viraria ordem. Reseta uma vez, na transição.
            if (_gateWasOpen) { _engine.Reset(); _gateWasOpen = false; }
            return;
        }
        _gateWasOpen = true;
        _engine.Feed(audio);
    }

    public void Flush()
    {
        if (_gate.ShouldProcess()) _engine.Flush();
    }

    void OnRecognized(RecognitionResult result)
    {
        if (!result.IsFinal) return;
        if (!_gate.ShouldProcess()) return;
        if (result.Text.Length == 0) return;

        Heard?.Invoke(result);

        if (result.ContainsUnknown)
        {
            Rejected?.Invoke(new Rejection(RejectionReason.Unknown, result.Text));
            return;
        }

        if (_confidenceThreshold > 0 && result.AverageConfidence < _confidenceThreshold)
        {
            Rejected?.Invoke(new Rejection(
                RejectionReason.LowConfidence, result.Text,
                result.AverageConfidence.ToString("0.000")));
            return;
        }

        var intent = _matcher.Match(result.Text);
        if (intent is null)
        {
            Rejected?.Invoke(new Rejection(RejectionReason.NoMatch, result.Text));
            return;
        }

        Matched?.Invoke(intent);

        KeySequence sequence;
        try { sequence = _resolver.Resolve(intent); }
        catch (ResolveException ex)
        {
            Rejected?.Invoke(new Rejection(RejectionReason.Unresolvable, result.Text, ex.Message));
            return;
        }

        _sender.Send(sequence);
        Sent?.Invoke(sequence);
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

```
& $dotnet test --filter VoicePipelineTests
```

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Pipeline RonVoice.Tests/VoicePipelineTests.cs
git commit -m "feat: wire recognition through matching and resolution to input"
```

---

## Task 10: `WasapiCapture` — o microfone de verdade

**Files:**
- Create: `RonVoice.Core/Audio/WasapiCapture.cs`
- Modify: `RonVoice.Core/RonVoice.Core.csproj` (pacote NAudio)

**Interfaces:**
- Consumes: `IAudioCapture`.
- Produces: `WasapiCapture(int deviceNumber = 0)`; `WasapiCapture.ListDevices()` → `IReadOnlyList<string>`.

- [ ] **Step 1: Adicionar o NAudio**

```
& $dotnet add RonVoice.Core package NAudio --version 2.2.1
```

- [ ] **Step 2: Implementar**

Create `RonVoice.Core/Audio/WasapiCapture.cs`:

```csharp
using NAudio.Wave;

namespace RonVoice.Core.Audio;

/// <summary>
/// Microfone real a 16 kHz mono 16 bits — o formato que o modelo espera, pedido
/// direto ao driver para não precisar reamostrar.
/// </summary>
public sealed class WasapiCapture : IAudioCapture
{
    readonly WaveInEvent _waveIn;
    bool _disposed;

    public event Action<ReadOnlyMemory<byte>>? OnAudio;
    public event Action? OnStopped;

    public WasapiCapture(int deviceNumber = 0)
    {
        if (WaveInEvent.DeviceCount == 0)
            throw new InvalidOperationException(
                "nenhum dispositivo de entrada de áudio encontrado");

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50,
        };
        _waveIn.DataAvailable += (_, e) =>
            OnAudio?.Invoke(new ReadOnlyMemory<byte>(e.Buffer, 0, e.BytesRecorded));
        _waveIn.RecordingStopped += (_, _) => OnStopped?.Invoke();
    }

    public static IReadOnlyList<string> ListDevices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount)
                  .Select(i => WaveInEvent.GetCapabilities(i).ProductName)
                  .ToList();

    public void Start() => _waveIn.StartRecording();
    public void Stop() => _waveIn.StopRecording();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _waveIn.StopRecording(); } catch (Exception) { /* já parado */ }
        _waveIn.Dispose();
    }
}
```

- [ ] **Step 3: Verificar à mão que o dispositivo aparece**

Não há teste automatizado: depende de hardware. Confirme com um comando temporário
ou pelo `ronvoice listen --list-devices` da próxima tarefa. Registre no relatório
os dispositivos listados nesta máquina.

- [ ] **Step 4: Rodar a suíte inteira**

```
& $dotnet test
```

Esperado: tudo verde. O NAudio não pode ter quebrado nada.

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core
git commit -m "feat: capture microphone audio at 16 kHz mono"
```

---

## Task 11: `ronvoice listen` e o teste negativo

**Files:**
- Create: `RonVoice.Cli/Commands/ListenCommand.cs`, `RonVoice.Tests/SpeechIntegrationTests.cs`
- Modify: `RonVoice.Cli/Program.cs`

**Interfaces:**
- Consumes: tudo das tarefas anteriores.
- Produces: `ronvoice listen [--lang en|pt] [--from-wav <arquivo>] [--list-devices] [--device N] [--threshold F] [--dry-run] [--process <nome>]`.

- [ ] **Step 1: Implementar o comando**

Create `RonVoice.Cli/Commands/ListenCommand.cs`:

```csharp
using System.Globalization;
using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Cli.Commands;

public static class ListenCommand
{
    public static int Run(string[] args)
    {
        if (Cli.Flag(args, "--list-devices"))
        {
            var devices = WasapiCapture.ListDevices();
            if (devices.Count == 0) { Console.Error.WriteLine("nenhum microfone encontrado"); return 4; }
            for (var i = 0; i < devices.Count; i++) Console.WriteLine($"  {i}: {devices[i]}");
            return 0;
        }

        var lang = Cli.Option(args, "--lang") ?? "en";
        var fromWav = Cli.Option(args, "--from-wav");
        var dryRun = Cli.Flag(args, "--dry-run");

        var thresholdText = Cli.Option(args, "--threshold");
        if (thresholdText is not null
            && !double.TryParse(thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            Console.Error.WriteLine($"--threshold inválido: '{thresholdText}'");
            return 1;
        }
        var threshold = thresholdText is null
            ? 0.0
            : double.Parse(thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture);

        var map = CommandMap.Load(Cli.MapPath);
        var iniPath = KeybindReader.FindDefaultIniPath();
        if (iniPath is null)
            Console.Error.WriteLine("AVISO: Input.ini não encontrado; usando keybind_defaults");
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        string modelPath;
        try { modelPath = ModelLocator.Find(lang); }
        catch (ModelNotFoundException ex) { Console.Error.WriteLine(ex.Message); return 5; }

        // Com --from-wav o portão fica aberto: não há jogo para estar em foco.
        var processName = Cli.Option(args, "--process");
        var gate = new ListenGate(
            fromWav is not null
                ? () => true
                : () => ForegroundGuard.IsGameForeground(
                    processName is null ? null : [processName]),
            () => false);

        using var engine = new VoskSpeechEngine(modelPath, GrammarBuilder.Build(map, lang));
        var pipeline = new VoicePipeline(
            engine, gate,
            new PhraseMatcher(map, lang),
            new CommandResolver(map, binds),
            new SendInputSender(dryRun),
            threshold);

        pipeline.Heard += r => Console.WriteLine($"ouvi     : {r.Text}  (conf {r.AverageConfidence:0.00})");
        pipeline.Matched += i => Console.WriteLine(
            $"casou    : element={i.Element ?? "-"} order={i.OrderId ?? "-"} queue={i.Queue}");
        pipeline.Rejected += r => Console.WriteLine(
            $"rejeitada: {r.Reason} — {r.Text}{(r.Detail is null ? "" : $" ({r.Detail})")}");
        pipeline.Sent += s => Console.WriteLine($"enviada  : {s.Steps.Count} passos");
        pipeline.Start();

        using IAudioCapture capture = fromWav is not null
            ? new WavFileCapture(fromWav)
            : new WasapiCapture(int.Parse(Cli.Option(args, "--device") ?? "0", CultureInfo.InvariantCulture));

        capture.OnAudio += chunk => pipeline.Push(chunk);
        capture.OnStopped += () => pipeline.Flush();

        if (fromWav is not null)
        {
            capture.Start();                 // síncrono: retorna no fim do arquivo
            return 0;
        }

        Console.WriteLine($"escutando ({lang}) — Ctrl+C para sair");
        if (!ForegroundGuard.IsElevated())
            Console.Error.WriteLine("AVISO: sem elevação, as teclas não chegam ao jogo.");

        using var quit = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Set(); };
        capture.Start();
        quit.Wait();
        capture.Stop();
        return 0;
    }
}
```

- [ ] **Step 2: Ligar no despacho**

Em `RonVoice.Cli/Program.cs`, acrescente ao `switch`:

```csharp
    "listen" => ListenCommand.Run(rest),
```

E ao `Help()`:

```
ronvoice listen [--lang en|pt] [--from-wav <arq>] [--list-devices] [--device N]
                [--threshold F] [--dry-run] [--process <nome>]   escuta e envia
```

- [ ] **Step 3: Escrever o teste de integração, incluindo o negativo**

Gere primeiro os áudios:

```
& $dotnet run --project RonVoice.Cli -- synth --out RonVoice.Tests/audio --phrase "stack left"
& $dotnet run --project RonVoice.Cli -- synth --out RonVoice.Tests/audio --phrase "red team open with flashbang"
& $dotnet run --project RonVoice.Cli -- synth --out RonVoice.Tests/audio --phrase "the quarterly earnings report was disappointing"
```

Create `RonVoice.Tests/SpeechIntegrationTests.cs`:

```csharp
using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>
/// O pipeline completo com o Vosk de verdade, dirigido por WAV. Prova que falar
/// produz o mesmo intent que digitar, e — o mais importante — que fala fora do
/// vocabulário não dispara nada.
/// </summary>
public class SpeechIntegrationTests
{
    static string ModelsDir =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "models");
    static string AudioDir => Path.Combine(AppContext.BaseDirectory, "audio");

    static (List<Intent> Matched, List<KeySequence> Sent, List<Rejection> Rejected) Run(string wavName)
    {
        var wav = Path.Combine(AudioDir, wavName);
        Assert.True(File.Exists(wav), $"gere o áudio primeiro: {wav}");

        var map = CommandMap.Load(CommandMapTests.MapPath);
        var binds = KeybindReader.Read(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

        using var engine = new VoskSpeechEngine(
            ModelLocator.Find("en", ModelsDir), GrammarBuilder.Build(map, "en"));

        var sender = new RecordingSender();
        var pipeline = new VoicePipeline(
            engine,
            new ListenGate(() => true, () => false),
            new PhraseMatcher(map, "en"),
            new CommandResolver(map, binds),
            sender);

        var matched = new List<Intent>();
        var rejected = new List<Rejection>();
        pipeline.Matched += matched.Add;
        pipeline.Rejected += rejected.Add;
        pipeline.Start();

        using var capture = new WavFileCapture(wav);
        capture.OnAudio += chunk => pipeline.Push(chunk);
        capture.OnStopped += () => pipeline.Flush();
        capture.Start();

        return (matched, sender.Sent, rejected);
    }

    [Fact]
    public void SpokenPhraseProducesTheSameIntentAsTypedText()
    {
        var (matched, sent, _) = Run("stack_left.wav");

        var typed = new PhraseMatcher(CommandMap.Load(CommandMapTests.MapPath), "en")
            .Match("stack left");

        Assert.Contains(matched, i => i.OrderId == typed!.OrderId);
        Assert.NotEmpty(sent);
    }

    [Fact]
    public void SpokenElementAndOrderResolveTogether()
    {
        var (matched, _, _) = Run("red_team_open_with_flashbang.wav");
        Assert.Contains(matched, i => i is { Element: "red", OrderId: "door.open.flashbang" });
    }

    /// <summary>
    /// O teste mais importante desta etapa. Sem [unk] na gramática, o Vosk força
    /// qualquer áudio para dentro dela e o app passa a mandar ordens sozinho —
    /// e com o microfone sempre ligado isso aconteceria o dia inteiro.
    /// </summary>
    [Fact]
    public void SpeechOutsideTheVocabularySendsNothing()
    {
        var (_, sent, _) = Run("the_quarterly_earnings_report_was_disappointing.wav");
        Assert.Empty(sent);
    }
}
```

- [ ] **Step 4: Rodar**

```
& $dotnet test
```

Esperado: tudo verde. Se `SpeechOutsideTheVocabularySendsNothing` falhar, **pare e reporte**: significa que o `[unk]` não está segurando, e é o defeito mais grave possível nesta etapa.

- [ ] **Step 5: Verificar à mão com o WAV**

```
& $dotnet run --project RonVoice.Cli -- listen --from-wav RonVoice.Tests/audio/stack_left.wav --dry-run
```

Esperado: linhas de `ouvi`, `casou` e `enviada`.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Cli RonVoice.Tests/SpeechIntegrationTests.cs
git commit -m "feat: add the listen command and the end-to-end speech tests"
```

---

## Task 12: `RonVoice.Tray` — estado visível e mute

**Files:**
- Create: `RonVoice.Tray/RonVoice.Tray.csproj`, `RonVoice.Tray/Program.cs`, `RonVoice.Tray/TrayIcon.cs`, `RonVoice.Tray/GlobalHotkey.cs`
- Modify: `RonVoice.sln`

**Interfaces:**
- Consumes: `VoicePipeline`, `ListenGate`, `ListenState`, `WasapiCapture`, `VoskSpeechEngine`.
- Produces: um executável de bandeja.

- [ ] **Step 1: Criar o projeto**

```
& $dotnet new winforms -o RonVoice.Tray -n RonVoice.Tray
& $dotnet sln add RonVoice.Tray
& $dotnet add RonVoice.Tray reference RonVoice.Core
```

Remova os arquivos de template `Form1.cs` e `Form1.Designer.cs` — este app não tem janela.
Em `RonVoice.Tray/RonVoice.Tray.csproj`, garanta `<UseWindowsForms>true</UseWindowsForms>` e
acrescente a cópia do mapa:

```xml
  <ItemGroup>
    <None Include="../data/ron_commands.json" Link="data/ron_commands.json"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Implementar o atalho global**

Create `RonVoice.Tray/GlobalHotkey.cs`:

```csharp
using System.Runtime.InteropServices;

namespace RonVoice.Tray;

/// <summary>
/// Atalho global via RegisterHotKey. O mesmo mecanismo que a etapa 6 vai usar
/// para observar F5/F6/F7 e manter o indicador de elemento em sincronia.
/// </summary>
public sealed partial class GlobalHotkey : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_ID = 0xB001;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    public event Action? Pressed;

    public GlobalHotkey(uint modifiers, uint virtualKey)
    {
        CreateHandle(new CreateParams());
        if (!RegisterHotKey(Handle, HOTKEY_ID, modifiers, virtualKey))
            throw new InvalidOperationException(
                $"não foi possível registrar o atalho global (erro {Marshal.GetLastWin32Error()}); "
                + "outro programa provavelmente já o usa");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, HOTKEY_ID);
        DestroyHandle();
    }
}
```

- [ ] **Step 3: Implementar o ícone**

Create `RonVoice.Tray/TrayIcon.cs`:

```csharp
using System.Drawing;
using RonVoice.Core.Pipeline;

namespace RonVoice.Tray;

/// <summary>
/// Quatro estados visíveis. Com o microfone sempre ligado, saber se ele está
/// ativo não é conforto: é a única forma de o jogador perceber que está sendo
/// ouvido.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon;
    readonly Dictionary<ListenState, Icon> _icons = [];
    Icon? _faultIcon;

    public event Action? MuteRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        foreach (var (state, color) in new[]
        {
            (ListenState.Listening, Color.LimeGreen),
            (ListenState.Idle, Color.Gray),
            (ListenState.Muted, Color.OrangeRed),
        })
            _icons[state] = Dot(color);

        _faultIcon = Dot(Color.Red);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Mutar / desmutar", null, (_, _) => MuteRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = _icons[ListenState.Idle],
            Text = "RonVoice",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    public void Show(ListenState state)
    {
        _icon.Icon = _icons[state];
        _icon.Text = state switch
        {
            ListenState.Listening => "RonVoice — escutando",
            ListenState.Idle => "RonVoice — jogo fora de foco",
            ListenState.Muted => "RonVoice — mudo",
            _ => "RonVoice",
        };
    }

    public void ShowFault(string message)
    {
        _icon.Icon = _faultIcon;
        // NotifyIcon.Text lança acima de 63 caracteres.
        var text = "RonVoice — falha: " + message;
        _icon.Text = text.Length > 63 ? text[..63] : text;
        _icon.ShowBalloonTip(5000, "RonVoice", message, ToolTipIcon.Error);
    }

    static Icon Dot(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var i in _icons.Values) i.Dispose();
        _faultIcon?.Dispose();
        _faultIcon = null;
    }
}
```

- [ ] **Step 4: Implementar o app**

Create `RonVoice.Tray/Program.cs`:

```csharp
using RonVoice.Core.Audio;
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;
using RonVoice.Core.Pipeline;
using RonVoice.Core.Speech;

namespace RonVoice.Tray;

static class Program
{
    const uint MOD_CONTROL = 0x0002, MOD_ALT = 0x0001;
    const uint VK_M = 0x4D;

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var lang = args.Length > 0 ? args[0] : "en";
        using var tray = new TrayIcon();

        try { RunPipeline(lang, tray); }
        catch (Exception ex)
        {
            tray.ShowFault(ex.Message);
            Application.Run();          // mantém o ícone vivo para o usuário ler o erro
        }
    }

    static void RunPipeline(string lang, TrayIcon tray)
    {
        var mapPath = Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json");
        var map = CommandMap.Load(mapPath);
        var iniPath = KeybindReader.FindDefaultIniPath();
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        var gate = new ListenGate(() => ForegroundGuard.IsGameForeground());
        gate.StateChanged += s => tray.Show(s);
        tray.Show(gate.State);

        using var engine = new VoskSpeechEngine(
            ModelLocator.Find(lang), GrammarBuilder.Build(map, lang));

        var pipeline = new VoicePipeline(
            engine, gate,
            new PhraseMatcher(map, lang),
            new CommandResolver(map, binds),
            new SendInputSender());
        pipeline.Start();

        using var capture = new WasapiCapture();
        capture.OnAudio += chunk => pipeline.Push(chunk);
        capture.Start();

        using var hotkey = new GlobalHotkey(MOD_CONTROL | MOD_ALT, VK_M);
        hotkey.Pressed += () => { gate.Toggle(); tray.Show(gate.State); };

        tray.MuteRequested += () => { gate.Toggle(); tray.Show(gate.State); };
        tray.ExitRequested += () => Application.Exit();

        Application.Run();
        capture.Stop();
    }
}
```

- [ ] **Step 5: Rodar a suíte e o app**

```
& $dotnet test
& $dotnet build
```

Esperado: tudo verde, build limpo.

Rode o app com o jogo **fechado** primeiro:

```
& $dotnet run --project RonVoice.Tray
```

Esperado: ícone cinza (jogo fora de foco). `Ctrl+Alt+M` alterna para laranja (mudo).
Menu de contexto com Mutar e Sair. Com o jogo aberto e em foco, o ícone fica verde.

Registre no relatório o que você observou em cada estado.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Tray RonVoice.sln
git commit -m "feat: add a tray app with visible listening state and a mute hotkey"
```

---

## Validação com voz real (fecha a etapa 5)

Não é tarefa de código. É o que só o autor pode fazer.

- [ ] **1. Falar contra o texto.** Com o jogo aberto e o app de bandeja rodando elevado,
  dizer uma ordem e conferir que o intent casado é o mesmo que
  `ronvoice test "<a mesma frase>"` produz.
- [ ] **2. O teste negativo com voz humana.** Conversar normalmente com o jogo em foco por
  alguns minutos e contar quantas ordens dispararam sem intenção. É o risco residual
  aceito na seção 2 da spec, e só o uso mede.
- [ ] **3. Calibrar o limiar.** Se houver falsas ativações, subir `--threshold` em passos
  de 0.05 até parar, e anotar o valor. Se não houver, deixar em 0.
- [ ] **4. Latência.** Medir do fim da fala até a tecla chegar ao jogo.
- [ ] **5. Português.** Repetir 1 e 2 com `--lang pt`.

---

## Self-Review

**Cobertura da spec:**

| Seção da spec | Onde é implementada |
|---|---|
| 2 sempre-ligado, portão por foco | Task 6 (`ListenGate`), Task 9 (`Push` reseta ao fechar) |
| 3 API do Vosk | Task 8, e as constantes do plano |
| 4 estrutura | todas |
| 5.1 gramática plana | Task 2 |
| 5.2 `[unk]` obrigatório | Task 2 (teste), Task 11 (teste negativo ponta a ponta) |
| 5.3 medir composição | Task 4 |
| 6 contratos | Tasks 5, 7, 8, 9 |
| 6 limiar de confiança | Task 9, exposto em `--threshold` na Task 11 |
| 7 pipeline e filas | Task 9 |
| 8 bandeja | Task 12 |
| 9 tratamento de erro | Tasks 1, 5, 9, 10, 11, 12 |
| 10.1 lógica pura | Tasks 2, 6 |
| 10.2 pipeline com dublês | Task 9 |
| 10.3 Vosk por WAV, síntese | Tasks 3, 4, 11 |
| 11 modelos | Task 1 |
| 12 pendências | Task 4 responde a 1; as outras ficam na validação com voz real |
| 13 critérios de pronto | Tasks 2, 6, 11, 12 |

Sem lacunas.

**Consistência de tipos:** `RecognitionResult(string, IReadOnlyList<WordConfidence>, bool)`
é criado na Task 5 e consumido igual nas 8, 9 e 11. `ISpeechEngine` tem `Feed`/`Flush`/
`Reset` na Task 8 e é implementado pelo dublê da Task 9 com a mesma superfície.
`IAudioCapture` expõe `OnAudio`/`OnStopped`/`Start`/`Stop` nas Tasks 7, 10, 11 e 12.
`ListenGate(Func<bool>, Func<bool>?)` com `ShouldProcess`/`State`/`Toggle`/`Poll` é usado
igual nas Tasks 6, 9, 11 e 12. `GrammarBuilder.UnknownToken` é referenciado nas Tasks 2, 3 e 5.

**Riscos conhecidos, registrados de propósito:**

- **Task 4 pode reprovar a hipótese central.** O plano manda parar e reportar em vez de
  improvisar o produto cartesiano, porque o fallback muda a Task 2 e acrescenta tarefas.
- **Task 3 depende de haver voz sintetizada instalada no Windows.** Se não houver, as
  Tasks 4 e 11 precisam ser replanejadas sobre gravações reais. O plano manda parar e
  reportar, não contornar.
- **Task 12 não tem teste automatizado.** Bandeja, ícones e atalho global dependem de
  sessão interativa. A verificação é a observação manual do Step 5, registrada no relatório.
