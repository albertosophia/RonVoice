# RonVoice — frases próprias e verificação guiada (etapa 7) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Quem fala diferente do mapa consegue acrescentar as próprias frases sem quebrar nada, e descobre pela tela o que falta em vez de por tentativa e erro.

**Architecture:** As duas funcionalidades são lógica pura no `RonVoice.Core`, testáveis sem UI. `CustomPhrases.Apply` mescla o arquivo do usuário no mapa já carregado e devolve os problemas encontrados; `StartupChecks` roda cinco verificações com predicados injetados. A UI apenas exibe.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-29-ronvoice-custom-phrases-design.md` — leia antes de começar. Se algo aqui divergir dela, a spec vence e o conflito deve ser levantado, não resolvido em silêncio.

## Global Constraints

- **Invoque o SDK pelo caminho absoluto.** O `dotnet` do PATH é runtime 7 sem SDK:

  ```powershell
  $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
  & $dotnet build
  & $dotnet test
  ```

  Onde este plano escreve `dotnet ...`, leia `& $dotnet ...`.

- **PowerShell 5.1**: sem `&&` e sem `||`; use `;`.
- **`RonVoice.Core` continua sem referência a WPF, WinForms ou `System.Windows`.**
- **Zero lógica de negócio no `RonVoice.App`** — §9 do brief. Lógica em view models ou no Core.
- **A suíte tem 308 testes hoje e deve continuar verde**, além dos que você acrescentar.
- **Build warning-clean**: `TreatWarningsAsErrors` está ligado; `using` não usado é erro.
- **Nada das etapas 1–6 muda de comportamento.** `PhraseMatcher`, `PhraseIndex`, `CommandResolver`, `GrammarBuilder`, `VoicePipeline` ficam como estão. O mapa mesclado entra neles pelo caminho normal.
- **A checagem de colisão usa `TextNormalizer.Tokenize`**, a mesma do matcher. Outra comparação faria a checagem mentir.
- **Uma frase recusada nunca derruba o carregamento**; o resto do arquivo continua valendo.
- **Nada nesta etapa pode impedir o app de abrir.** Quem tem arquivo errado precisa da tela para descobrir o erro.
- Código, identificadores e commits em **inglês**. Documentação e texto de interface em **português**.

---

## File Structure

```
RonVoice.Core/
  Config/
    CustomPhrases.cs        arquivo do usuário -> mapa mesclado + problemas
    PhraseIssue.cs          record do problema, com o motivo
  Startup/
    StartupChecks.cs        as cinco verificações
    CheckResult.cs          record do resultado

RonVoice.App/
  ViewModels/
    CommandsViewModel.cs    MODIFICADO: mostra os avisos e marca frases próprias
    OrderRowViewModel.cs    MODIFICADO: sabe quais frases são do usuário
    ChecksViewModel.cs      estado das verificações na tela
  Views/
    ChecksView.xaml(.cs)    a lista de verificações
  RonVoiceSession.cs        MODIFICADO: aplica as frases, liga Recarregar e Verificar

RonVoice.Tests/
  CustomPhrasesTests.cs
  StartupChecksTests.cs
  ChecksViewModelTests.cs
```

---

## Task 1: `CustomPhrases` — mesclar e validar

O núcleo da etapa. Tudo o mais depende disto.

**Files:**
- Create: `RonVoice.Core/Config/PhraseIssue.cs`, `RonVoice.Core/Config/CustomPhrases.cs`
- Test: `RonVoice.Tests/CustomPhrasesTests.cs`

**Interfaces:**
- Consumes: `CommandMap` com `Orders`, `Elements`, `Queue`, `Defaults`, `Timing`;
  `OrderDefinition(Id, Context, Path, CloseMenu, Confidence, Phrases)`;
  `TextNormalizer.Tokenize(string)`.
- Produces:
  - `enum PhraseIssueKind { UnknownOrder, Collision, Duplicate, Empty, FileUnreadable }`
  - `record PhraseIssue(PhraseIssueKind Kind, string OrderId, string Phrase, string Message)`
  - `record CustomPhraseResult(CommandMap Map, IReadOnlyList<PhraseIssue> Issues, IReadOnlyDictionary<string, IReadOnlyList<string>> Accepted)`
  - `CustomPhrases.FileName` → `"minhas_frases.json"`
  - `CustomPhrases.Apply(CommandMap map, string? filePath, string language)` → `CustomPhraseResult`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/CustomPhrasesTests.cs`:

```csharp
using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Config;

namespace RonVoice.Tests;

public class CustomPhrasesTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static string WriteFile(Dictionary<string, string[]> content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-frases-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(content));
        return path;
    }

    static IReadOnlyList<string> PhrasesOf(CommandMap map, string orderId, string lang) =>
        map.Orders[orderId].Phrases[lang];

    [Fact]
    public void NoFileMeansNoChangeAndNoComplaints()
    {
        var result = CustomPhrases.Apply(Map(), null, "pt");
        Assert.Empty(result.Issues);
        Assert.Equal(371, result.Map.Orders.Values.Sum(o => o.Phrases["pt"].Count));
    }

    [Fact]
    public void MissingFileIsSilent()
    {
        var result = CustomPhrases.Apply(
            Map(), Path.Combine(Path.GetTempPath(), "nao-existe-ronvoice.json"), "pt");
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void AddsAPhraseToAnExistingOrder()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            Assert.Empty(result.Issues);
            Assert.Contains("manda a bang", PhrasesOf(result.Map, "door.open.flashbang", "pt"));
            Assert.Contains("manda a bang", result.Accepted["door.open.flashbang"]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void KeepsTheOriginalPhrasesOfThatOrder()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Contains("abre com flash", PhrasesOf(result.Map, "door.open.flashbang", "pt"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void DoesNotTouchOtherOrders()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var before = PhrasesOf(Map(), "hold", "pt").Count;
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Equal(before, PhrasesOf(result.Map, "hold", "pt").Count);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void UnknownOrderIsReportedAndIgnored()
    {
        var file = WriteFile(new() { ["ordem.que.nao.existe"] = ["qualquer coisa"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            var issue = Assert.Single(result.Issues);
            Assert.Equal(PhraseIssueKind.UnknownOrder, issue.Kind);
            Assert.Contains("ordem.que.nao.existe", issue.Message);
            Assert.Empty(result.Accepted);
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// A validacao que justifica a funcionalidade existir. Este projeto ja sofreu
    /// isso: "drop chemlight" estava em duas ordens e AS DUAS ficavam mudas, sem
    /// erro em lugar nenhum.
    /// </summary>
    [Fact]
    public void APhraseThatCollidesWithAnotherOrderIsRefused()
    {
        // "empilha" ja e' frase de door.stack.auto; nao pode entrar em hold.
        var file = WriteFile(new() { ["hold"] = ["empilha"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            var issue = Assert.Single(result.Issues);
            Assert.Equal(PhraseIssueKind.Collision, issue.Kind);
            Assert.Contains("door.stack.auto", issue.Message);
            Assert.DoesNotContain("empilha", PhrasesOf(result.Map, "hold", "pt"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ARefusedPhraseDoesNotBlockTheGoodOnesInTheSameFile()
    {
        var file = WriteFile(new()
        {
            ["hold"] = ["empilha", "fica quieto"],
        });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            Assert.Single(result.Issues);
            Assert.Contains("fica quieto", PhrasesOf(result.Map, "hold", "pt"));
        }
        finally { File.Delete(file); }
    }

    /// <summary>A checagem tem que usar a mesma normalizacao do matcher, ou mente.</summary>
    [Fact]
    public void CollisionIgnoresCaseAccentAndPunctuation()
    {
        var file = WriteFile(new() { ["hold"] = ["Empilha!"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Equal(PhraseIssueKind.Collision, Assert.Single(result.Issues).Kind);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ADuplicateOnTheSameOrderIsIgnoredQuietly()
    {
        var file = WriteFile(new() { ["door.stack.auto"] = ["empilha"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");

            var issue = Assert.Single(result.Issues);
            Assert.Equal(PhraseIssueKind.Duplicate, issue.Kind);
            // Nao duplica na lista.
            Assert.Equal(1, PhrasesOf(result.Map, "door.stack.auto", "pt")
                .Count(p => p == "empilha"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EmptyPhrasesAreIgnored()
    {
        var file = WriteFile(new() { ["hold"] = ["", "   "] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.All(result.Issues, i => Assert.Equal(PhraseIssueKind.Empty, i.Kind));
            Assert.Empty(result.Accepted);
        }
        finally { File.Delete(file); }
    }

    /// <summary>Arquivo quebrado nao pode impedir o app de abrir.</summary>
    [Fact]
    public void AMalformedFileYieldsTheOriginalMapPlusOneComplaint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-ruim-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ isto nao e json ");
        try
        {
            var result = CustomPhrases.Apply(Map(), path, "pt");

            Assert.Equal(PhraseIssueKind.FileUnreadable, Assert.Single(result.Issues).Kind);
            Assert.Equal(371, result.Map.Orders.Values.Sum(o => o.Phrases["pt"].Count));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OnlyTheChosenLanguageIsTouched()
    {
        var file = WriteFile(new() { ["hold"] = ["fica quieto"] });
        try
        {
            var before = PhrasesOf(Map(), "hold", "en").Count;
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Equal(before, PhrasesOf(result.Map, "hold", "en").Count);
        }
        finally { File.Delete(file); }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter CustomPhrasesTests
```

Esperado: erro de compilação — `CustomPhrases` não existe.

- [ ] **Step 3: Escrever o record do problema**

Create `RonVoice.Core/Config/PhraseIssue.cs`:

```csharp
namespace RonVoice.Core.Config;

public enum PhraseIssueKind
{
    /// <summary>O id da ordem não existe no mapa.</summary>
    UnknownOrder,
    /// <summary>A frase já pertence a outra ordem; aceitar deixaria as duas mudas.</summary>
    Collision,
    /// <summary>A frase já existe nessa mesma ordem. Inofensivo.</summary>
    Duplicate,
    /// <summary>Frase vazia ou só espaços.</summary>
    Empty,
    /// <summary>O arquivo existe mas não pôde ser lido ou não é JSON válido.</summary>
    FileUnreadable,
}

public sealed record PhraseIssue(
    PhraseIssueKind Kind, string OrderId, string Phrase, string Message);
```

- [ ] **Step 4: Escrever a mesclagem**

Create `RonVoice.Core/Config/CustomPhrases.cs`:

```csharp
using System.Text.Json;
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Config;

public sealed record CustomPhraseResult(
    CommandMap Map,
    IReadOnlyList<PhraseIssue> Issues,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Accepted);

/// <summary>
/// Acrescenta ao mapa as frases que o usuário escreveu. Só acrescenta: não
/// remove frase de fábrica nem cria ordem, porque uma ordem nova exigiria que
/// ele escrevesse a sequência de teclas do menu, e uma sequência errada manda
/// teclas erradas ao jogo sem explicação nenhuma.
/// </summary>
public static class CustomPhrases
{
    public const string FileName = "minhas_frases.json";

    public static CustomPhraseResult Apply(CommandMap map, string? filePath, string language)
    {
        var issues = new List<PhraseIssue>();
        var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        if (filePath is null || !File.Exists(filePath))
            return new CustomPhraseResult(map, issues, accepted);

        Dictionary<string, string[]>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                File.ReadAllText(filePath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            issues.Add(new PhraseIssue(
                PhraseIssueKind.FileUnreadable, "", "",
                $"não consegui ler {Path.GetFileName(filePath)}: {ex.Message}"));
            return new CustomPhraseResult(map, issues, accepted);
        }

        if (raw is null || raw.Count == 0)
            return new CustomPhraseResult(map, issues, accepted);

        // Índice de frase normalizada -> ordem dona, para detectar colisão.
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var order in map.Orders.Values)
            if (order.Phrases.TryGetValue(language, out var existing))
                foreach (var p in existing)
                    owner.TryAdd(Normalize(p), order.Id);

        var additions = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (orderId, phrases) in raw)
        {
            if (!map.Orders.ContainsKey(orderId))
            {
                issues.Add(new PhraseIssue(
                    PhraseIssueKind.UnknownOrder, orderId, "",
                    $"ordem desconhecida: {orderId}"));
                continue;
            }

            foreach (var phrase in phrases ?? [])
            {
                var normalized = Normalize(phrase ?? "");

                if (normalized.Length == 0)
                {
                    issues.Add(new PhraseIssue(
                        PhraseIssueKind.Empty, orderId, phrase ?? "", "frase vazia"));
                    continue;
                }

                if (owner.TryGetValue(normalized, out var existingOwner))
                {
                    if (existingOwner == orderId)
                        issues.Add(new PhraseIssue(
                            PhraseIssueKind.Duplicate, orderId, phrase!,
                            $"\"{phrase}\" já existe em {orderId}"));
                    else
                        // Aceitar deixaria as duas ordens mudas: o matcher
                        // rejeita por ambiguidade e não há erro em lugar nenhum.
                        issues.Add(new PhraseIssue(
                            PhraseIssueKind.Collision, orderId, phrase!,
                            $"\"{phrase}\" já pertence a {existingOwner}; "
                            + "aceitar deixaria as duas ordens sem funcionar"));
                    continue;
                }

                owner[normalized] = orderId;
                if (!additions.TryGetValue(orderId, out var list))
                    additions[orderId] = list = [];
                list.Add(phrase!);
            }
        }

        foreach (var (orderId, list) in additions)
            accepted[orderId] = list;

        return new CustomPhraseResult(Merge(map, additions, language), issues, accepted);
    }

    static string Normalize(string phrase) =>
        string.Join(' ', TextNormalizer.Tokenize(phrase));

    static CommandMap Merge(
        CommandMap map, Dictionary<string, List<string>> additions, string language)
    {
        if (additions.Count == 0) return map;

        var orders = new Dictionary<string, OrderDefinition>(StringComparer.Ordinal);
        foreach (var (id, order) in map.Orders)
        {
            if (!additions.TryGetValue(id, out var extra)) { orders[id] = order; continue; }

            var phrases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var (lang, list) in order.Phrases)
                phrases[lang] = lang == language ? [.. list, .. extra] : list;

            orders[id] = order with { Phrases = phrases };
        }

        return map.WithOrders(orders);
    }
}
```

- [ ] **Step 5: Dar ao `CommandMap` uma forma de trocar as ordens**

O `CommandMap` tem construtor privado. Acrescente a `RonVoice.Core/Commands/CommandMap.cs`,
logo depois do construtor:

```csharp
    /// <summary>
    /// Cópia com outro conjunto de ordens, preservando o resto. Usado pela
    /// mesclagem das frases do usuário, que não pode alterar o mapa no lugar.
    /// </summary>
    public CommandMap WithOrders(IReadOnlyDictionary<string, OrderDefinition> orders) =>
        new(orders, Elements, Queue, Defaults, Timing);
```

- [ ] **Step 6: Rodar e ver passar**

```
& $dotnet test --filter CustomPhrasesTests
```

Esperado: 13 testes passando.

- [ ] **Step 7: Commit**

```bash
git add RonVoice.Core RonVoice.Tests/CustomPhrasesTests.cs
git commit -m "feat: merge user phrases into the map, refusing collisions"
```

---

## Task 2: As invariantes do mapa sobrevivem à mesclagem

Task separada porque prova outra coisa: não que a mesclagem funciona, mas que ela **não
estraga** o que já estava garantido.

**Files:**
- Test: `RonVoice.Tests/CustomPhrasesTests.cs` (estender)

**Interfaces:**
- Consumes: `CustomPhrases.Apply`, `PhraseMatcher`, `CommandMap`.
- Produces: nada — só garantias.

- [ ] **Step 1: Escrever os testes**

Acrescente a `RonVoice.Tests/CustomPhrasesTests.cs`:

```csharp
    /// <summary>
    /// A garantia central do projeto: nenhuma frase resolve para ordem errada.
    /// Frases proprias nao podem quebra-la.
    /// </summary>
    [Fact]
    public void AfterMergingNoPhraseResolvesToTheWrongOrder()
    {
        var file = WriteFile(new()
        {
            ["door.open.flashbang"] = ["manda a bang", "joga a luz e entra"],
            ["hold"] = ["fica quieto"],
            ["door.stack.left"] = ["cola na esquerda"],
        });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            Assert.Empty(result.Issues);

            var matcher = new RonVoice.Core.Matching.PhraseMatcher(result.Map, "pt");
            var wrong = new List<string>();

            foreach (var order in result.Map.Orders.Values)
                foreach (var phrase in order.Phrases["pt"])
                {
                    var got = matcher.Match(phrase)?.OrderId;
                    if (got is not null && got != order.Id)
                        wrong.Add($"{phrase}: {order.Id} -> {got}");
                }

            Assert.Empty(wrong);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheNewPhrasesAreActuallyReachable()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            var matcher = new RonVoice.Core.Matching.PhraseMatcher(result.Map, "pt");

            Assert.Equal("door.open.flashbang", matcher.Match("manda a bang")?.OrderId);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void EveryOrderStaysReachableAfterMerging()
    {
        var file = WriteFile(new() { ["hold"] = ["fica quieto"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            var matcher = new RonVoice.Core.Matching.PhraseMatcher(result.Map, "pt");

            var reachable = new HashSet<string>(StringComparer.Ordinal);
            foreach (var order in result.Map.Orders.Values)
                foreach (var phrase in order.Phrases["pt"])
                    if (matcher.Match(phrase)?.OrderId == order.Id) reachable.Add(order.Id);

            Assert.Empty(result.Map.Orders.Keys.Except(reachable));
        }
        finally { File.Delete(file); }
    }

    /// <summary>
    /// A gramatica precisa conter a frase nova, senao o Vosk nunca a ouve — e o
    /// usuario conclui que a funcionalidade nao funciona.
    /// </summary>
    [Fact]
    public void TheNewPhraseEntersTheRecognizerGrammar()
    {
        var file = WriteFile(new() { ["door.open.flashbang"] = ["manda a bang"] });
        try
        {
            var result = CustomPhrases.Apply(Map(), file, "pt");
            var grammar = RonVoice.Core.Speech.GrammarBuilder.Build(result.Map, "pt");
            Assert.Contains("manda a bang", grammar);
        }
        finally { File.Delete(file); }
    }
```

- [ ] **Step 2: Rodar**

```
& $dotnet test --filter CustomPhrasesTests
```

Esperado: 17 testes passando. Se `AfterMergingNoPhraseResolvesToTheWrongOrder` falhar,
**pare e reporte**: significa que a checagem de colisão deixou passar algo, e é o defeito
mais grave possível nesta etapa.

- [ ] **Step 3: Commit**

```bash
git add RonVoice.Tests/CustomPhrasesTests.cs
git commit -m "test: prove user phrases do not break the map's guarantees"
```

---

## Task 3: `StartupChecks`

**Files:**
- Create: `RonVoice.Core/Startup/CheckResult.cs`, `RonVoice.Core/Startup/StartupChecks.cs`
- Test: `RonVoice.Tests/StartupChecksTests.cs`

**Interfaces:**
- Consumes: nada — tudo injetado.
- Produces:
  - `enum CheckStatus { Ok, Warning, Failed }`
  - `record CheckResult(string Name, CheckStatus Status, string Message)`
  - `record CheckInputs(bool Elevated, bool ModelPresent, string Language, double MicrophonePeak, bool GameFound, bool InputIniFound)`
  - `StartupChecks.Run(CheckInputs inputs)` → `IReadOnlyList<CheckResult>`
  - `StartupChecks.Summarize(IReadOnlyList<CheckResult>)` → `string`
  - `StartupChecks.SilenceFloor` → `double` (0.02, o mesmo do `VoiceTestRunner`)

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/StartupChecksTests.cs`:

```csharp
using RonVoice.Core.Startup;

namespace RonVoice.Tests;

public class StartupChecksTests
{
    static CheckInputs AllGood() => new(
        Elevated: true, ModelPresent: true, Language: "en",
        MicrophonePeak: 0.5, GameFound: true, InputIniFound: true);

    static CheckResult Find(IReadOnlyList<CheckResult> r, string fragment) =>
        r.First(x => x.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void RunsFiveChecks() => Assert.Equal(5, StartupChecks.Run(AllGood()).Count);

    [Fact]
    public void EverythingGoodIsAllOk() =>
        Assert.All(StartupChecks.Run(AllGood()), c => Assert.Equal(CheckStatus.Ok, c.Status));

    /// <summary>
    /// Sem elevacao nenhuma tecla chega ao jogo e nao ha erro. E' falha, nao aviso.
    /// </summary>
    [Fact]
    public void NotElevatedIsAFailureAndSaysWhatToDo()
    {
        var check = Find(StartupChecks.Run(AllGood() with { Elevated = false }), "eleva");
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("administrador", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingModelIsAFailure()
    {
        var check = Find(StartupChecks.Run(AllGood() with { ModelPresent = false }), "modelo");
        Assert.Equal(CheckStatus.Failed, check.Status);
    }

    /// <summary>Silencio significa microfone, e' a distincao que evita a tarde perdida.</summary>
    [Fact]
    public void ASilentMicrophoneIsAFailureThatBlamesTheMicrophone()
    {
        var check = Find(StartupChecks.Run(AllGood() with { MicrophonePeak = 0.0 }), "microfone");
        Assert.Equal(CheckStatus.Failed, check.Status);
        Assert.Contains("microfone", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AudioJustAboveTheFloorCounts()
    {
        var check = Find(
            StartupChecks.Run(AllGood() with { MicrophonePeak = StartupChecks.SilenceFloor + 0.01 }),
            "microfone");
        Assert.Equal(CheckStatus.Ok, check.Status);
    }

    [Fact]
    public void GameNotFoundIsAWarningBecauseTheAppStillOpens()
    {
        var check = Find(StartupChecks.Run(AllGood() with { GameFound = false }), "jogo");
        Assert.Equal(CheckStatus.Warning, check.Status);
        Assert.Contains("Configuração", check.Message);
    }

    /// <summary>
    /// Sem Input.ini o app usa keybind_defaults e funciona; so' quebra para quem
    /// remapeou. Aviso, nao falha.
    /// </summary>
    [Fact]
    public void MissingInputIniIsAWarningNotAFailure()
    {
        var check = Find(StartupChecks.Run(AllGood() with { InputIniFound = false }), "teclas");
        Assert.Equal(CheckStatus.Warning, check.Status);
    }

    [Fact]
    public void SummaryOfEverythingGoodTellsThemWhatToSay()
    {
        var summary = StartupChecks.Summarize(StartupChecks.Run(AllGood()));
        Assert.Contains("pronto", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stack up", summary);
    }

    [Fact]
    public void SummaryWithAFailureNamesWhatIsMissing()
    {
        var summary = StartupChecks.Summarize(
            StartupChecks.Run(AllGood() with { Elevated = false }));
        Assert.DoesNotContain("pronto", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("administrador", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningsAloneStillCountAsReady()
    {
        var summary = StartupChecks.Summarize(
            StartupChecks.Run(AllGood() with { InputIniFound = false }));
        Assert.Contains("pronto", summary, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter StartupChecksTests
```

- [ ] **Step 3: Escrever o record**

Create `RonVoice.Core/Startup/CheckResult.cs`:

```csharp
namespace RonVoice.Core.Startup;

public enum CheckStatus
{
    Ok,
    /// <summary>Funciona, mas com ressalva que vale dizer.</summary>
    Warning,
    /// <summary>Não vai funcionar enquanto isto não for resolvido.</summary>
    Failed,
}

public sealed record CheckResult(string Name, CheckStatus Status, string Message);

/// <param name="MicrophonePeak">
/// Pico de áudio medido enquanto a pessoa falava. Vem de fora porque quem grava
/// é a UI; assim a lógica continua testável sem hardware.
/// </param>
public sealed record CheckInputs(
    bool Elevated,
    bool ModelPresent,
    string Language,
    double MicrophonePeak,
    bool GameFound,
    bool InputIniFound);
```

- [ ] **Step 4: Escrever as verificações**

Create `RonVoice.Core/Startup/StartupChecks.cs`:

```csharp
namespace RonVoice.Core.Startup;

/// <summary>
/// As cinco coisas que precisam estar certas, verificadas de uma vez e ditas em
/// português. Existem porque toda falha deste sistema é silenciosa: sem elas,
/// cada relato de "não funciona" vira uma conversa de quatro perguntas.
/// </summary>
public static class StartupChecks
{
    /// <summary>O mesmo piso do VoiceTestRunner, para os dois concordarem.</summary>
    public const double SilenceFloor = 0.02;

    public static IReadOnlyList<CheckResult> Run(CheckInputs i) =>
    [
        new("Elevação",
            i.Elevated ? CheckStatus.Ok : CheckStatus.Failed,
            i.Elevated
                ? "rodando como administrador"
                : "abra como administrador, senão as teclas não chegam ao jogo "
                  + "e não aparece erro nenhum"),

        new($"Modelo de voz ({i.Language})",
            i.ModelPresent ? CheckStatus.Ok : CheckStatus.Failed,
            i.ModelPresent
                ? "instalado"
                : $"o modelo de {i.Language} não está instalado"),

        new("Microfone",
            i.MicrophonePeak > SilenceFloor ? CheckStatus.Ok : CheckStatus.Failed,
            i.MicrophonePeak > SilenceFloor
                ? "captando som"
                : "não captei nenhum som. Confira o microfone escolhido na aba "
                  + "Configuração e o volume de entrada do Windows"),

        new("Jogo",
            i.GameFound ? CheckStatus.Ok : CheckStatus.Warning,
            i.GameFound
                ? "encontrado"
                : "não encontrei o Ready or Not. Escolha o executável na aba Configuração"),

        new("Teclas do jogo",
            i.InputIniFound ? CheckStatus.Ok : CheckStatus.Warning,
            i.InputIniFound
                ? "lidas do Input.ini"
                : "não achei o Input.ini; usando as teclas padrão. "
                  + "Se você remapeou algo no jogo, pode não funcionar"),
    ];

    public static string Summarize(IReadOnlyList<CheckResult> results)
    {
        var failed = results.Where(r => r.Status == CheckStatus.Failed).ToList();

        if (failed.Count == 0)
            return "Está pronto — abra o jogo e fale \"stack up\" mirando numa porta.";

        return "Falta resolver:\n"
             + string.Join('\n', failed.Select(f => $"  · {f.Name}: {f.Message}"));
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

```
& $dotnet test --filter StartupChecksTests
```

Esperado: 11 testes passando.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Startup RonVoice.Tests/StartupChecksTests.cs
git commit -m "feat: check the five things that silently break the app"
```

---

## Task 4: Mostrar as frases próprias e os avisos no catálogo

**Files:**
- Modify: `RonVoice.App/ViewModels/OrderRowViewModel.cs`, `RonVoice.App/ViewModels/CommandsViewModel.cs`, `RonVoice.App/Views/CommandsView.xaml`
- Test: `RonVoice.Tests/CommandsViewModelTests.cs` (estender)

**Interfaces:**
- Consumes: `PhraseIssue`, `CustomPhraseResult`.
- Produces:
  - `OrderRowViewModel(OrderDefinition order, IReadOnlyList<string>? customPhrases = null)` com `CustomPhrasesText` e `HasCustomPhrases`
  - `CommandsViewModel(CommandMap map, IReadOnlyDictionary<string, IReadOnlyList<string>>? custom = null, IReadOnlyList<PhraseIssue>? issues = null)` com `Issues`, `HasIssues`, `IssuesText`, `ReloadCommand`

- [ ] **Step 1: Escrever os testes**

Acrescente a `RonVoice.Tests/CommandsViewModelTests.cs`:

```csharp
    [Fact]
    public void MarksTheRowsThatCarryUserPhrases()
    {
        var custom = new Dictionary<string, IReadOnlyList<string>>
        {
            ["door.stack.left"] = new[] { "cola na esquerda" },
        };
        var vm = new CommandsViewModel(CommandMap.Load(CommandMapTests.MapPath), custom);

        var rows = vm.Groups.SelectMany(g => g.Orders).ToList();
        Assert.True(rows.First(o => o.Id == "door.stack.left").HasCustomPhrases);
        Assert.False(rows.First(o => o.Id == "hold").HasCustomPhrases);
    }

    [Fact]
    public void ShowsTheUserPhrasesSeparatelyFromTheFactoryOnes()
    {
        var custom = new Dictionary<string, IReadOnlyList<string>>
        {
            ["door.stack.left"] = new[] { "cola na esquerda" },
        };
        var vm = new CommandsViewModel(CommandMap.Load(CommandMapTests.MapPath), custom);
        var row = vm.Groups.SelectMany(g => g.Orders).First(o => o.Id == "door.stack.left");

        Assert.Contains("cola na esquerda", row.CustomPhrasesText);
    }

    /// <summary>
    /// Quem escreveu o arquivo precisa VER que uma linha dele foi recusada.
    /// Num log ninguem olha.
    /// </summary>
    [Fact]
    public void SurfacesTheIssuesSoARefusedPhraseIsNotSilent()
    {
        var issues = new List<PhraseIssue>
        {
            new(PhraseIssueKind.Collision, "hold", "empilha",
                "\"empilha\" já pertence a door.stack.auto"),
        };
        var vm = new CommandsViewModel(
            CommandMap.Load(CommandMapTests.MapPath), null, issues);

        Assert.True(vm.HasIssues);
        Assert.Contains("empilha", vm.IssuesText);
        Assert.Contains("door.stack.auto", vm.IssuesText);
    }

    [Fact]
    public void NoIssuesMeansNothingIsShown()
    {
        var vm = new CommandsViewModel(CommandMap.Load(CommandMapTests.MapPath));
        Assert.False(vm.HasIssues);
    }

    [Fact]
    public void SearchAlsoFindsUserPhrases()
    {
        var custom = new Dictionary<string, IReadOnlyList<string>>
        {
            ["door.stack.left"] = new[] { "cola na esquerda" },
        };
        var vm = new CommandsViewModel(CommandMap.Load(CommandMapTests.MapPath), custom)
        {
            Search = "cola na esquerda",
        };
        Assert.Contains(vm.Groups.SelectMany(g => g.Orders), o => o.Id == "door.stack.left");
    }
```

Acrescente `using RonVoice.Core.Config;` no topo do arquivo.

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter CommandsViewModelTests
```

- [ ] **Step 3: Estender a linha**

Em `RonVoice.App/ViewModels/OrderRowViewModel.cs`, troque a declaração da classe e
acrescente os membros:

```csharp
public sealed class OrderRowViewModel(
    OrderDefinition order, IReadOnlyList<string>? customPhrases = null)
{
    /// <summary>Frases que o usuário acrescentou pelo minhas_frases.json.</summary>
    public IReadOnlyList<string> CustomPhrases => customPhrases ?? [];

    public bool HasCustomPhrases => CustomPhrases.Count > 0;

    public string CustomPhrasesText => string.Join("  ·  ", CustomPhrases);
```

O resto da classe fica como está. O `SearchableText` já cobre as frases próprias, porque
elas foram mescladas no mapa antes de chegar aqui.

- [ ] **Step 4: Estender o view model**

Em `RonVoice.App/ViewModels/CommandsViewModel.cs`, troque o construtor e acrescente:

```csharp
    public CommandsViewModel(
        CommandMap map,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? custom = null,
        IReadOnlyList<PhraseIssue>? issues = null)
    {
        Issues = issues ?? [];

        _all = map.Orders.Values
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => new OrderRowViewModel(
                o, custom is not null && custom.TryGetValue(o.Id, out var c) ? c : null))
            .ToList();
        Groups = Group(_all);
        SendCommand = new RelayCommand(_ => { }, _ => false);
        ReloadCommand = new RelayCommand(_ => { }, _ => false);
    }

    public IReadOnlyList<PhraseIssue> Issues { get; }

    public bool HasIssues => Issues.Count > 0;

    public string IssuesText => string.Join('\n', Issues.Select(i => $"· {i.Message}"));

    /// <summary>Relê o minhas_frases.json sem fechar o app. Ligado na integração.</summary>
    public RelayCommand ReloadCommand { get; set; }
```

Acrescente `using RonVoice.Core.Config;` no topo.

- [ ] **Step 5: Mostrar na tela**

Em `RonVoice.App/Views/CommandsView.xaml`:

1. Dentro do `StackPanel` do topo, antes da caixa de busca, acrescente um painel de avisos
   visível quando `HasIssues`, com fundo `#FFF3CD`, mostrando `IssuesText` e um botão
   **Recarregar** ligado a `ReloadCommand`.
2. Dentro do template de cada linha, depois do `TextBlock` de `PhrasesPtText`, acrescente
   um `TextBlock` ligado a `CustomPhrasesText`, com cor distinta (por exemplo `#2E7D32`) e
   visível apenas quando `HasCustomPhrases` for verdadeiro, usando o mesmo padrão de
   `DataTrigger` que o selo `NeedsVerification` já usa nesse arquivo.

- [ ] **Step 6: Rodar**

```
& $dotnet test --filter CommandsViewModelTests
& $dotnet build
```

Esperado: testes passando, build limpo.

- [ ] **Step 7: Commit**

```bash
git add RonVoice.App RonVoice.Tests/CommandsViewModelTests.cs
git commit -m "feat: show user phrases and refused ones in the catalogue"
```

---

## Task 5: `ChecksViewModel` e a tela de verificação

**Files:**
- Create: `RonVoice.App/ViewModels/ChecksViewModel.cs`, `RonVoice.App/Views/ChecksView.xaml(.cs)`
- Test: `RonVoice.Tests/ChecksViewModelTests.cs`

**Interfaces:**
- Consumes: `CheckResult`, `CheckStatus`, `StartupChecks`.
- Produces: `ChecksViewModel` com `Results`, `Summary`, `Ready`, `Listening`, `Level`, `Show(IReadOnlyList<CheckResult>)`, `BeginMicrophoneTest()`, `RunCommand`.

- [ ] **Step 1: Escrever os testes**

Create `RonVoice.Tests/ChecksViewModelTests.cs`:

```csharp
using RonVoice.App.ViewModels;
using RonVoice.Core.Startup;

namespace RonVoice.Tests;

public class ChecksViewModelTests
{
    static IReadOnlyList<CheckResult> AllOk() => StartupChecks.Run(new(
        Elevated: true, ModelPresent: true, Language: "en",
        MicrophonePeak: 0.5, GameFound: true, InputIniFound: true));

    static IReadOnlyList<CheckResult> WithFailure() => StartupChecks.Run(new(
        Elevated: false, ModelPresent: true, Language: "en",
        MicrophonePeak: 0.5, GameFound: true, InputIniFound: true));

    [Fact]
    public void ShowsEveryCheck()
    {
        var vm = new ChecksViewModel();
        vm.Show(AllOk());
        Assert.Equal(5, vm.Results.Count);
    }

    [Fact]
    public void EverythingOkMeansReady()
    {
        var vm = new ChecksViewModel();
        vm.Show(AllOk());
        Assert.True(vm.Ready);
        Assert.Contains("pronto", vm.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFailureMeansNotReadyAndTheSummarySaysWhy()
    {
        var vm = new ChecksViewModel();
        vm.Show(WithFailure());
        Assert.False(vm.Ready);
        Assert.Contains("administrador", vm.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartingTheMicrophoneTestClearsThePreviousResult()
    {
        var vm = new ChecksViewModel();
        vm.Show(AllOk());
        vm.BeginMicrophoneTest();

        Assert.True(vm.Listening);
        Assert.Empty(vm.Results);
        Assert.Equal(0, vm.Level);
    }

    [Fact]
    public void ShowingAResultStopsTheListening()
    {
        var vm = new ChecksViewModel();
        vm.BeginMicrophoneTest();
        vm.Show(AllOk());
        Assert.False(vm.Listening);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter ChecksViewModelTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.App/ViewModels/ChecksViewModel.cs`:

```csharp
using RonVoice.Core.Startup;

namespace RonVoice.App.ViewModels;

public sealed class ChecksViewModel : ObservableBase
{
    IReadOnlyList<CheckResult> _results = [];
    string _summary = "";
    bool _ready;
    bool _listening;
    double _level;

    public IReadOnlyList<CheckResult> Results
    {
        get => _results;
        private set => Set(ref _results, value);
    }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public bool Ready { get => _ready; private set => Set(ref _ready, value); }
    public bool Listening { get => _listening; private set => Set(ref _listening, value); }
    public double Level { get => _level; set => Set(ref _level, value); }

    /// <summary>Ligado na integração, que é quem grava e monta as entradas.</summary>
    public RelayCommand RunCommand { get; set; } = new(_ => { }, _ => false);

    /// <summary>
    /// A checagem do microfone é a única que exige a pessoa falar, e é a que mais
    /// importa: responde antes do fato a pergunta "ele está me ouvindo?".
    /// </summary>
    public void BeginMicrophoneTest()
    {
        Listening = true;
        Results = [];
        Summary = "Fale alguma coisa...";
        Ready = false;
        Level = 0;
    }

    public void Show(IReadOnlyList<CheckResult> results)
    {
        Listening = false;
        Results = results;
        Summary = StartupChecks.Summarize(results);
        Ready = results.All(r => r.Status != CheckStatus.Failed);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

```
& $dotnet test --filter ChecksViewModelTests
```

Esperado: 5 testes passando.

- [ ] **Step 5: Escrever a view**

Create `RonVoice.App/Views/ChecksView.xaml`. Estrutura exigida:

- botão **Verificar tudo**, ligado a `RunCommand`
- barra de nível ligada a `Level`, visível quando `Listening`, com o texto
  "Fale alguma coisa para eu testar o microfone"
- lista sobre `Results`: por item, o `Name`, o `Message`, e uma marca colorida conforme
  `Status` — verde para `Ok`, âmbar para `Warning`, vermelho para `Failed`
- o `Summary` em destaque no fim, com cor diferente conforme `Ready`

Ligue `ChecksView` no topo da aba Configuração, acima dos campos existentes.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.App RonVoice.Tests/ChecksViewModelTests.cs
git commit -m "feat: show the startup checks with a live microphone test"
```

---

## Task 6: Ligar tudo na sessão

**Files:**
- Modify: `RonVoice.App/RonVoiceSession.cs`, `RonVoice.App/ViewModels/MainViewModel.cs`, `RonVoice.App/Views/SettingsView.xaml`

**Interfaces:**
- Consumes: tudo das tarefas anteriores.
- Produces: as duas funcionalidades funcionando no app.

- [ ] **Step 1: Aplicar as frases próprias ao carregar**

Em `RonVoiceSession`, logo depois de `CommandMap.Load`, antes de construir qualquer coisa
que use o mapa:

```csharp
        var customPath = Path.Combine(
            Path.GetDirectoryName(_settingsPath)!, CustomPhrases.FileName);
        var custom = CustomPhrases.Apply(rawMap, customPath, settings.Language);
        _map = custom.Map;
```

O `_map` mesclado é o que vai para o `PhraseMatcher`, o `GrammarBuilder` e o
`CommandsViewModel`. **A ordem importa:** a gramática precisa ser construída depois da
mesclagem, senão o Vosk nunca ouve as frases novas.

Guarde `custom.Accepted` e `custom.Issues` em campos, para o `CommandsViewModel` e para o
botão Recarregar.

- [ ] **Step 2: Construir o catálogo com as frases próprias**

Troque a construção do `CommandsViewModel`:

```csharp
        _main.Commands = new CommandsViewModel(_map, custom.Accepted, custom.Issues);
```

- [ ] **Step 3: Ligar o Recarregar**

Em `WireCommands`:

```csharp
        _main.Commands.ReloadCommand = new RelayCommand(_ => ReloadCustomPhrases());
```

E o método:

```csharp
    /// <summary>
    /// Relê o arquivo e reconstrói o que depende dele. O reconhecedor precisa ser
    /// recriado porque a gramática é imutável na vida de um VoskRecognizer.
    /// </summary>
    void ReloadCustomPhrases()
    {
        var rawMap = CommandMap.Load(
            Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json"));
        var customPath = Path.Combine(
            Path.GetDirectoryName(_settingsPath)!, CustomPhrases.FileName);
        var custom = CustomPhrases.Apply(rawMap, customPath, _settings.Language);

        _main.Commands = new CommandsViewModel(custom.Map, custom.Accepted, custom.Issues);
        _main.RaiseCommandsChanged();

        MessageBox.Show(
            custom.Issues.Count == 0
                ? "Frases recarregadas. Reabra o RonVoice para o reconhecedor passar a ouvi-las."
                : $"Frases recarregadas com {custom.Issues.Count} aviso(s). "
                  + "Reabra o RonVoice para o reconhecedor passar a ouvi-las.",
            "RonVoice", MessageBoxButton.OK, MessageBoxImage.Information);
    }
```

Em `MainViewModel`, acrescente:

```csharp
    public void RaiseCommandsChanged() => Raise(nameof(Commands));
```

- [ ] **Step 4: Ligar a verificação**

Acrescente `_main.Checks = new ChecksViewModel();` na montagem, uma propriedade
`ChecksViewModel Checks { get; set; } = null!;` em `MainViewModel`, e em `WireCommands`:

```csharp
        _main.Checks.RunCommand = new RelayCommand(_ => _ = RunChecksAsync());
```

E o método, que grava dois segundos para medir o microfone:

```csharp
    async Task RunChecksAsync()
    {
        _main.Checks.BeginMicrophoneTest();

        double peak = 0;
        void Measure(ReadOnlyMemory<byte> chunk)
        {
            var level = AudioLevel.Rms(chunk.Span);
            if (level > peak) peak = level;
            Application.Current.Dispatcher.Invoke(() => _main.Checks.Level = level);
        }

        _capture.OnAudio += Measure;
        await Task.Delay(TimeSpan.FromSeconds(2));
        _capture.OnAudio -= Measure;

        var modelsDir = ModelLocator.FindModelsDirectory();
        var modelPresent = modelsDir is not null && ModelPresent(_settings.Language, modelsDir);

        _main.Checks.Show(StartupChecks.Run(new CheckInputs(
            Elevated: ForegroundGuard.IsElevated(),
            ModelPresent: modelPresent,
            Language: _settings.Language,
            MicrophonePeak: peak,
            GameFound: GameIsRunning() || _settings.GameExecutablePath is not null,
            InputIniFound: KeybindReader.FindDefaultIniPath() is not null)));
    }

    static bool ModelPresent(string language, string modelsDir)
    {
        try { return ModelLocator.LooksLikeAModel(ModelLocator.Find(language, modelsDir)); }
        catch (ModelNotFoundException) { return false; }
    }
```

- [ ] **Step 5: Mostrar na aba Configuração**

Em `RonVoice.App/Views/SettingsView.xaml`, acrescente no topo do `StackPanel`, antes do
bloco "Jogo":

```xml
            <views:ChecksView DataContext="{Binding DataContext.Checks,
                              RelativeSource={RelativeSource AncestorType=Window}}"
                              Margin="0,0,0,20" />
```

- [ ] **Step 6: Verificar**

```
& $dotnet build
& $dotnet test
```

Depois, manualmente:

1. Crie `minhas_frases.json` ao lado do executável com
   `{"door.stack.left": ["cola na esquerda"]}` e abra o app. A ordem `door.stack.left` deve
   aparecer marcada no catálogo com a frase nova.
2. Troque para `{"hold": ["empilha"]}` e clique em Recarregar. Deve aparecer o aviso de
   colisão nomeando `door.stack.auto`, e a frase **não** deve entrar.
3. Troque para `{ isto nao e json` e recarregue. Deve avisar e o app continuar aberto.
4. Clique em **Verificar tudo** e fale. A barra de nível deve reagir, e o resultado listar
   as cinco checagens.

Registre cada item no relatório.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: apply user phrases at startup and wire the guided check"
```

---

## Task 7: Documentar no LEIA-ME

**Files:**
- Modify: `pack/LEIA-ME.txt`

- [ ] **Step 1: Acrescentar a seção**

Acrescente ao `LEIA-ME.txt`, antes da seção "SE NAO FUNCIONAR":

```
SUAS PROPRIAS FRASES
--------------------

Se voce fala diferente do que o programa espera, crie um arquivo chamado
minhas_frases.json nesta pasta:

    {
      "door.open.flashbang": ["manda a bang", "joga a luz e entra"],
      "hold": ["fica quieto"]
    }

A chave e' o nome da ordem, que aparece na aba Comandos. As frases novas
somam com as que ja existem; nada e' removido.

Se uma frase sua ja pertencer a outra ordem, o programa recusa aquela frase e
avisa na aba Comandos qual foi o conflito. Isso e' proposital: duas ordens com
a mesma frase fariam as DUAS pararem de funcionar, sem erro nenhum.

Depois de editar, clique em Recarregar na aba Comandos, e reabra o programa
para o reconhecimento passar a ouvir as frases novas.


ESTA TUDO CERTO?
----------------

Na aba Configuracao, clique em "Verificar tudo". Ele checa elevacao, modelo de
voz, microfone, jogo e teclas, e diz o que falta. A checagem do microfone pede
que voce fale — se a barra nao se mexer, o problema e' o microfone, nao a sua
pronuncia.
```

- [ ] **Step 2: Commit**

```bash
git add pack/LEIA-ME.txt
git commit -m "docs: explain user phrases and the guided check in the readme"
```

---

## Self-Review

**Cobertura da spec:**

| Seção da spec | Onde é implementada |
|---|---|
| 2.1 formato e escopo | Task 1 |
| 2.2 as quatro validações | Task 1 |
| 2.2 colisão pela normalização do matcher | Task 1, `CollisionIgnoresCaseAccentAndPunctuation` |
| 2.3 avisos na tela e frases marcadas | Task 4 |
| 2.3 botão Recarregar | Tasks 4 e 6 |
| 2.4 onde vive | Task 1 |
| 3.1 as cinco checagens | Task 3 |
| 3.2 a do microfone | Tasks 3 e 6 |
| 3.3 quando roda e o botão | Tasks 5 e 6 |
| 3.4 onde vive | Task 3 |
| 4 tratamento de erro | Tasks 1 e 6 |
| 5 testes | Tasks 1, 2, 3, 4, 5 |
| 6 critérios de pronto | Task 6, verificação manual |

Sem lacunas.

**Consistência de tipos:** `PhraseIssue(Kind, OrderId, Phrase, Message)` é criado na Task 1
e consumido igual na 4. `CustomPhraseResult(Map, Issues, Accepted)` idem nas Tasks 1, 4 e 6.
`CheckResult(Name, Status, Message)` e `CheckInputs` da Task 3 são usados nas 5 e 6.
`ObservableBase` e `RelayCommand` vêm da etapa 6 e não mudam.

**Riscos registrados de propósito:**

- **A Task 1 acrescenta `CommandMap.WithOrders`**, o primeiro método que devolve cópia do
  mapa. É deliberado: a mesclagem não pode alterar o mapa no lugar, porque o mapa de
  fábrica precisa continuar disponível para o Recarregar comparar.
- **Recarregar não recria o reconhecedor.** A gramática é imutável na vida de um
  `VoskRecognizer` — é o achado registrado na spec da etapa 5 — então as frases novas só
  passam a ser ouvidas ao reabrir o app. A mensagem diz isso; esconder seria pior.
- **A Task 5 e a Task 6 não têm teste automatizado de tela.** Verificação manual registrada
  no relatório, como nas etapas anteriores.
