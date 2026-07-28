# RonVoice Core (etapas 1–4) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Traduzir uma frase em linguagem natural na sequência exata de teclas do menu de comandos de Ready or Not, e enviá-la ao jogo via `SendInput` com scan codes.

**Architecture:** `RonVoice.Core` é uma biblioteca sem UI, dividida em três camadas independentes: `Matching` (texto → `Intent`), `Commands` (`Intent` + binds do jogo → `KeySequence`) e `Input` (`KeySequence` → Win32). Cada camada é testável isolada; só a última toca em P/Invoke. `RonVoice.Cli` é a ferramenta de depuração e o driver de validação em jogo.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), xUnit, `System.Text.Json`, P/Invoke em `user32.dll`.

**Spec:** `docs/superpowers/specs/2026-07-28-ronvoice-core-design.md` — leia antes de começar. Este plano implementa as decisões dela; se algo aqui divergir da spec, a spec vence e o conflito deve ser levantado, não resolvido em silêncio.

## Global Constraints

- **Windows-only.** TFM `net10.0-windows` em todos os projetos. Requer .NET SDK 10 instalado (`dotnet --list-sdks` deve listar `10.x`).
- **`RonVoice.Core` não referencia WPF nem `System.Windows`.** Sem `<UseWPF>`, sem `<UseWindowsForms>`. Se precisar, o design está errado.
- **Nunca `SendKeys`, `keybd_event` ou mensagens de janela.** Só `SendInput` com `KEYEVENTF_SCANCODE`, `wVk = 0`, scan code em `wScan`.
- **Nenhuma tecla fixa no código.** Todo token resolve por ActionName lido do `Input.ini`, com `keybind_defaults` do JSON como fallback.
- **Nenhum índice, cache ou dicionário pode usar `path` como chave.** A chave é sempre `id`.
- **Resolução incerta não envia nada.** Bind desconhecido vira erro nomeado, nunca uma tecla plausível.
- **Timing:** teclas `key_hold_ms = 35`, `gap_between_keys_ms = 35`; clique do menu hold `100`, gap `menu_open_settle_ms = 60`. Valores vêm do JSON, exceto o hold de 100 ms do mouse, que é constante da spec §2.4.
- **Idiomas:** `en` e `pt`. Stopwords e IDF são **por idioma**, nunca uma lista compartilhada.
- **Parâmetros do matcher:** limiar `0.60`, margem de ambiguidade `0.05`.
- Código, identificadores e mensagens de commit em **inglês**. Documentação em português.

**Referência executável:** `docs/superpowers/specs/prototype/phrase_matcher.py` é o protótipo validado do matcher. A implementação em C# deve reproduzir seus números: 399 frases en e 371 pt resolvendo para a própria ordem, **zero erradas**, cobertura **70/70** por idioma.

---

## File Structure

```
RonVoice.sln
data/ron_commands.json                     movido da raiz

RonVoice.Core/
  RonVoice.Core.csproj
  Commands/
    OrderDefinition.cs      records do mapa: OrderDefinition, ElementDefinition,
                            ModifierDefinition, KeybindDefaults, TimingSettings
    CommandMap.cs           carrega e indexa ron_commands.json por id
    KeybindReader.cs        Input.ini -> ActionName -> nome de tecla UE
    ActionNames.cs          token do path -> ActionName (tabela estática)
    KeyCatalog.cs           nome de tecla UE -> InputToken (tabela estática)
    CommandResolver.cs      Intent + binds -> KeySequence
  Matching/
    Intent.cs               record Intent
    TextNormalizer.cs       normalização e tokenização
    PhraseIndex.cs          catálogo por idioma + pesos IDF + pontuação
    PhraseMatcher.cs        elemento, fila, margem -> Intent?
  Input/
    InputToken.cs           InputToken, ScanCodeToken, MouseToken, MouseButton
    KeyStep.cs              StepKind, KeyStep
    KeySequence.cs          KeySequence
    IInputSender.cs
    SendInputSender.cs      P/Invoke SendInput + espera de alta resolução
    ForegroundGuard.cs      janela em foco e elevação

RonVoice.Cli/
  RonVoice.Cli.csproj
  Program.cs                despacho de subcomandos
  Commands/TestCommand.cs   ronvoice test
  Commands/KeymapCommand.cs ronvoice keymap
  Commands/CorpusCommand.cs ronvoice corpus
  Commands/SendCommand.cs   ronvoice send

RonVoice.Tests/
  RonVoice.Tests.csproj
  CommandMapTests.cs
  KeybindReaderTests.cs
  KeyCatalogTests.cs
  CommandResolverTests.cs
  TextNormalizerTests.cs
  PhraseMatcherTests.cs
  CorpusTests.cs
  fixtures/Input.full.ini      cópia do arquivo real
  fixtures/Input.missing.ini   sem as chaves de SWAT
  fixtures/Input.none.ini      com Key=None
  corpus/en.tsv                gerado, versionado
  corpus/pt.tsv                gerado, versionado
  corpus/adversarial.tsv       escrito à mão
```

Divisão por responsabilidade, não por camada técnica: `PhraseIndex` existe separado de `PhraseMatcher` porque pontuação (IDF, stopwords) e parsing (elemento, fila, margem) mudam por motivos diferentes e cada um cabe confortavelmente em contexto.

---

## Task 1: Solução, projetos e movimentação dos dados

**Files:**
- Create: `RonVoice.sln`, `RonVoice.Core/RonVoice.Core.csproj`, `RonVoice.Cli/RonVoice.Cli.csproj`, `RonVoice.Tests/RonVoice.Tests.csproj`
- Create: `Directory.Build.props`
- Move: `ron_commands.json` → `data/ron_commands.json`

**Interfaces:**
- Consumes: nada
- Produces: os três projetos compilando, `dotnet test` verde, e `data/ron_commands.json` no caminho que todas as tarefas seguintes usam.

- [ ] **Step 1: Verificar o SDK**

```
dotnet --list-sdks
```

Esperado: uma linha começando com `10.`. Se não houver, **pare** — instale com `winget install --id Microsoft.DotNet.SDK.10 -e` num terminal elevado. Nenhuma outra etapa funciona sem isso.

- [ ] **Step 2: Criar solução e projetos**

```
dotnet new sln -n RonVoice
dotnet new classlib -n RonVoice.Core -o RonVoice.Core
dotnet new console  -n RonVoice.Cli  -o RonVoice.Cli
dotnet new xunit    -n RonVoice.Tests -o RonVoice.Tests
dotnet sln add RonVoice.Core RonVoice.Cli RonVoice.Tests
dotnet add RonVoice.Cli   reference RonVoice.Core
dotnet add RonVoice.Tests reference RonVoice.Core
```

Apague os arquivos de template `RonVoice.Core/Class1.cs` e `RonVoice.Tests/UnitTest1.cs`.

- [ ] **Step 3: Fixar TFM e opções comuns**

Create `Directory.Build.props` na raiz:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

Remova a linha `<TargetFramework>` de cada um dos três `.csproj`, para que herdem daqui.

- [ ] **Step 4: Mover o mapa de comandos**

```
mkdir data
git mv ron_commands.json data/ron_commands.json
```

- [ ] **Step 5: Copiar dados e fixtures para a saída dos testes**

Em `RonVoice.Tests/RonVoice.Tests.csproj`, dentro de `<Project>`:

```xml
  <ItemGroup>
    <None Include="../data/ron_commands.json" Link="data/ron_commands.json"
          CopyToOutputDirectory="PreserveNewest" />
    <None Include="fixtures/**" CopyToOutputDirectory="PreserveNewest" />
    <None Include="corpus/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

Crie as pastas `RonVoice.Tests/fixtures/` e `RonVoice.Tests/corpus/` com um `.gitkeep` cada, senão o glob falha.

- [ ] **Step 6: Teste de fumaça — o mapa chega na saída**

Create `RonVoice.Tests/CommandMapTests.cs`:

```csharp
namespace RonVoice.Tests;

public class CommandMapTests
{
    public static string MapPath =>
        Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json");

    [Fact]
    public void MapFileIsCopiedToOutput()
    {
        Assert.True(File.Exists(MapPath), $"não encontrado: {MapPath}");
    }
}
```

- [ ] **Step 7: Rodar**

```
dotnet build
dotnet test
```

Esperado: build sem warnings, 1 teste passando.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution and move command map into data/"
```

---

## Task 2: `CommandMap` — carregar e indexar o mapa

**Files:**
- Create: `RonVoice.Core/Commands/OrderDefinition.cs`
- Create: `RonVoice.Core/Commands/CommandMap.cs`
- Test: `RonVoice.Tests/CommandMapTests.cs` (estender)

**Interfaces:**
- Consumes: `data/ron_commands.json` da Task 1.
- Produces:
  - `record OrderDefinition(string Id, string Context, IReadOnlyList<string> Path, bool CloseMenu, string Confidence, IReadOnlyDictionary<string, IReadOnlyList<string>> Phrases)`
  - `record ElementDefinition(string Name, string Key, IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases)`
  - `record ModifierDefinition(IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases)`
  - `record KeybindDefaults(string SwatCommandMenu, string DefaultCommand, string HoldCommand, string Back, string SelectGold, string SelectBlue, string SelectRed, IReadOnlyList<string> CommandKeys, string InteractYell)`
  - `record TimingSettings(int KeyHoldMs, int GapBetweenKeysMs, int MenuOpenSettleMs)`
  - `CommandMap.Load(string path)` → `CommandMap` com `Orders`, `Elements`, `Queue`, `Defaults`, `Timing`

- [ ] **Step 1: Escrever os testes que falham**

Substitua o conteúdo de `RonVoice.Tests/CommandMapTests.cs`:

```csharp
using RonVoice.Core.Commands;

namespace RonVoice.Tests;

public class CommandMapTests
{
    public static string MapPath =>
        Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json");

    static CommandMap Load() => CommandMap.Load(MapPath);

    [Fact]
    public void MapFileIsCopiedToOutput() =>
        Assert.True(File.Exists(MapPath), $"não encontrado: {MapPath}");

    [Fact]
    public void LoadsSeventyOrders() => Assert.Equal(70, Load().Orders.Count);

    [Fact]
    public void OrderIdsAreUnique()
    {
        // Orders é um dicionário por id; conferimos contra o array cru do JSON
        var raw = System.Text.Json.JsonDocument.Parse(File.ReadAllText(MapPath));
        var ids = raw.RootElement.GetProperty("orders").EnumerateArray()
            .Select(o => o.GetProperty("id").GetString()!).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ReadsAKnownOrder()
    {
        var o = Load().Orders["door.open.flashbang"];
        Assert.Equal("door", o.Context);
        Assert.Equal(new[] { "MENU", "2", "2" }, o.Path);
        Assert.Contains("open with flashbang", o.Phrases["en"]);
        Assert.Contains("abre com flash", o.Phrases["pt"]);
    }

    [Fact]
    public void CloseMenuDefaultsToFalseWhenAbsent() =>
        Assert.False(Load().Orders["door.stack.auto"].CloseMenu);

    [Fact]
    public void ReadsElementsWithKeys()
    {
        var map = Load();
        Assert.Equal("F7", map.Elements["red"].Key);
        Assert.Contains("red team", map.Elements["red"].Aliases["en"]);
        Assert.Contains("team", map.Elements["gold"].Aliases["en"]);
    }

    [Fact]
    public void ReadsQueueModifier() =>
        Assert.Contains("prep", Load().Queue.Aliases["en"]);

    [Fact]
    public void ReadsTimingAndDefaults()
    {
        var map = Load();
        Assert.Equal(35, map.Timing.KeyHoldMs);
        Assert.Equal(35, map.Timing.GapBetweenKeysMs);
        Assert.Equal(60, map.Timing.MenuOpenSettleMs);
        Assert.Equal("MiddleMouse", map.Defaults.SwatCommandMenu);
        Assert.Equal("LeftShift", map.Defaults.HoldCommand);
        Assert.Equal(9, map.Defaults.CommandKeys.Count);
    }

    [Fact]
    public void EveryPathTokenIsWellFormed()
    {
        foreach (var o in Load().Orders.Values)
            foreach (var t in o.Path)
                Assert.True(
                    t == "MENU" || (t.Length == 1 && t[0] >= '1' && t[0] <= '9')
                        || t.StartsWith("KEY:", StringComparison.Ordinal),
                    $"token inesperado {t} em {o.Id}");
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
dotnet test
```

Esperado: erro de compilação — `CommandMap` e `RonVoice.Core.Commands` não existem.

- [ ] **Step 3: Escrever os records**

Create `RonVoice.Core/Commands/OrderDefinition.cs`:

```csharp
namespace RonVoice.Core.Commands;

public sealed record OrderDefinition(
    string Id,
    string Context,
    IReadOnlyList<string> Path,
    bool CloseMenu,
    string Confidence,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Phrases);

public sealed record ElementDefinition(
    string Name,
    string Key,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases);

public sealed record ModifierDefinition(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases);

public sealed record KeybindDefaults(
    string SwatCommandMenu,
    string DefaultCommand,
    string HoldCommand,
    string Back,
    string SelectGold,
    string SelectBlue,
    string SelectRed,
    IReadOnlyList<string> CommandKeys,
    string InteractYell);

public sealed record TimingSettings(
    int KeyHoldMs,
    int GapBetweenKeysMs,
    int MenuOpenSettleMs);
```

- [ ] **Step 4: Escrever o loader**

Create `RonVoice.Core/Commands/CommandMap.cs`:

```csharp
using System.Text.Json;

namespace RonVoice.Core.Commands;

/// <summary>
/// Carrega ron_commands.json. Fonte de verdade única do que o app entende e
/// do que consegue executar. Indexado por id — nunca por path, que é ambíguo.
/// </summary>
public sealed class CommandMap
{
    public IReadOnlyDictionary<string, OrderDefinition> Orders { get; }
    public IReadOnlyDictionary<string, ElementDefinition> Elements { get; }
    public ModifierDefinition Queue { get; }
    public KeybindDefaults Defaults { get; }
    public TimingSettings Timing { get; }

    CommandMap(
        IReadOnlyDictionary<string, OrderDefinition> orders,
        IReadOnlyDictionary<string, ElementDefinition> elements,
        ModifierDefinition queue,
        KeybindDefaults defaults,
        TimingSettings timing)
    {
        Orders = orders;
        Elements = elements;
        Queue = queue;
        Defaults = defaults;
        Timing = timing;
    }

    public static CommandMap Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var orders = new Dictionary<string, OrderDefinition>(StringComparer.Ordinal);
        foreach (var o in root.GetProperty("orders").EnumerateArray())
        {
            var id = o.GetProperty("id").GetString()!;
            if (orders.ContainsKey(id))
                throw new InvalidDataException($"id duplicado no mapa: {id}");

            orders[id] = new OrderDefinition(
                id,
                o.GetProperty("context").GetString()!,
                StringList(o.GetProperty("path")),
                o.TryGetProperty("close_menu", out var cm) && cm.GetBoolean(),
                o.GetProperty("confidence").GetString()!,
                PhraseMap(o.GetProperty("phrases")));
        }

        var elements = new Dictionary<string, ElementDefinition>(StringComparer.Ordinal);
        foreach (var e in root.GetProperty("elements").EnumerateObject())
            elements[e.Name] = new ElementDefinition(
                e.Name, e.Value.GetProperty("key").GetString()!, PhraseMap(e.Value));

        var queue = new ModifierDefinition(
            PhraseMap(root.GetProperty("modifiers").GetProperty("queue")));

        var kd = root.GetProperty("keybind_defaults");
        var defaults = new KeybindDefaults(
            kd.GetProperty("swat_command_menu").GetString()!,
            kd.GetProperty("default_command").GetString()!,
            kd.GetProperty("hold_command").GetString()!,
            kd.GetProperty("back").GetString()!,
            kd.GetProperty("select_gold").GetString()!,
            kd.GetProperty("select_blue").GetString()!,
            kd.GetProperty("select_red").GetString()!,
            StringList(kd.GetProperty("command_keys")),
            kd.GetProperty("interact_yell").GetString()!);

        var t = root.GetProperty("timing");
        var timing = new TimingSettings(
            t.GetProperty("key_hold_ms").GetInt32(),
            t.GetProperty("gap_between_keys_ms").GetInt32(),
            t.GetProperty("menu_open_settle_ms").GetInt32());

        return new CommandMap(orders, elements, queue, defaults, timing);
    }

    static IReadOnlyList<string> StringList(JsonElement arr) =>
        arr.EnumerateArray().Select(x => x.GetString()!).ToArray();

    /// <summary>Lê só as chaves "en" e "pt"; ignora "key", "how", "_nota".</summary>
    static IReadOnlyDictionary<string, IReadOnlyList<string>> PhraseMap(JsonElement obj)
    {
        var d = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var lang in new[] { "en", "pt" })
            if (obj.TryGetProperty(lang, out var v) && v.ValueKind == JsonValueKind.Array)
                d[lang] = StringList(v);
        return d;
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

```
dotnet test
```

Esperado: 9 testes passando.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Commands RonVoice.Tests/CommandMapTests.cs
git commit -m "feat: load and index ron_commands.json by order id"
```

---

## Task 3: Correções de dados no mapa

Implementa as seções 2.7 e 10.1 da spec. Cinco remoções de frases duplicadas e a semente de `close_menu`. Tarefa separada porque é mudança de **dados**, não de código, e um revisor pode aprová-la ou rejeitá-la independente do resto.

**Files:**
- Modify: `data/ron_commands.json`
- Test: `RonVoice.Tests/CommandMapTests.cs` (estender)

**Interfaces:**
- Consumes: `CommandMap.Load` da Task 2.
- Produces: mapa com 399 frases en e 371 pt, e 19 ordens com `close_menu: true`.

- [ ] **Step 1: Escrever os testes que falham**

Acrescente a `RonVoice.Tests/CommandMapTests.cs`:

```csharp
    [Fact]
    public void DuplicatePhrasesWereRemoved()
    {
        var m = Load();
        Assert.DoesNotContain("drop a chemlight", m.Orders["deploy.chemlight"].Phrases["en"]);
        Assert.DoesNotContain("solta a luz",      m.Orders["deploy.chemlight"].Phrases["pt"]);
        Assert.DoesNotContain("para",             m.Orders["player.yell"].Phrases["pt"]);
        Assert.DoesNotContain("go",               m.Orders["move.to"].Phrases["en"]);
        Assert.DoesNotContain("leader leader and clear",
                              m.Orders["door.breach.leader.leader"].Phrases["en"]);
    }

    [Fact]
    public void SurvivingPhrasesAreStillThere()
    {
        var m = Load();
        Assert.Contains("drop chemlight", m.Orders["player.chemlight"].Phrases["en"]);
        Assert.Contains("solta luz",      m.Orders["player.chemlight"].Phrases["pt"]);
        Assert.Contains("para",           m.Orders["hold"].Phrases["pt"]);
        Assert.Contains("go go go",       m.Orders["confirm.default"].Phrases["en"]);
        Assert.Contains("leader and clear",
                        m.Orders["door.breach.leader.clear"].Phrases["en"]);
    }

    [Fact]
    public void PhraseCountsMatchSpec()
    {
        var m = Load();
        Assert.Equal(399, m.Orders.Values.Sum(o => o.Phrases["en"].Count));
        Assert.Equal(371, m.Orders.Values.Sum(o => o.Phrases["pt"].Count));
    }

    [Fact]
    public void NoOrderLosesAllPhrases()
    {
        foreach (var o in Load().Orders.Values)
        {
            Assert.NotEmpty(o.Phrases["en"]);
            Assert.NotEmpty(o.Phrases["pt"]);
        }
    }

    [Fact]
    public void CloseMenuSeedIsExactlyNineteenOrders()
    {
        var expected = new[]
        {
            "cover", "deploy.chemlight", "deploy.flashbang", "deploy.gas",
            "deploy.shield", "deploy.stinger", "door.disarm", "door.open.flashbang",
            "door.open.gas", "door.open.stinger", "door.stack.left",
            "door.stack.right", "door.stack.split", "door.toggle", "door.wedge",
            "move.fallin", "move.to", "person.restrain", "search",
        };
        var actual = Load().Orders.Values.Where(o => o.CloseMenu)
                         .Select(o => o.Id).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), actual);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

```
dotnet test
```

Esperado: os 5 testes novos falham. `PhraseCountsMatchSpec` deve reportar 402 e 373.

- [ ] **Step 3: Aplicar as remoções**

Edite `data/ron_commands.json` à mão. Remova exatamente estas cinco strings dos arrays indicados, sem tocar em mais nada:

| ordem | array | string a remover |
|---|---|---|
| `deploy.chemlight` | `phrases.en` | `"drop a chemlight"` |
| `deploy.chemlight` | `phrases.pt` | `"solta a luz"` |
| `player.yell` | `phrases.pt` | `"para"` |
| `move.to` | `phrases.en` | `"go"` |
| `door.breach.leader.leader` | `phrases.en` | `"leader leader and clear"` |

Cuidado com a vírgula do JSON ao remover o último elemento de um array.

- [ ] **Step 4: Aplicar a semente de `close_menu`**

Acrescente `"close_menu": true` a estas 19 ordens, logo depois da linha `"confidence"`:

```
cover                deploy.chemlight     deploy.flashbang     deploy.gas
deploy.shield        deploy.stinger       door.disarm          door.open.flashbang
door.open.gas        door.open.stinger    door.stack.left      door.stack.right
door.stack.split     door.toggle          door.wedge           move.fallin
move.to              person.restrain      search
```

Não acrescente o campo nas outras 51 — ausente já significa `false`.

- [ ] **Step 5: Rodar e ver passar**

```
dotnet test
```

Esperado: 14 testes passando.

- [ ] **Step 6: Commit**

```bash
git add data/ron_commands.json RonVoice.Tests/CommandMapTests.cs
git commit -m "fix: remove five cross-order duplicate phrases and seed close_menu"
```

---

## Task 4: `KeybindReader` — ler o `Input.ini` do jogo

**Files:**
- Create: `RonVoice.Core/Commands/KeybindReader.cs`
- Create: `RonVoice.Tests/fixtures/Input.full.ini`, `Input.missing.ini`, `Input.none.ini`
- Test: `RonVoice.Tests/KeybindReaderTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `KeybindReader.Read(string path)` → `IReadOnlyDictionary<string,string>` de ActionName para nome de tecla UE
  - `KeybindReader.FindDefaultIniPath()` → `string?`

- [ ] **Step 1: Criar as fixtures**

`RonVoice.Tests/fixtures/Input.full.ini` — copie o arquivo real:

```
copy "%LOCALAPPDATA%\ReadyOrNot\Saved\Config\Windows\Input.ini" RonVoice.Tests\fixtures\Input.full.ini
```

Se o arquivo não existir na máquina, crie-o com este conteúdo mínimo, que cobre tudo que os testes exigem:

```ini
[/Script/Engine.InputSettings]
ActionMappings=(ActionName="Fire",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Gamepad_RightTrigger)
ActionMappings=(ActionName="Fire",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=LeftMouseButton)
ActionMappings=(ActionName="HoldGoCode",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=LeftShift)
ActionMappings=(ActionName="IssueDefaultCommand",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Z)
ActionMappings=(ActionName="OpenSwatCommand",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=MiddleMouseButton)
ActionMappings=(ActionName="SelectElementBlue",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=F6)
ActionMappings=(ActionName="SelectElementGold",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=F5)
ActionMappings=(ActionName="SelectElementRed",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=F7)
ActionMappings=(ActionName="SwatInputKeyOne",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=One)
ActionMappings=(ActionName="SwatInputKeyTwo",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Two)
ActionMappings=(ActionName="SwatInputKeyThree",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Three)
ActionMappings=(ActionName="SwatInputKeyFour",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Four)
ActionMappings=(ActionName="SwatInputKeyFive",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Five)
ActionMappings=(ActionName="SwatInputKeySix",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Six)
ActionMappings=(ActionName="SwatInputKeySeven",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Seven)
ActionMappings=(ActionName="SwatInputKeyEight",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Eight)
ActionMappings=(ActionName="SwatInputKeyNine",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=Nine)
ActionMappings=(ActionName="Use",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=F)
ActionMappings=(ActionName="FireSelect",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=X)
ActionMappings=(ActionName="DropChem",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=C)
ActionMappings=(ActionName="VoteYes",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=PageUp)
ActionMappings=(ActionName="Yell",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=None)
AxisMappings=(AxisName="MoveForward",Scale=1.000000,Key=W)
```

`RonVoice.Tests/fixtures/Input.missing.ini` — só o cabeçalho e uma ação irrelevante:

```ini
[/Script/Engine.InputSettings]
ActionMappings=(ActionName="Chat",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=J)
```

`RonVoice.Tests/fixtures/Input.none.ini`:

```ini
[/Script/Engine.InputSettings]
ActionMappings=(ActionName="Yell",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=None)
ActionMappings=(ActionName="OpenSwatCommand",bShift=False,bCtrl=False,bAlt=False,bCmd=False,Key=MiddleMouseButton)
```

- [ ] **Step 2: Escrever os testes que falham**

Create `RonVoice.Tests/KeybindReaderTests.cs`:

```csharp
using RonVoice.Core.Commands;

namespace RonVoice.Tests;

public class KeybindReaderTests
{
    static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void ReadsSwatBindsFromRealFile()
    {
        var b = KeybindReader.Read(Fixture("Input.full.ini"));
        Assert.Equal("MiddleMouseButton", b["OpenSwatCommand"]);
        Assert.Equal("LeftShift", b["HoldGoCode"]);
        Assert.Equal("Z", b["IssueDefaultCommand"]);
        Assert.Equal("F7", b["SelectElementRed"]);
        Assert.Equal("Two", b["SwatInputKeyTwo"]);
        Assert.Equal("Nine", b["SwatInputKeyNine"]);
    }

    [Fact]
    public void PrefersKeyboardOrMouseOverGamepad() =>
        Assert.Equal("LeftMouseButton", KeybindReader.Read(Fixture("Input.full.ini"))["Fire"]);

    [Fact]
    public void IgnoresAxisMappings() =>
        Assert.False(KeybindReader.Read(Fixture("Input.full.ini")).ContainsKey("MoveForward"));

    [Fact]
    public void OmitsActionsBoundToNone() =>
        Assert.False(KeybindReader.Read(Fixture("Input.none.ini")).ContainsKey("Yell"));

    [Fact]
    public void MissingActionsAreSimplyAbsent() =>
        Assert.False(KeybindReader.Read(Fixture("Input.missing.ini")).ContainsKey("OpenSwatCommand"));

    [Fact]
    public void NonexistentFileYieldsEmptyMap() =>
        Assert.Empty(KeybindReader.Read(Fixture("nao-existe.ini")));
}
```

- [ ] **Step 3: Rodar e ver falhar**

```
dotnet test --filter KeybindReaderTests
```

Esperado: erro de compilação — `KeybindReader` não existe.

- [ ] **Step 4: Implementar**

Create `RonVoice.Core/Commands/KeybindReader.cs`:

```csharp
using System.Text.RegularExpressions;

namespace RonVoice.Core.Commands;

/// <summary>
/// Lê os binds reais do jogo. Devolve ActionName -> nome de tecla UE e nada mais:
/// não conhece ordens, não conhece MENU. Quem junta as pontas é o CommandResolver.
/// </summary>
public static partial class KeybindReader
{
    [GeneratedRegex(
        """^ActionMappings=\(ActionName="(?<action>[^"]+)".*?Key=(?<key>[A-Za-z0-9_]+)""",
        RegexOptions.Compiled)]
    private static partial Regex ActionMappingLine();

    /// <summary>Dispositivos que não nos interessam; ficam de fora do resultado.</summary>
    static readonly string[] NonDesktopPrefixes =
    [
        "Gamepad_", "OculusTouch_", "Vive_", "ValveIndex_", "MixedReality_",
        "MotionController_", "Daydream_", "SteamVR_", "HTC",
    ];

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var binds = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return binds;

        foreach (var line in File.ReadLines(path))
        {
            var m = ActionMappingLine().Match(line);
            if (!m.Success) continue;

            var key = m.Groups["key"].Value;
            if (key == "None") continue;                       // bind vazio: cai no default
            if (NonDesktopPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)))
                continue;

            // Uma ação pode aparecer várias vezes; vence o primeiro bind de desktop.
            binds.TryAdd(m.Groups["action"].Value, key);
        }
        return binds;
    }

    /// <summary>
    /// Windows/ é o caminho do UE5, usado pela versão atual do jogo.
    /// WindowsNoEditor/ é o do UE4, mantido para instalações antigas.
    /// </summary>
    public static string? FindDefaultIniPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        [
            Path.Combine(local, "ReadyOrNot", "Saved", "Config", "Windows", "Input.ini"),
            Path.Combine(local, "ReadyOrNot", "Saved", "Config", "WindowsNoEditor", "Input.ini"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

```
dotnet test --filter KeybindReaderTests
```

Esperado: 6 testes passando.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Commands/KeybindReader.cs RonVoice.Tests/KeybindReaderTests.cs RonVoice.Tests/fixtures
git commit -m "feat: read game keybinds from Input.ini with UE5 and UE4 paths"
```

---

## Task 5: `InputToken` e `KeyCatalog` — nome de tecla UE para scan code

**Files:**
- Create: `RonVoice.Core/Input/InputToken.cs`
- Create: `RonVoice.Core/Commands/KeyCatalog.cs`
- Test: `RonVoice.Tests/KeyCatalogTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `abstract record InputToken`, `record ScanCodeToken(ushort Scan, bool Extended) : InputToken`, `record MouseToken(MouseButton Button) : InputToken`, `enum MouseButton { Left, Right, Middle, X1, X2 }`
  - `KeyCatalog.TryResolve(string ueKeyName, out InputToken token)` → `bool`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/KeyCatalogTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e ver falhar**

```
dotnet test --filter KeyCatalogTests
```

Esperado: erro de compilação.

- [ ] **Step 3: Escrever os tipos de token**

Create `RonVoice.Core/Input/InputToken.cs`:

```csharp
namespace RonVoice.Core.Input;

public enum MouseButton { Left, Right, Middle, X1, X2 }

/// <summary>
/// Tecla e botão de mouse são tipos distintos de propósito: geram estruturas
/// INPUT diferentes. Colapsar os dois num ushort é a forma silenciosa de
/// mandar input que o jogo ignora.
/// </summary>
public abstract record InputToken;

/// <param name="Scan">Scan code do conjunto 1.</param>
/// <param name="Extended">Precisa do prefixo E0.</param>
public sealed record ScanCodeToken(ushort Scan, bool Extended) : InputToken;

public sealed record MouseToken(MouseButton Button) : InputToken;
```

- [ ] **Step 4: Escrever o catálogo**

Create `RonVoice.Core/Commands/KeyCatalog.cs`:

```csharp
using RonVoice.Core.Input;

namespace RonVoice.Core.Commands;

/// <summary>
/// Nome de tecla do Unreal para scan code do conjunto 1. Nome desconhecido
/// devolve false: a ordem é rejeitada em vez de virar uma tecla plausível.
/// </summary>
public static class KeyCatalog
{
    static ScanCodeToken K(int scan) => new((ushort)scan, false);
    static ScanCodeToken E(int scan) => new((ushort)scan, true);

    static readonly Dictionary<string, InputToken> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // linha de dígitos
        ["One"] = K(0x02), ["Two"] = K(0x03), ["Three"] = K(0x04), ["Four"] = K(0x05),
        ["Five"] = K(0x06), ["Six"] = K(0x07), ["Seven"] = K(0x08), ["Eight"] = K(0x09),
        ["Nine"] = K(0x0A), ["Zero"] = K(0x0B),
        ["Hyphen"] = K(0x0C), ["Equals"] = K(0x0D),

        // letras
        ["Q"] = K(0x10), ["W"] = K(0x11), ["E"] = K(0x12), ["R"] = K(0x13),
        ["T"] = K(0x14), ["Y"] = K(0x15), ["U"] = K(0x16), ["I"] = K(0x17),
        ["O"] = K(0x18), ["P"] = K(0x19),
        ["A"] = K(0x1E), ["S"] = K(0x1F), ["D"] = K(0x20), ["F"] = K(0x21),
        ["G"] = K(0x22), ["H"] = K(0x23), ["J"] = K(0x24), ["K"] = K(0x25),
        ["L"] = K(0x26),
        ["Z"] = K(0x2C), ["X"] = K(0x2D), ["C"] = K(0x2E), ["V"] = K(0x2F),
        ["B"] = K(0x30), ["N"] = K(0x31), ["M"] = K(0x32),

        // pontuação
        ["LeftBracket"] = K(0x1A), ["RightBracket"] = K(0x1B),
        ["Semicolon"] = K(0x27), ["Apostrophe"] = K(0x28), ["Tilde"] = K(0x29),
        ["Backslash"] = K(0x2B), ["Comma"] = K(0x33), ["Period"] = K(0x34),
        ["Slash"] = K(0x35),

        // controle
        ["Escape"] = K(0x01), ["BackSpace"] = K(0x0E), ["Tab"] = K(0x0F),
        ["Enter"] = K(0x1C), ["LeftControl"] = K(0x1D), ["LeftShift"] = K(0x2A),
        ["RightShift"] = K(0x36), ["LeftAlt"] = K(0x38), ["SpaceBar"] = K(0x39),
        ["CapsLock"] = K(0x3A), ["NumLock"] = K(0x45), ["ScrollLock"] = K(0x46),

        // função
        ["F1"] = K(0x3B), ["F2"] = K(0x3C), ["F3"] = K(0x3D), ["F4"] = K(0x3E),
        ["F5"] = K(0x3F), ["F6"] = K(0x40), ["F7"] = K(0x41), ["F8"] = K(0x42),
        ["F9"] = K(0x43), ["F10"] = K(0x44), ["F11"] = K(0x57), ["F12"] = K(0x58),

        // numpad — não são estendidas, exceto Divide e Enter
        ["NumPadSeven"] = K(0x47), ["NumPadEight"] = K(0x48), ["NumPadNine"] = K(0x49),
        ["Subtract"] = K(0x4A),
        ["NumPadFour"] = K(0x4B), ["NumPadFive"] = K(0x4C), ["NumPadSix"] = K(0x4D),
        ["Add"] = K(0x4E),
        ["NumPadOne"] = K(0x4F), ["NumPadTwo"] = K(0x50), ["NumPadThree"] = K(0x51),
        ["NumPadZero"] = K(0x52), ["Decimal"] = K(0x53), ["Multiply"] = K(0x37),

        // estendidas: prefixo E0
        ["RightControl"] = E(0x1D), ["RightAlt"] = E(0x38),
        ["Divide"] = E(0x35), ["NumPadEnter"] = E(0x1C),
        ["Home"] = E(0x47), ["Up"] = E(0x48), ["PageUp"] = E(0x49),
        ["Left"] = E(0x4B), ["Right"] = E(0x4D),
        ["End"] = E(0x4F), ["Down"] = E(0x50), ["PageDown"] = E(0x51),
        ["Insert"] = E(0x52), ["Delete"] = E(0x53),

        // mouse. "MiddleMouse" é a grafia usada em keybind_defaults do JSON.
        ["LeftMouseButton"] = new MouseToken(MouseButton.Left),
        ["RightMouseButton"] = new MouseToken(MouseButton.Right),
        ["MiddleMouseButton"] = new MouseToken(MouseButton.Middle),
        ["MiddleMouse"] = new MouseToken(MouseButton.Middle),
        ["ThumbMouseButton"] = new MouseToken(MouseButton.X1),
        ["ThumbMouseButton2"] = new MouseToken(MouseButton.X2),
    };

    public static bool TryResolve(string ueKeyName, out InputToken token) =>
        Map.TryGetValue(ueKeyName, out token!);
}
```

- [ ] **Step 5: Rodar e ver passar**

```
dotnet test --filter KeyCatalogTests
```

Esperado: todos passando.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Input/InputToken.cs RonVoice.Core/Commands/KeyCatalog.cs RonVoice.Tests/KeyCatalogTests.cs
git commit -m "feat: map Unreal key names to set-1 scan codes and mouse buttons"
```

---

## Task 6: `ActionNames`, `KeySequence` e `CommandResolver`

**Files:**
- Create: `RonVoice.Core/Input/KeyStep.cs`, `RonVoice.Core/Input/KeySequence.cs`
- Create: `RonVoice.Core/Matching/Intent.cs`
- Create: `RonVoice.Core/Commands/ActionNames.cs`
- Create: `RonVoice.Core/Commands/CommandResolver.cs`
- Test: `RonVoice.Tests/CommandResolverTests.cs`

**Interfaces:**
- Consumes: `CommandMap` (Task 2), `KeybindReader.Read` (Task 4), `KeyCatalog.TryResolve` (Task 5).
- Produces:
  - `record Intent(string? Element, string? OrderId, bool Queue)`
  - `enum StepKind { Press, Down, Up }`, `record KeyStep(StepKind Kind, InputToken Token, int HoldMs, int GapAfterMs)`, `record KeySequence(IReadOnlyList<KeyStep> Steps)`
  - `CommandResolver(CommandMap map, IReadOnlyDictionary<string,string> binds)` com `Resolve(Intent)` → `KeySequence`, lançando `ResolveException`
  - `const int CommandResolver.MouseHoldMs = 100`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/CommandResolverTests.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class CommandResolverTests
{
    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IReadOnlyDictionary<string, string> Binds() => KeybindReader.Read(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.full.ini"));

    static CommandResolver Resolver(IReadOnlyDictionary<string, string>? binds = null) =>
        new(Map(), binds ?? Binds());

    static readonly InputToken Mmb = new MouseToken(MouseButton.Middle);
    static InputToken Sc(int s) => new ScanCodeToken((ushort)s, false);

    [Fact]
    public void ElementOnlySendsJustTheSelectionKey()
    {
        var seq = Resolver().Resolve(new Intent("red", null, false));
        Assert.Collection(seq.Steps,
            s => Assert.Equal(new KeyStep(StepKind.Press, Sc(0x41), 35, 35), s));
    }

    [Fact]
    public void OrderWithoutElementSkipsTheSelectionKey()
    {
        // door.stack.left = MENU 1 2
        var seq = Resolver().Resolve(new Intent(null, "door.stack.left", false));
        Assert.Equal(
            new[]
            {
                new KeyStep(StepKind.Press, Mmb,      100, 60),
                new KeyStep(StepKind.Press, Sc(0x02),  35, 35),
                new KeyStep(StepKind.Press, Sc(0x03),  35, 35),
            },
            seq.Steps);
    }

    [Fact]
    public void QueuedOrderWrapsOnlyTheLastKeyAndClosesTheMenu()
    {
        // door.open.flashbang = MENU 2 2, close_menu: true
        var seq = Resolver().Resolve(new Intent("red", "door.open.flashbang", true));
        Assert.Equal(
            new[]
            {
                new KeyStep(StepKind.Press, Sc(0x41),  35, 35),   // F7
                new KeyStep(StepKind.Press, Mmb,      100, 60),   // abre
                new KeyStep(StepKind.Press, Sc(0x03),  35, 35),   // 2
                new KeyStep(StepKind.Down,  Sc(0x2A),   0, 0),    // LShift
                new KeyStep(StepKind.Press, Sc(0x03),  35, 35),   // 2, envolvida
                new KeyStep(StepKind.Up,    Sc(0x2A),   0, 0),
                new KeyStep(StepKind.Press, Mmb,      100, 0),    // fecha
            },
            seq.Steps);
    }

    [Fact]
    public void QueuedOrderWithoutCloseMenuDoesNotClose()
    {
        // door.stack.auto = MENU 1 4, sem close_menu
        var seq = Resolver().Resolve(new Intent(null, "door.stack.auto", true));
        Assert.Equal(StepKind.Up, seq.Steps[^1].Kind);
        Assert.Equal(4, seq.Steps.Count);
    }

    [Fact]
    public void UnqueuedOrderNeverClosesTheMenuEvenWhenFlagged()
    {
        var seq = Resolver().Resolve(new Intent(null, "door.open.flashbang", false));
        Assert.Equal(3, seq.Steps.Count);
        Assert.DoesNotContain(seq.Steps.Skip(1), s => Equals(s.Token, Mmb));
    }

    [Fact]
    public void ResolvesDirectKeyTokens()
    {
        // player.fireselect = KEY:X -> FireSelect -> X
        var seq = Resolver().Resolve(new Intent(null, "player.fireselect", false));
        Assert.Collection(seq.Steps,
            s => Assert.Equal(new KeyStep(StepKind.Press, Sc(0x2D), 35, 35), s));
    }

    [Fact]
    public void FallsBackToDefaultsWhenBindIsAbsent()
    {
        // Input.missing.ini não tem OpenSwatCommand; keybind_defaults diz MiddleMouse
        var binds = KeybindReader.Read(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "Input.missing.ini"));
        var seq = new CommandResolver(Map(), binds)
            .Resolve(new Intent(null, "door.stack.left", false));
        Assert.Equal(Mmb, seq.Steps[0].Token);
    }

    [Fact]
    public void ThrowsNamingTheActionWhenNothingResolves()
    {
        var map = Map();
        var broken = map.Defaults with { CommandKeys = ["Xyzzy", "Two", "Three", "Four",
                                                        "Five", "Six", "Seven", "Eight", "Nine"] };
        var resolver = new CommandResolver(map, new Dictionary<string, string>(), broken);
        var ex = Assert.Throws<ResolveException>(
            () => resolver.Resolve(new Intent(null, "door.stack.left", false)));
        Assert.Contains("Xyzzy", ex.Message);
    }

    [Fact]
    public void ThrowsOnUnknownOrderId() =>
        Assert.Throws<ResolveException>(
            () => Resolver().Resolve(new Intent(null, "nao.existe", false)));

    [Fact]
    public void EveryOrderInTheMapResolves()
    {
        var r = Resolver();
        foreach (var id in Map().Orders.Keys)
            _ = r.Resolve(new Intent(null, id, false));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
dotnet test --filter CommandResolverTests
```

Esperado: erro de compilação.

- [ ] **Step 3: Escrever os tipos de sequência e o `Intent`**

Create `RonVoice.Core/Input/KeyStep.cs`:

```csharp
namespace RonVoice.Core.Input;

public enum StepKind { Press, Down, Up }

/// <param name="HoldMs">Tempo entre o down e o up. Ignorado quando Kind != Press.</param>
/// <param name="GapAfterMs">Espera depois do passo, antes do próximo.</param>
public sealed record KeyStep(StepKind Kind, InputToken Token, int HoldMs, int GapAfterMs);
```

Create `RonVoice.Core/Input/KeySequence.cs`:

```csharp
namespace RonVoice.Core.Input;

/// <summary>
/// Dado puro: carrega o tempo, não o executa. É o que torna a regra de hold
/// de 35 ms testável sem tocar em Win32.
/// </summary>
public sealed record KeySequence(IReadOnlyList<KeyStep> Steps);
```

Create `RonVoice.Core/Matching/Intent.cs`:

```csharp
namespace RonVoice.Core.Matching;

/// <summary>
/// Element e OrderId nunca são ambos nulos. Só Element é válido e manda apenas
/// a tecla de seleção — é o que faz "red team" dito sozinho funcionar.
/// </summary>
public sealed record Intent(string? Element, string? OrderId, bool Queue);
```

- [ ] **Step 4: Escrever a tabela de ActionNames**

Create `RonVoice.Core/Commands/ActionNames.cs`:

```csharp
namespace RonVoice.Core.Commands;

/// <summary>
/// Token do mapa para ActionName do Ready or Not. Existe para que nem os
/// dígitos do menu fiquem fixos no código: eles são ações rebindáveis
/// (SwatInputKeyOne..Nine) como qualquer outra.
/// </summary>
public static class ActionNames
{
    public const string OpenSwatCommand = "OpenSwatCommand";
    public const string HoldGoCode = "HoldGoCode";

    static readonly string[] DigitWords =
        ["One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"];

    /// <summary>'1'..'9' -> "SwatInputKeyOne".."SwatInputKeyNine".</summary>
    public static string ForDigit(char digit) =>
        "SwatInputKey" + DigitWords[digit - '1'];

    public static string ForElement(string element) => element switch
    {
        "gold" => "SelectElementGold",
        "blue" => "SelectElementBlue",
        "red" => "SelectElementRed",
        _ => throw new ArgumentOutOfRangeException(nameof(element), element, "elemento desconhecido"),
    };

    /// <summary>
    /// "KEY:NOME" -> ActionName, quando existe um. Devolve null para tokens que
    /// são nome de tecla literal e vão direto ao KeyCatalog.
    /// </summary>
    public static string? ForKeyToken(string token) => token switch
    {
        "KEY:DEFAULT_COMMAND" => "IssueDefaultCommand",
        "KEY:INTERACT" => "Use",
        "KEY:X" => "FireSelect",
        "KEY:C" => "DropChem",
        "KEY:PAGEUP" => "VoteYes",
        _ => null,
    };
}
```

- [ ] **Step 5: Escrever o resolver**

Create `RonVoice.Core/Commands/CommandResolver.cs`:

```csharp
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Commands;

public sealed class ResolveException(string message) : Exception(message);

/// <summary>
/// Intent + binds do jogo -> KeySequence. Quando a resolução é incerta, lança:
/// nunca inventa uma tecla plausível.
/// </summary>
public sealed class CommandResolver
{
    /// <summary>
    /// O perfil VoiceAttack original segura o clique do meio por 0.1s, contra
    /// 0.033s das teclas. Constante, não vem do JSON.
    /// </summary>
    public const int MouseHoldMs = 100;

    readonly CommandMap _map;
    readonly IReadOnlyDictionary<string, string> _binds;
    readonly KeybindDefaults _defaults;

    public CommandResolver(
        CommandMap map,
        IReadOnlyDictionary<string, string> binds,
        KeybindDefaults? defaults = null)
    {
        _map = map;
        _binds = binds;
        _defaults = defaults ?? map.Defaults;
    }

    public KeySequence Resolve(Intent intent)
    {
        var steps = new List<KeyStep>();
        var hold = _map.Timing.KeyHoldMs;
        var gap = _map.Timing.GapBetweenKeysMs;

        if (intent.Element is { } element)
            steps.Add(new KeyStep(
                StepKind.Press, ResolveElement(element), hold, gap));

        if (intent.OrderId is not { } orderId)
        {
            if (steps.Count == 0)
                throw new ResolveException("intent vazio: sem elemento e sem ordem");
            return new KeySequence(steps);
        }

        if (!_map.Orders.TryGetValue(orderId, out var order))
            throw new ResolveException($"ordem desconhecida: {orderId}");

        for (var i = 0; i < order.Path.Count; i++)
        {
            var token = ResolvePathToken(order.Path[i]);
            var isLast = i == order.Path.Count - 1;
            var isMenu = token is MouseToken;

            if (isLast && intent.Queue)
            {
                var shift = ResolveAction(ActionNames.HoldGoCode, _defaults.HoldCommand);
                steps.Add(new KeyStep(StepKind.Down, shift, 0, 0));
                steps.Add(new KeyStep(StepKind.Press, token, hold, gap));
                steps.Add(new KeyStep(StepKind.Up, shift, 0, 0));
            }
            else
            {
                steps.Add(new KeyStep(
                    StepKind.Press, token,
                    isMenu ? MouseHoldMs : hold,
                    isMenu ? _map.Timing.MenuOpenSettleMs : gap));
            }
        }

        // O clique de fechamento pertence ao modificador de fila, não à ordem.
        if (intent.Queue && order.CloseMenu)
            steps.Add(new KeyStep(
                StepKind.Press,
                ResolveAction(ActionNames.OpenSwatCommand, _defaults.SwatCommandMenu),
                MouseHoldMs, 0));

        // O último passo não precisa de espera depois dele.
        steps[^1] = steps[^1] with { GapAfterMs = 0 };
        return new KeySequence(steps);
    }

    InputToken ResolveElement(string element)
    {
        var fallback = element switch
        {
            "gold" => _defaults.SelectGold,
            "blue" => _defaults.SelectBlue,
            "red" => _defaults.SelectRed,
            _ => throw new ResolveException($"elemento desconhecido: {element}"),
        };
        return ResolveAction(ActionNames.ForElement(element), fallback);
    }

    InputToken ResolvePathToken(string token)
    {
        if (token == "MENU")
            return ResolveAction(ActionNames.OpenSwatCommand, _defaults.SwatCommandMenu);

        if (token.Length == 1 && token[0] is >= '1' and <= '9')
            return ResolveAction(
                ActionNames.ForDigit(token[0]),
                _defaults.CommandKeys[token[0] - '1']);

        if (token.StartsWith("KEY:", StringComparison.Ordinal))
        {
            var action = ActionNames.ForKeyToken(token);
            var literal = token["KEY:".Length..];
            var fallback = token switch
            {
                "KEY:DEFAULT_COMMAND" => _defaults.DefaultCommand,
                "KEY:INTERACT" => _defaults.InteractYell,
                _ => literal,
            };
            return action is null ? ResolveKeyName(fallback) : ResolveAction(action, fallback);
        }

        throw new ResolveException($"token de path desconhecido: {token}");
    }

    /// <summary>Bind real do jogo; se ausente ou irreconhecível, o default do mapa.</summary>
    InputToken ResolveAction(string action, string fallbackKeyName)
    {
        if (_binds.TryGetValue(action, out var bound)
            && KeyCatalog.TryResolve(bound, out var token))
            return token;
        return ResolveKeyName(fallbackKeyName);
    }

    static InputToken ResolveKeyName(string keyName) =>
        KeyCatalog.TryResolve(keyName, out var token)
            ? token
            : throw new ResolveException($"nome de tecla desconhecido: {keyName}");
}
```

- [ ] **Step 6: Rodar e ver passar**

```
dotnet test --filter CommandResolverTests
```

Esperado: 10 testes passando. Se `QueuedOrderWithoutCloseMenuDoesNotClose` falhar com 5 passos em vez de 4, confira se `door.stack.auto` recebeu `close_menu` por engano na Task 3.

- [ ] **Step 7: Commit**

```bash
git add RonVoice.Core RonVoice.Tests/CommandResolverTests.cs
git commit -m "feat: resolve intents into timed key sequences via game actions"
```

---

## Task 7: `TextNormalizer`

**Files:**
- Create: `RonVoice.Core/Matching/TextNormalizer.cs`
- Test: `RonVoice.Tests/TextNormalizerTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `TextNormalizer.Tokenize(string text)` → `IReadOnlyList<string>`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/TextNormalizerTests.cs`:

```csharp
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void LowercasesAndSplits() =>
        Assert.Equal(new[] { "stack", "up" }, TextNormalizer.Tokenize("Stack Up"));

    [Fact]
    public void StripsPunctuation() =>
        Assert.Equal(new[] { "red", "team", "open", "the", "door" },
                     TextNormalizer.Tokenize("Red team, open the door!"));

    [Fact]
    public void StripsDiacritics() =>
        Assert.Equal(new[] { "posicao", "a", "esquerda" },
                     TextNormalizer.Tokenize("posição à esquerda"));

    [Fact]
    public void CollapsesWhitespace() =>
        Assert.Equal(new[] { "a", "b" }, TextNormalizer.Tokenize("  a \t\n  b  "));

    [Fact]
    public void KeepsDigits() =>
        Assert.Equal(new[] { "c2", "and", "clear" }, TextNormalizer.Tokenize("C2 and clear"));

    [Fact]
    public void EmptyInputYieldsEmptyList() =>
        Assert.Empty(TextNormalizer.Tokenize("  ,.!  "));
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
dotnet test --filter TextNormalizerTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.Core/Matching/TextNormalizer.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace RonVoice.Core.Matching;

public static class TextNormalizer
{
    /// <summary>
    /// Minúsculas, sem diacríticos, sem pontuação, espaços colapsados.
    /// "Red team, open the door!" -> ["red","team","open","the","door"]
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : ' ');
        }

        return sb.ToString()
                 .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

```
dotnet test --filter TextNormalizerTests
```

Esperado: 6 testes passando.

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Matching/TextNormalizer.cs RonVoice.Tests/TextNormalizerTests.cs
git commit -m "feat: normalize utterances to accent-free lowercase tokens"
```

---

## Task 8: `PhraseIndex` — catálogo, IDF e pontuação

**Files:**
- Create: `RonVoice.Core/Matching/PhraseIndex.cs`
- Test: `RonVoice.Tests/PhraseIndexTests.cs`

**Interfaces:**
- Consumes: `CommandMap` (Task 2), `TextNormalizer.Tokenize` (Task 7).
- Produces:
  - `PhraseIndex(CommandMap map, string language)`
  - `record ScoredPhrase(double Score, string OrderId, string Phrase)`
  - `PhraseIndex.Rank(IReadOnlyList<string> tokens)` → `IReadOnlyList<ScoredPhrase>` ordenado desc
  - `PhraseIndex.Score(IReadOnlyList<string> a, IReadOnlyList<string> b)` → `double`

**Fórmula** (paridade exata com `docs/superpowers/specs/prototype/phrase_matcher.py`):

```
df(t)   = em quantas frases do idioma t aparece, contando cada frase uma vez,
          já sem stopwords
peso(t) = log(1 + N / (1 + df(t)))      N = número de frases do idioma
peso(t) = log(1 + N)                    para token ausente do catálogo
filtra(S) = S menos as stopwords do idioma; se ficar vazio, S cru
score(A,B) = 2 · Σpeso(filtra(A) ∩ filtra(B)) / (Σpeso(filtra(A)) + Σpeso(filtra(B)))
```

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/PhraseIndexTests.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class PhraseIndexTests
{
    static PhraseIndex Index(string lang) =>
        new(CommandMap.Load(CommandMapTests.MapPath), lang);

    static IReadOnlyList<string> T(string s) => TextNormalizer.Tokenize(s);

    [Fact]
    public void IdenticalPhrasesScoreOne() =>
        Assert.Equal(1.0, Index("en").Score(T("stack left"), T("stack left")), 6);

    [Fact]
    public void DisjointPhrasesScoreZero() =>
        Assert.Equal(0.0, Index("en").Score(T("banana pudding"), T("stack left")), 6);

    [Fact]
    public void RareTokensOutweighCommonOnes()
    {
        // "flashbang" discrimina; "door" não. É o que desempata o caso 1 do brief.
        var idx = Index("en");
        var input = T("open the door with flashbang");
        var flash = idx.Score(input, T("open with flashbang"));
        var toggle = idx.Score(input, T("open the door"));
        Assert.True(flash > toggle, $"esperado flashbang > toggle, veio {flash} vs {toggle}");
        Assert.True(flash - toggle >= 0.05, $"margem insuficiente: {flash - toggle}");
    }

    [Fact]
    public void StopwordsAreLanguageSpecific()
    {
        // "do" é artigo em pt e verbo em en. Uma lista compartilhada zeraria "do it".
        Assert.Equal(1.0, Index("en").Score(T("do it"), T("do it")), 6);
    }

    [Fact]
    public void AllStopwordPhraseFallsBackToRawTokens() =>
        Assert.Equal(1.0, Index("en").Score(T("the a and"), T("the a and")), 6);

    [Fact]
    public void RankReturnsBestFirst()
    {
        var top = Index("en").Rank(T("open the door with flashbang"))[0];
        Assert.Equal("door.open.flashbang", top.OrderId);
    }

    [Fact]
    public void RankOnlyContainsPhrasesOfItsLanguage()
    {
        var ptIds = Index("pt").Rank(T("abre com flash")).Select(r => r.Phrase);
        Assert.DoesNotContain("open with flashbang", ptIds);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
dotnet test --filter PhraseIndexTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.Core/Matching/PhraseIndex.cs`:

```csharp
using RonVoice.Core.Commands;

namespace RonVoice.Core.Matching;

public sealed record ScoredPhrase(double Score, string OrderId, string Phrase);

/// <summary>
/// Catálogo de frases de um idioma, com pesos IDF. Sobreposição simples de
/// tokens não separa "open the door" de "open with flashbang" — pesar cada
/// token pelo inverso da frequência separa.
/// </summary>
public sealed class PhraseIndex
{
    /// <summary>
    /// Por idioma, nunca compartilhadas: "do" é artigo em pt e verbo em en.
    /// "with"/"com" ficam de fora de propósito — são o que distingue
    /// "open with flashbang" de "open the door".
    /// </summary>
    static readonly IReadOnlyDictionary<string, HashSet<string>> Stopwords =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["en"] = new(StringComparer.Ordinal)
                { "the", "a", "an", "and", "to", "of", "on", "it", "that", "for" },
            ["pt"] = new(StringComparer.Ordinal)
                { "o", "a", "os", "as", "e", "de", "do", "da", "no", "na", "um", "uma", "que" },
        };

    readonly HashSet<string> _stop;
    readonly Dictionary<string, double> _idf = new(StringComparer.Ordinal);
    readonly double _defaultIdf;
    readonly List<(string OrderId, string Raw, HashSet<string> Tokens)> _phrases = [];

    public string Language { get; }

    public PhraseIndex(CommandMap map, string language)
    {
        Language = language;
        _stop = Stopwords.TryGetValue(language, out var s) ? s : new HashSet<string>(StringComparer.Ordinal);

        foreach (var order in map.Orders.Values)
        {
            if (!order.Phrases.TryGetValue(language, out var list)) continue;
            foreach (var raw in list)
                _phrases.Add((order.Id, raw, [.. TextNormalizer.Tokenize(raw)]));
        }

        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, _, tokens) in _phrases)
            foreach (var t in tokens)
                if (!_stop.Contains(t))
                    df[t] = df.GetValueOrDefault(t) + 1;

        var n = _phrases.Count;
        _defaultIdf = Math.Log(1 + n);
        foreach (var (t, c) in df)
            _idf[t] = Math.Log(1 + (double)n / (1 + c));
    }

    public double Score(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        Score(Filter(a), Filter(b));

    public IReadOnlyList<ScoredPhrase> Rank(IReadOnlyList<string> tokens)
    {
        var a = Filter(tokens);
        var results = new List<ScoredPhrase>(_phrases.Count);
        foreach (var (orderId, raw, phraseTokens) in _phrases)
            results.Add(new ScoredPhrase(Score(a, Filter(phraseTokens)), orderId, raw));
        results.Sort((x, y) => y.Score.CompareTo(x.Score));
        return results;
    }

    /// <summary>Remove stopwords; se sobrar nada, devolve o conjunto cru.</summary>
    HashSet<string> Filter(IEnumerable<string> tokens)
    {
        var all = new HashSet<string>(tokens, StringComparer.Ordinal);
        var kept = new HashSet<string>(all, StringComparer.Ordinal);
        kept.ExceptWith(_stop);
        return kept.Count > 0 ? kept : all;
    }

    double Score(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;

        double intersection = 0, weightA = 0, weightB = 0;
        foreach (var t in a)
        {
            var w = Weight(t);
            weightA += w;
            if (b.Contains(t)) intersection += w;
        }
        foreach (var t in b) weightB += Weight(t);

        return 2 * intersection / (weightA + weightB);
    }

    double Weight(string token) => _idf.GetValueOrDefault(token, _defaultIdf);
}
```

- [ ] **Step 4: Rodar e ver passar**

```
dotnet test --filter PhraseIndexTests
```

Esperado: 7 testes passando.

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Matching/PhraseIndex.cs RonVoice.Tests/PhraseIndexTests.cs
git commit -m "feat: score phrases with IDF-weighted F1 and per-language stopwords"
```

---

## Task 9: `PhraseMatcher` — elemento, fila e portão de ambiguidade

**Files:**
- Create: `RonVoice.Core/Matching/PhraseMatcher.cs`
- Test: `RonVoice.Tests/PhraseMatcherTests.cs`

**Interfaces:**
- Consumes: `CommandMap` (Task 2), `PhraseIndex` (Task 8), `Intent` (Task 6).
- Produces:
  - `record MatcherOptions(double Threshold = 0.60, double Margin = 0.05)`
  - `PhraseMatcher(CommandMap map, string language, MatcherOptions? options = null)`
  - `PhraseMatcher.Match(string utterance)` → `Intent?`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/PhraseMatcherTests.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class PhraseMatcherTests
{
    static PhraseMatcher M(string lang = "en") =>
        new(CommandMap.Load(CommandMapTests.MapPath), lang);

    [Theory]
    // Os seis casos da §8 do brief, com o caso 5 corrigido pela §2.6 da spec.
    [InlineData("red team, open the door with flashbang", "door.open.flashbang", "red", false)]
    [InlineData("open the door with flashbang", "door.open.flashbang", null, false)]
    [InlineData("red team", null, "red", false)]
    [InlineData("stack up left", "door.stack.left", null, false)]
    [InlineData("blue team prep breach and clear", "door.breach.leader.clear", "blue", true)]
    // Colisão team/red team: casamento mais longo primeiro.
    [InlineData("team", null, "gold", false)]
    // Colisão hold: alias de fila e frase de ordem ao mesmo tempo.
    [InlineData("hold", "hold", null, false)]
    [InlineData("hold position", "hold", null, false)]
    [InlineData("gold team hold", "hold", "gold", false)]
    [InlineData("red team hold up", "hold", "red", false)]
    // Stopwords por idioma: "do" não pode ser removido em inglês.
    [InlineData("do it", "confirm.default", null, false)]
    [InlineData("go go go", "confirm.default", null, false)]
    public void EnglishAdversarialCases(string text, string? orderId, string? element, bool queue)
    {
        var intent = M().Match(text);
        Assert.NotNull(intent);
        Assert.Equal(orderId, intent!.OrderId);
        Assert.Equal(element, intent.Element);
        Assert.Equal(queue, intent.Queue);
    }

    [Theory]
    [InlineData("time vermelho abre com flash", "door.open.flashbang", "red", false)]
    [InlineData("azul prepara empilha a esquerda", "door.stack.left", "blue", true)]
    public void PortugueseAdversarialCases(string text, string orderId, string element, bool queue)
    {
        var intent = M("pt").Match(text);
        Assert.NotNull(intent);
        Assert.Equal(orderId, intent!.OrderId);
        Assert.Equal(element, intent.Element);
        Assert.Equal(queue, intent.Queue);
    }

    [Fact]
    public void NoiseYieldsNothing() => Assert.Null(M("pt").Match("banana pudim relogio"));

    [Fact]
    public void EmptyInputYieldsNothing() => Assert.Null(M().Match("   "));

    [Fact]
    public void ElementOnlyIntentHasNoOrder()
    {
        var intent = M().Match("blue team");
        Assert.Equal(new Intent("blue", null, false), intent);
    }

    [Fact]
    public void TighterMarginRejectsInsteadOfGuessing()
    {
        var strict = new PhraseMatcher(
            CommandMap.Load(CommandMapTests.MapPath), "en", new MatcherOptions(Margin: 0.90));
        Assert.Null(strict.Match("open the door with flashbang"));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
dotnet test --filter PhraseMatcherTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.Core/Matching/PhraseMatcher.cs`:

```csharp
using RonVoice.Core.Commands;

namespace RonVoice.Core.Matching;

/// <param name="Threshold">Piso contra ruído.</param>
/// <param name="Margin">
/// Quanto a melhor pontuação precisa superar a melhor de outra ordem. É o
/// parâmetro que importa: sem ele, o matcher manda ordens erradas.
/// </param>
public sealed record MatcherOptions(double Threshold = 0.60, double Margin = 0.05);

/// <summary>
/// Texto -> Intent. Sem estado entre chamadas: o estado de seleção vive no
/// jogo, não aqui.
/// </summary>
public sealed class PhraseMatcher
{
    readonly PhraseIndex _index;
    readonly MatcherOptions _options;
    readonly List<(string[] Tokens, string Element)> _elementAliases = [];
    readonly List<string[]> _queueAliases = [];

    public PhraseMatcher(CommandMap map, string language, MatcherOptions? options = null)
    {
        _index = new PhraseIndex(map, language);
        _options = options ?? new MatcherOptions();

        foreach (var element in map.Elements.Values)
            foreach (var lang in new[] { "en", "pt" })
                if (element.Aliases.TryGetValue(lang, out var aliases))
                    foreach (var a in aliases)
                        _elementAliases.Add(([.. TextNormalizer.Tokenize(a)], element.Name));

        foreach (var lang in new[] { "en", "pt" })
            if (map.Queue.Aliases.TryGetValue(lang, out var aliases))
                foreach (var a in aliases)
                    _queueAliases.Add([.. TextNormalizer.Tokenize(a)]);

        // Casamento mais longo primeiro: "team" é alias de gold e substring de
        // "red team". Varrer na ordem do JSON resolveria "red team" como gold.
        _elementAliases.Sort((x, y) => y.Tokens.Length.CompareTo(x.Tokens.Length));
        _queueAliases.Sort((x, y) => y.Length.CompareTo(x.Length));
    }

    public Intent? Match(string utterance)
    {
        var tokens = TextNormalizer.Tokenize(utterance);
        if (tokens.Count == 0) return null;

        var (afterElement, elementAlias) = StripLongest(
            tokens, _elementAliases.Select(e => e.Tokens).ToList());
        var element = elementAlias is null
            ? null
            : _elementAliases.First(e => ReferenceEquals(e.Tokens, elementAlias)).Element;

        var (afterQueue, queueAlias) = StripLongest(afterElement, _queueAliases);

        // "hold" é alias de fila E frase da ordem `hold`. Remover de forma gulosa
        // destruiria a ordem, então pontuamos os dois candidatos e ficamos com o melhor.
        var candidates = new List<(IReadOnlyList<string> Tokens, bool Queue)>();
        if (queueAlias is not null && afterQueue.Count > 0)
            candidates.Add((afterQueue, true));
        candidates.Add((afterElement, false));

        (double Score, string? OrderId, bool Queue)? best = null;

        foreach (var (candidateTokens, isQueue) in candidates)
        {
            if (candidateTokens.Count == 0) continue;

            var ranked = _index.Rank(candidateTokens);
            if (ranked.Count == 0 || ranked[0].Score < _options.Threshold) continue;

            var top = ranked[0];
            var runnerUp = ranked.FirstOrDefault(
                r => !string.Equals(r.OrderId, top.OrderId, StringComparison.Ordinal));
            var runnerUpScore = runnerUp?.Score ?? 0.0;

            var accepted = top.Score - runnerUpScore >= _options.Margin;
            var candidate = (top.Score, accepted ? top.OrderId : null, isQueue);

            // Empate desempata a favor da fila: enfileirar o que era para executar
            // deixa os NPCs parados e o jogador percebe; o contrário arromba cedo.
            if (best is null || candidate.Score > best.Value.Score
                || (candidate.Score == best.Value.Score && isQueue))
                best = candidate;
        }

        if (best is null || best.Value.OrderId is null)
            return element is null ? null : new Intent(element, null, false);

        return new Intent(element, best.Value.OrderId, best.Value.Queue);
    }

    /// <summary>
    /// Remove a primeira ocorrência da sequência mais longa que couber.
    /// A lista já vem ordenada por tamanho decrescente.
    /// </summary>
    static (IReadOnlyList<string> Rest, string[]? Found) StripLongest(
        IReadOnlyList<string> tokens, IReadOnlyList<string[]> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Length == 0 || candidate.Length > tokens.Count) continue;

            for (var i = 0; i + candidate.Length <= tokens.Count; i++)
            {
                var hit = true;
                for (var j = 0; j < candidate.Length; j++)
                {
                    if (!string.Equals(tokens[i + j], candidate[j], StringComparison.Ordinal))
                    {
                        hit = false;
                        break;
                    }
                }
                if (!hit) continue;

                var rest = new List<string>(tokens.Count - candidate.Length);
                for (var k = 0; k < tokens.Count; k++)
                    if (k < i || k >= i + candidate.Length)
                        rest.Add(tokens[k]);
                return (rest, candidate);
            }
        }
        return (tokens, null);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

```
dotnet test --filter PhraseMatcherTests
```

Esperado: 18 testes passando. Se algum caso adversarial falhar, compare o
comportamento com `docs/superpowers/specs/prototype/phrase_matcher.py` rodando
`python docs/superpowers/specs/prototype/phrase_matcher.py data/ron_commands.json` —
ele imprime os mesmos casos e é a referência.

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Matching/PhraseMatcher.cs RonVoice.Tests/PhraseMatcherTests.cs
git commit -m "feat: match utterances into intents with element and queue parsing"
```

---

## Task 10: CLI `test`, `keymap` e `corpus`

**Files:**
- Create: `RonVoice.Cli/Program.cs`, `RonVoice.Cli/Commands/TestCommand.cs`, `KeymapCommand.cs`, `CorpusCommand.cs`
- Modify: `RonVoice.Cli/RonVoice.Cli.csproj`

**Interfaces:**
- Consumes: `CommandMap`, `KeybindReader`, `CommandResolver`, `PhraseMatcher`.
- Produces: executável `ronvoice` com `test`, `keymap` e `corpus`; e os arquivos `RonVoice.Tests/corpus/{en,pt}.tsv`.

- [ ] **Step 1: Copiar o mapa para a saída do CLI**

Em `RonVoice.Cli/RonVoice.Cli.csproj`, dentro de `<Project>`:

```xml
  <ItemGroup>
    <None Include="../data/ron_commands.json" Link="data/ron_commands.json"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Escrever o despacho**

Create `RonVoice.Cli/Program.cs`:

```csharp
using RonVoice.Cli.Commands;

var command = args.Length > 0 ? args[0] : "help";
var rest = args.Skip(1).ToArray();

return command switch
{
    "test" => TestCommand.Run(rest),
    "keymap" => KeymapCommand.Run(rest),
    "corpus" => CorpusCommand.Run(rest),
    _ => Help(),
};

static int Help()
{
    Console.WriteLine("""
        ronvoice test "<frase>" [--lang en|pt]   casa a frase e imprime a sequência
        ronvoice keymap [--ini <caminho>]        imprime os binds resolvidos
        ronvoice corpus [--out <pasta>]          regenera corpus/{en,pt}.tsv
        """);
    return 1;
}
```

- [ ] **Step 3: Escrever o carregamento compartilhado e o comando `test`**

Create `RonVoice.Cli/Commands/TestCommand.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Cli.Commands;

public static class Cli
{
    public static string MapPath =>
        Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json");

    public static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static bool Flag(string[] args, string name) => args.Contains(name);

    public static string Describe(InputToken token) => token switch
    {
        MouseToken m => $"Mouse({m.Button})",
        ScanCodeToken s => $"Scan(0x{s.Scan:X2}{(s.Extended ? ",E0" : "")})",
        _ => token.ToString()!,
    };

    public static void PrintSequence(KeySequence seq)
    {
        foreach (var s in seq.Steps)
            Console.WriteLine(
                $"  {s.Kind,-5} {Describe(s.Token),-18} hold {s.HoldMs,3}  gap {s.GapAfterMs,3}");
    }
}

public static class TestCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("uso: ronvoice test \"<frase>\" [--lang en|pt]");
            return 1;
        }

        var utterance = args[0];
        var lang = Cli.Option(args, "--lang") ?? "en";
        var map = CommandMap.Load(Cli.MapPath);

        var iniPath = KeybindReader.FindDefaultIniPath();
        if (iniPath is null)
            Console.WriteLine("AVISO: Input.ini não encontrado; usando keybind_defaults");
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        var intent = new PhraseMatcher(map, lang).Match(utterance);
        Console.WriteLine($"frase   : {utterance}");
        Console.WriteLine($"idioma  : {lang}");

        if (intent is null)
        {
            Console.WriteLine("intent  : (nada — rejeitada)");
            return 2;
        }

        Console.WriteLine(
            $"intent  : element={intent.Element ?? "-"} order={intent.OrderId ?? "-"} queue={intent.Queue}");

        if (intent.OrderId is { } id && map.Orders.TryGetValue(id, out var order))
            Console.WriteLine(
                $"ordem   : contexto={order.Context} confiança={order.Confidence} "
                + $"close_menu={order.CloseMenu} path=[{string.Join(' ', order.Path)}]");

        try
        {
            Cli.PrintSequence(new CommandResolver(map, binds).Resolve(intent));
            return 0;
        }
        catch (ResolveException ex)
        {
            Console.Error.WriteLine($"ERRO de resolução: {ex.Message}");
            return 3;
        }
    }
}
```

- [ ] **Step 4: Escrever `keymap` e `corpus`**

Create `RonVoice.Cli/Commands/KeymapCommand.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Cli.Commands;

public static class KeymapCommand
{
    public static int Run(string[] args)
    {
        var map = CommandMap.Load(Cli.MapPath);
        var iniPath = Cli.Option(args, "--ini") ?? KeybindReader.FindDefaultIniPath();

        Console.WriteLine($"Input.ini : {iniPath ?? "(não encontrado — só defaults)"}");
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);
        Console.WriteLine($"binds lidos: {binds.Count}");
        Console.WriteLine();

        var resolver = new CommandResolver(map, binds);
        var tokens = new List<string> { "MENU" };
        tokens.AddRange(Enumerable.Range(1, 9).Select(i => i.ToString()));
        tokens.AddRange(map.Orders.Values
            .SelectMany(o => o.Path)
            .Where(t => t.StartsWith("KEY:", StringComparison.Ordinal))
            .Distinct());

        Console.WriteLine($"{"token",-22} {"tecla",-20} origem");
        foreach (var token in tokens)
            PrintToken(map, binds, token);

        foreach (var element in map.Elements.Keys)
        {
            var action = ActionNames.ForElement(element);
            var bound = binds.GetValueOrDefault(action);
            var seq = resolver.Resolve(new Core.Matching.Intent(element, null, false));
            Console.WriteLine($"{"element:" + element,-22} {Cli.Describe(seq.Steps[0].Token),-20} "
                              + $"{(bound is null ? "default" : action + "=" + bound)}");
        }
        return 0;
    }

    static void PrintToken(
        CommandMap map, IReadOnlyDictionary<string, string> binds, string token)
    {
        var action = token switch
        {
            "MENU" => ActionNames.OpenSwatCommand,
            _ when token.Length == 1 && token[0] is >= '1' and <= '9' =>
                ActionNames.ForDigit(token[0]),
            _ => ActionNames.ForKeyToken(token),
        };

        var bound = action is null ? null : binds.GetValueOrDefault(action);
        var resolver = new CommandResolver(map, binds);
        string rendered;
        try
        {
            var order = map.Orders.Values.First(o => o.Path.Contains(token));
            var seq = resolver.Resolve(new Core.Matching.Intent(null, order.Id, false));
            var index = order.Path.ToList().IndexOf(token);
            rendered = Cli.Describe(seq.Steps[index].Token);
        }
        catch (Exception)
        {
            rendered = "(não resolve)";
        }

        Console.WriteLine($"{token,-22} {rendered,-20} "
                          + $"{(bound is null ? "default" : action + "=" + bound)}");
    }
}
```

Create `RonVoice.Cli/Commands/CorpusCommand.cs`:

```csharp
using System.Text;
using RonVoice.Core.Commands;

namespace RonVoice.Cli.Commands;

/// <summary>
/// Gera uma linha por frase do mapa: frase TAB orderId TAB element TAB queue.
/// É a rede de regressão que pega colisão nova de alias quando o mapa mudar.
/// </summary>
public static class CorpusCommand
{
    public static int Run(string[] args)
    {
        var outDir = Cli.Option(args, "--out")
                     ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                                     "RonVoice.Tests", "corpus");
        Directory.CreateDirectory(outDir);
        var map = CommandMap.Load(Cli.MapPath);

        foreach (var lang in new[] { "en", "pt" })
        {
            var sb = new StringBuilder();
            var count = 0;
            foreach (var order in map.Orders.Values.OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                if (!order.Phrases.TryGetValue(lang, out var phrases)) continue;
                foreach (var phrase in phrases)
                {
                    sb.Append(phrase).Append('\t').Append(order.Id).Append("\t-\tfalse\n");
                    count++;
                }
            }

            var path = Path.Combine(outDir, $"{lang}.tsv");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"{path}: {count} linhas");
        }
        return 0;
    }
}
```

- [ ] **Step 5: Gerar o corpus e conferir os totais**

```
dotnet run --project RonVoice.Cli -- corpus
```

Esperado: `en.tsv: 399 linhas` e `pt.tsv: 371 linhas`. Se vierem 402 e 373, a Task 3 não foi aplicada.

- [ ] **Step 6: Conferir o comando `test` à mão**

```
dotnet run --project RonVoice.Cli -- test "red team, open the door with flashbang"
```

Esperado: `intent : element=red order=door.open.flashbang queue=False`, seguido de três passos (F7, mouse do meio, `Scan(0x03)`).

```
dotnet run --project RonVoice.Cli -- keymap
```

Esperado: tabela com `MENU` resolvendo para `Mouse(Middle)` via `OpenSwatCommand=MiddleMouseButton`, e os dígitos via `SwatInputKey*`.

- [ ] **Step 7: Commit**

```bash
git add RonVoice.Cli RonVoice.Tests/corpus
git commit -m "feat: add CLI test, keymap and corpus commands"
```

---

## Task 11: Testes de corpus — a rede de regressão

**Files:**
- Create: `RonVoice.Tests/corpus/adversarial.tsv`
- Create: `RonVoice.Tests/CorpusTests.cs`

**Interfaces:**
- Consumes: `PhraseMatcher` (Task 9), corpus gerado (Task 10).
- Produces: as duas asserções agregadas da spec §8.1 — zero erradas e cobertura 70/70.

- [ ] **Step 1: Escrever o corpus adversarial**

Create `RonVoice.Tests/corpus/adversarial.tsv`. Colunas separadas por TAB:
`frase`, `orderId`, `element`, `queue`. Use `-` para nulo. Linhas com `#` são comentário.

```
# secao 8 do brief, com o caso 5 corrigido pela secao 2.6 da spec
red team, open the door with flashbang	door.open.flashbang	red	false
open the door with flashbang	door.open.flashbang	-	false
red team	-	red	false
stack up left	door.stack.left	-	false
blue team prep breach and clear	door.breach.leader.clear	blue	true
banana pudim relogio	-	-	false
# colisao team / red team: casamento mais longo primeiro
team	-	gold	false
gold team	-	gold	false
blue team	-	blue	false
# colisao hold: alias de fila e frase de ordem
hold	hold	-	false
hold position	hold	-	false
gold team hold	hold	gold	false
red team hold up	hold	red	false
# stopwords por idioma
do it	confirm.default	-	false
go go go	confirm.default	-	false
```

Create `RonVoice.Tests/corpus/adversarial.pt.tsv`:

```
# corpus PT: e' o que mantem o modo portugues vivo
time vermelho abre com flash	door.open.flashbang	red	false
azul prepara empilha a esquerda	door.stack.left	blue	true
banana pudim relogio	-	-	false
para	hold	-	false
solta luz	player.chemlight	-	false
time ouro empilha	door.stack.auto	gold	false
```

- [ ] **Step 2: Escrever os testes que falham**

Create `RonVoice.Tests/CorpusTests.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.Tests;

public class CorpusTests
{
    static string CorpusPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "corpus", name);

    static CommandMap Map() => CommandMap.Load(CommandMapTests.MapPath);

    static IEnumerable<(string Text, string? OrderId, string? Element, bool Queue)> ReadTsv(string name)
    {
        foreach (var line in File.ReadAllLines(CorpusPath(name)))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            yield return (
                f[0],
                f[1] == "-" ? null : f[1],
                f[2] == "-" ? null : f[2],
                bool.Parse(f[3]));
        }
    }

    [Theory]
    [InlineData("en", "adversarial.tsv")]
    [InlineData("pt", "adversarial.pt.tsv")]
    public void AdversarialCorpusPasses(string lang, string file)
    {
        var matcher = new PhraseMatcher(Map(), lang);
        var failures = new List<string>();

        foreach (var (text, orderId, element, queue) in ReadTsv(file))
        {
            var intent = matcher.Match(text);
            var gotOrder = intent?.OrderId;
            var gotElement = intent?.Element;
            var gotQueue = intent?.Queue ?? false;

            if (gotOrder != orderId || gotElement != element || gotQueue != queue)
                failures.Add(
                    $"\"{text}\" esperado (order={orderId}, el={element}, q={queue}) "
                    + $"veio (order={gotOrder}, el={gotElement}, q={gotQueue})");
        }
        Assert.Empty(failures);
    }

    /// <summary>
    /// A asserção que realmente protege o sistema: nenhuma frase do mapa pode
    /// resolver para uma ordem diferente da sua. Mandar a ordem errada
    /// compromete a missão; rejeitar custa uma repetição.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void NoPhraseResolvesToTheWrongOrder(string lang)
    {
        var matcher = new PhraseMatcher(Map(), lang);
        var wrong = new List<string>();

        foreach (var (text, orderId, _, _) in ReadTsv($"{lang}.tsv"))
        {
            var got = matcher.Match(text)?.OrderId;
            if (got is not null && got != orderId)
                wrong.Add($"{text}: {orderId} -> {got}");
        }
        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("en", 399)]
    [InlineData("pt", 371)]
    public void GeneratedCorpusHasExpectedSize(string lang, int expected) =>
        Assert.Equal(expected, ReadTsv($"{lang}.tsv").Count());

    /// <summary>Nenhuma ordem pode ficar inalcançável em nenhum idioma.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void EveryOrderIsReachable(string lang)
    {
        var matcher = new PhraseMatcher(Map(), lang);
        var reachable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (text, orderId, _, _) in ReadTsv($"{lang}.tsv"))
            if (matcher.Match(text)?.OrderId == orderId)
                reachable.Add(orderId!);

        var unreachable = Map().Orders.Keys.Except(reachable).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Empty(unreachable);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    public void EveryGeneratedPhraseResolves(string lang)
    {
        var matcher = new PhraseMatcher(Map(), lang);
        var rejected = ReadTsv($"{lang}.tsv")
            .Where(r => matcher.Match(r.Text)?.OrderId != r.OrderId)
            .Select(r => $"{r.Text} ({r.OrderId})")
            .ToList();
        Assert.Empty(rejected);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

```
dotnet test --filter CorpusTests
```

Esperado: os testes compilam e rodam. `GeneratedCorpusHasExpectedSize` falha se a
Task 10 não gerou os `.tsv`, e os demais falham se o corpus não foi copiado para a
saída — confira o glob `corpus/**` no `.csproj` da Task 1.

- [ ] **Step 4: Rodar e ver passar**

```
dotnet test
```

Esperado: **toda** a suíte verde. Os quatro testes agregados de `CorpusTests` são o critério de pronto da etapa 2 da spec.

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Tests
git commit -m "test: add generated and adversarial corpora with coverage assertions"
```

---

## Task 12: `SendInputSender`, `ForegroundGuard` e `ronvoice send`

**Files:**
- Create: `RonVoice.Core/Input/IInputSender.cs`, `SendInputSender.cs`, `ForegroundGuard.cs`
- Create: `RonVoice.Cli/Commands/SendCommand.cs`
- Modify: `RonVoice.Cli/Program.cs`

**Interfaces:**
- Consumes: `KeySequence` (Task 6).
- Produces:
  - `interface IInputSender { void Send(KeySequence sequence, CancellationToken ct = default); }`
  - `SendInputSender(bool dryRun = false)` com `Sent` (lista de `INPUT` descritos, para `--dry-run`)
  - `ForegroundGuard.IsGameForeground()`, `ForegroundGuard.IsElevated()`

- [ ] **Step 1: Escrever a interface**

Create `RonVoice.Core/Input/IInputSender.cs`:

```csharp
namespace RonVoice.Core.Input;

public interface IInputSender
{
    void Send(KeySequence sequence, CancellationToken ct = default);
}
```

- [ ] **Step 2: Escrever o guard de foco e elevação**

Create `RonVoice.Core/Input/ForegroundGuard.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RonVoice.Core.Input;

public static partial class ForegroundGuard
{
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>Nomes de processo do jogo, sem extensão.</summary>
    public static readonly string[] GameProcessNames =
        ["ReadyOrNot-Win64-Shipping", "ReadyOrNot"];

    public static string? ForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;   // processo morreu entre a consulta e o acesso
        }
    }

    public static bool IsGameForeground(IReadOnlyCollection<string>? processNames = null)
    {
        var name = ForegroundProcessName();
        if (name is null) return false;
        return (processNames ?? GameProcessNames)
            .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Se o jogo estiver elevado e nós não, o input não chega e não há erro.
    /// Detectar e avisar é o único remédio.
    /// </summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
```

- [ ] **Step 3: Escrever o sender**

Create `RonVoice.Core/Input/SendInputSender.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RonVoice.Core.Input;

/// <summary>
/// SendInput com scan codes. O jogo é Unreal e lê via RawInput: mensagens de
/// janela e keybd_event são ignoradas sem erro nenhum.
/// </summary>
public sealed partial class SendInputSender : IInputSender
{
    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_SCANCODE = 0x0008;

    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100;
    const uint XBUTTON1 = 0x0001, XBUTTON2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    readonly bool _dryRun;

    /// <summary>Descrição legível do que foi (ou seria) enviado. Só para depuração.</summary>
    public List<string> Log { get; } = [];

    public SendInputSender(bool dryRun = false) => _dryRun = dryRun;

    public void Send(KeySequence sequence, CancellationToken ct = default)
    {
        foreach (var step in sequence.Steps)
        {
            ct.ThrowIfCancellationRequested();

            switch (step.Kind)
            {
                case StepKind.Press:
                    Emit(step.Token, down: true);
                    Wait(step.HoldMs);
                    Emit(step.Token, down: false);
                    break;
                case StepKind.Down:
                    Emit(step.Token, down: true);
                    break;
                case StepKind.Up:
                    Emit(step.Token, down: false);
                    break;
            }

            Wait(step.GapAfterMs);
        }
    }

    void Emit(InputToken token, bool down)
    {
        var input = token switch
        {
            ScanCodeToken s => KeyInput(s, down),
            MouseToken m => MouseInput(m, down),
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        };

        Log.Add($"{(down ? "down" : "up  ")} {Render(token)}");
        if (_dryRun) return;

        var buffer = new[] { input };
        var sent = SendInput(1, buffer, Marshal.SizeOf<INPUT>());
        if (sent != 1)
            throw new InvalidOperationException(
                $"SendInput rejeitou o evento (erro {Marshal.GetLastWin32Error()})");
    }

    static string Render(InputToken token) => token switch
    {
        ScanCodeToken s => $"scan 0x{s.Scan:X2}{(s.Extended ? " E0" : "")}",
        MouseToken m => $"mouse {m.Button}",
        _ => token.ToString()!,
    };

    static INPUT KeyInput(ScanCodeToken token, bool down)
    {
        var flags = KEYEVENTF_SCANCODE;
        if (token.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (!down) flags |= KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                // wVk = 0 é obrigatório: com scan code, a virtual key tem que ficar vazia.
                ki = new KEYBDINPUT { wVk = 0, wScan = token.Scan, dwFlags = flags },
            },
        };
    }

    static INPUT MouseInput(MouseToken token, bool down)
    {
        uint flags;
        uint data = 0;

        switch (token.Button)
        {
            case MouseButton.Left: flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
            case MouseButton.Right: flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
            case MouseButton.Middle: flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
            case MouseButton.X1: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON1; break;
            case MouseButton.X2: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON2; break;
            default: throw new ArgumentOutOfRangeException(nameof(token));
        }

        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } },
        };
    }

    /// <summary>
    /// Thread.Sleep tem granularidade de ~15 ms no Windows, o que estoura um hold
    /// de 35 ms. Dorme o grosso e faz spin no resto.
    /// </summary>
    static void Wait(int ms)
    {
        if (ms <= 0) return;

        var sw = Stopwatch.StartNew();
        var coarse = ms - 16;
        if (coarse > 0) Thread.Sleep(coarse);
        while (sw.Elapsed.TotalMilliseconds < ms) Thread.SpinWait(50);
    }
}
```

- [ ] **Step 4: Escrever o teste do dry-run**

Acrescente a `RonVoice.Tests/CommandResolverTests.cs`:

```csharp
    [Fact]
    public void DryRunSenderEmitsDownUpPairsInOrder()
    {
        var seq = Resolver().Resolve(new Intent("red", "door.open.flashbang", true));
        var sender = new SendInputSender(dryRun: true);
        sender.Send(seq);

        Assert.Equal(
            new[]
            {
                "down scan 0x41", "up   scan 0x41",   // F7
                "down mouse Middle", "up   mouse Middle",
                "down scan 0x03", "up   scan 0x03",
                "down scan 0x2A",                     // LShift desce e fica
                "down scan 0x03", "up   scan 0x03",
                "up   scan 0x2A",
                "down mouse Middle", "up   mouse Middle",
            },
            sender.Log);
    }

    [Fact]
    public void DryRunRespectsTiming()
    {
        var seq = Resolver().Resolve(new Intent(null, "door.stack.left", false));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        new SendInputSender(dryRun: true).Send(seq);
        // MENU(100+60) + 1(35+35) + 2(35+0) = 265 ms; folga generosa para CI lento
        Assert.InRange(sw.Elapsed.TotalMilliseconds, 240, 900);
    }
```

Acrescente `using RonVoice.Core.Input;` no topo do arquivo se ainda não estiver lá.

- [ ] **Step 5: Rodar e ver passar**

```
dotnet test --filter CommandResolverTests
```

Esperado: 12 testes passando.

- [ ] **Step 6: Escrever o comando `send`**

Create `RonVoice.Cli/Commands/SendCommand.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Input;
using RonVoice.Core.Matching;

namespace RonVoice.Cli.Commands;

public static class SendCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("uso: ronvoice send \"<frase>\" [--lang en|pt] [--dry-run] [--force]");
            return 1;
        }

        var utterance = args[0];
        var lang = Cli.Option(args, "--lang") ?? "en";
        var dryRun = Cli.Flag(args, "--dry-run");
        var force = Cli.Flag(args, "--force");

        if (!ForegroundGuard.IsElevated())
            Console.Error.WriteLine(
                "AVISO: o app não está elevado. Se o jogo estiver, o input não chega e não há erro.");

        var map = CommandMap.Load(Cli.MapPath);
        var iniPath = KeybindReader.FindDefaultIniPath();
        var binds = iniPath is null
            ? new Dictionary<string, string>()
            : KeybindReader.Read(iniPath);

        var intent = new PhraseMatcher(map, lang).Match(utterance);
        if (intent is null)
        {
            Console.Error.WriteLine("rejeitada: nenhuma ordem casou");
            return 2;
        }

        KeySequence seq;
        try
        {
            seq = new CommandResolver(map, binds).Resolve(intent);
        }
        catch (ResolveException ex)
        {
            Console.Error.WriteLine($"ERRO de resolução: {ex.Message}");
            return 3;
        }

        if (!dryRun && !force && !ForegroundGuard.IsGameForeground())
        {
            Console.Error.WriteLine(
                $"descartada: o jogo não está em foco (em foco: {ForegroundGuard.ForegroundProcessName() ?? "?"}). "
                + "Use --force para mandar mesmo assim.");
            return 4;
        }

        Console.WriteLine(
            $"intent  : element={intent.Element ?? "-"} order={intent.OrderId ?? "-"} queue={intent.Queue}");
        Cli.PrintSequence(seq);

        var sender = new SendInputSender(dryRun);
        sender.Send(seq);

        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("--- eventos INPUT que sairiam ---");
            foreach (var line in sender.Log) Console.WriteLine("  " + line);
        }
        return 0;
    }
}
```

- [ ] **Step 7: Ligar no despacho**

Em `RonVoice.Cli/Program.cs`, acrescente ao `switch`:

```csharp
    "send" => SendCommand.Run(rest),
```

E acrescente à saída de `Help()`:

```
ronvoice send "<frase>" [--dry-run] [--force]   envia ao jogo
```

- [ ] **Step 8: Conferir o dry-run à mão**

```
dotnet run --project RonVoice.Cli -- send "stack up" --dry-run
```

Esperado: sequência impressa e a lista de eventos `INPUT`, sem nada acontecer na máquina.

- [ ] **Step 9: Commit**

```bash
git add RonVoice.Core/Input RonVoice.Cli RonVoice.Tests/CommandResolverTests.cs
git commit -m "feat: send key sequences via SendInput with scan codes and focus guard"
```

---

## Validação em jogo (fecha a etapa 4)

Não é uma tarefa de código: é o roteiro que transforma as hipóteses da spec em fatos.
Faça nesta ordem — o item 1 é o que a §5.4 do brief manda testar primeiro.

- [ ] **1. O clique de fechamento.** Com o jogo aberto e mirando numa porta:

  ```
  dotnet run --project RonVoice.Cli -- send "prep open the door"
  ```

  Espere alguns segundos e mande outra ordem. Se a segunda entrar no lugar errado, o
  menu ficou aberto: a ordem precisa de `close_menu: true`. Se o menu piscar e reabrir,
  ela tem `close_menu` a mais. Corrija `data/ron_commands.json` e anote na spec.

- [ ] **2. As 19 ordens semeadas.** Repita o item 1 para cada uma. A lista está na §10.2
  da spec. Lembre que a semente é hipótese: `MENU 2 2` pode ter sido `move.fallin` ou
  `door.open.flashbang`, e os dois foram marcados.

- [ ] **3. As 25 ordens `confidence: "verify"`.** Confira o caminho de cada uma contra o
  menu real e corrija o `path` no JSON onde divergir. Ao corrigir, rode
  `dotnet run --project RonVoice.Cli -- corpus` e `dotnet test` de novo.

- [ ] **4. O caso `breach and clear`.** Abra o menu e confira se `MENU 3 5 1` é mesmo
  "leader breach and clear". Resolve a pendência 5 da spec.

- [ ] **5. Timing.** Se alguma ordem funcionar de forma intermitente, o hold de 35 ms
  está sendo perdido. Suba `key_hold_ms` no JSON antes de suspeitar de qualquer outra
  coisa — o sintoma clássico é funcionar 70% das vezes.

- [ ] **6. Anti-cheat.** Confirme que a versão atual do jogo não reage a `SendInput`.

---

## Self-Review

**Cobertura da spec:**

| Seção da spec | Onde é implementada |
|---|---|
| 2.1 caminho do Input.ini | Task 4, `FindDefaultIniPath` |
| 2.2 dígitos rebindáveis | Task 6, `ActionNames.ForDigit` |
| 2.3 close_menu é da fila | Task 6, `Resolve`; Task 3, dados |
| 2.4 mouse hold 100 ms | Task 6, `MouseHoldMs` |
| 2.5 path não é chave | Task 2, `Orders` indexado por id |
| 2.6 caso 5 corrigido | Task 9 e Task 11, corpus adversarial |
| 2.7 frases duplicadas | Task 3 |
| 4.1–4.5 contratos | Tasks 2, 5, 6 |
| 5.1 ActionNames | Task 6 |
| 5.2 KeybindReader | Task 4 |
| 5.3 KeyCatalog | Task 5 |
| 5.4 CommandResolver | Task 6 |
| 6.1 normalização | Task 7 |
| 6.2 elemento mais longo | Task 9, `StripLongest` + sort |
| 6.3 fila com backtracking | Task 9, dois candidatos |
| 6.4 IDF e stopwords | Task 8 |
| 6.5 limiar e margem | Task 9, `MatcherOptions` |
| 6.6 saída | Task 9 |
| 7 tratamento de erro | Tasks 4, 6, 12 |
| 8.1 corpus | Tasks 10 e 11 |
| 8.2 demais testes | Tasks 2, 4, 5, 6, 12 |
| 9 CLI | Tasks 10 e 12 |
| 10.1 remoções | Task 3 |
| 10.2 close_menu | Task 3 |
| 11 pendências | seção "Validação em jogo" |
| 12 critérios de pronto | Tasks 3, 10, 11, 12 |

Sem lacunas.

**Consistência de tipos:** `Intent` é criado na Task 6 e consumido nas Tasks 9, 10 e 12
com a mesma assinatura `(string? Element, string? OrderId, bool Queue)`. `KeyStep` usa
`(StepKind, InputToken, int HoldMs, int GapAfterMs)` em todas as tarefas.
`KeybindReader.Read` devolve `IReadOnlyDictionary<string,string>` nas Tasks 4, 6, 10 e 12.
`CommandResolver` tem três parâmetros no construtor, sendo o terceiro opcional — usado
com o terceiro só no teste `ThrowsNamingTheActionWhenNothingResolves` da Task 6.
`PhraseIndex.Rank` devolve `IReadOnlyList<ScoredPhrase>` na Task 8 e é consumido assim na
Task 9.

**Nota de risco conhecido:** o teste `DryRunRespectsTiming` da Task 12 depende de relógio
e pode oscilar em máquina carregada; a faixa é folgada de propósito. Se der flake no CI,
troque por uma asserção sobre `KeySequence` em vez de tempo de parede.
