# RonVoice — aplicativo (etapa 6) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uma pasta portable que alguém baixa, abre, e usa sem nunca ver um terminal — com catálogo pesquisável, teste de voz e configuração de jogo, microfone e modo de escuta.

**Architecture:** O app WPF não contém regra de negócio: toda a lógica vive em view models e em acréscimos ao `RonVoice.Core`, que é o que fica testável. A janela apenas assina os eventos que o `VoicePipeline` já publica. O `RonVoice.Tray` da etapa 5 é absorvido pelo app.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), WPF com interop de WinForms apenas pelo `NotifyIcon`, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-29-ronvoice-app-design.md` — leia antes de começar. Se algo aqui divergir dela, a spec vence e o conflito deve ser levantado, não resolvido em silêncio.

## Global Constraints

- **Invoque o SDK pelo caminho absoluto.** O `dotnet` do PATH é runtime 7 sem SDK. Use sempre:

  ```powershell
  $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
  & $dotnet build
  & $dotnet test
  ```

  Onde este plano escreve `dotnet ...`, leia `& $dotnet ...`.

- **PowerShell 5.1**: não existe `&&` nem `||`; use `;`.
- **`RonVoice.Core` continua sem referência a WPF, WinForms ou `System.Windows`.**
- **Zero lógica de negócio no `RonVoice.App`** — é a §9 do brief. Toda lógica em view models ou no Core. Code-behind só faz *wiring*.
- **A suíte tem 209 testes hoje e deve continuar verde**, além dos que você acrescentar.
- **Build warning-clean**: `TreatWarningsAsErrors` está ligado. Um `using` não usado é erro.
- **Não ligue `InvariantGlobalization`** — quebra o dobramento de acentos e leva junto o modo português.
- **Nada das etapas 1–5 muda de comportamento.** `PhraseMatcher`, `CommandResolver`, `KeyCatalog`, `KeybindReader`, `SendInputSender`, `VoskSpeechEngine` e `VoicePipeline` ficam como estão. `ListenGate` **ganha** um modo, sem perder os existentes.
- **Áudio é 16 kHz, mono, PCM 16 bits.**
- **O app roda elevado por manifesto.** Motivo: o jogo tem integridade mais alta e o Windows descarta input de integridade menor sem gerar erro — confirmado em jogo em 2026-07-28.
- Código, identificadores e commits em **inglês**. Documentação e texto de interface em **português**.

**Sobre XAML:** este plano especifica estrutura, bindings e comportamento com precisão. Estilo visual — cores exatas, espaçamentos, fontes — fica a critério de quem implementa, desde que respeite o que cada tarefa exige. Não invente controles que a tarefa não pede.

---

## File Structure

```
RonVoice.Core/                          acréscimos; continua sem UI
  Config/
    AppSettings.cs          record das preferências + valores padrão
    SettingsStore.cs        carrega e grava, com fallback de caminho
    GameExecutable.cs       caminho do exe -> nome do processo
  Audio/
    AudioLevel.cs           RMS de um bloco PCM -> nível 0..1
  Pipeline/
    ListenGate.cs           MODIFICADO: ganha ListenMode
  Speech/
    ModelDownloader.cs      baixa, valida, move atomicamente
    VoiceTestRunner.cs      o teste de voz: reconhece sem portão e sem enviar
    VoiceTestResult.cs      record do veredito

RonVoice.App/                           WPF; substitui RonVoice.Tray
  app.manifest              requireAdministrator
  App.xaml(.cs)             ciclo de vida, bandeja, hook global
  TrayIcon.cs               movido de RonVoice.Tray
  GlobalHotkey.cs           movido de RonVoice.Tray
  ElementHook.cs            observa F5/F6/F7 para o indicador de elemento
  Views/
    MainWindow.xaml(.cs)  FirstRunView  CommandsView  TestView  SettingsView
  ViewModels/
    MainViewModel  StatusBarViewModel  CommandsViewModel
    TestViewModel  SettingsViewModel  FirstRunViewModel
    OrderRowViewModel        uma linha do catálogo
    RelayCommand.cs          ICommand mínimo

RonVoice.Tests/
  AppSettingsTests  SettingsStoreTests  GameExecutableTests
  AudioLevelTests  ListenModeTests  ModelDownloaderTests
  VoiceTestRunnerTests  CommandsViewModelTests  TestViewModelTests
  SettingsViewModelTests
```

**Fases.** Tarefas 1–6 são lógica no Core, testáveis sem UI. Tarefas 7–12 são o app. O fim da tarefa 6 é o ponto natural de revisão.

---

## Task 1: `AppSettings` e `SettingsStore`

**Files:**
- Create: `RonVoice.Core/Config/AppSettings.cs`, `RonVoice.Core/Config/SettingsStore.cs`
- Test: `RonVoice.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `record AppSettings(string Language, string? GameExecutablePath, int MicrophoneDevice, ListenModeSetting Mode, string? PushToTalkKey, double ConfidenceThreshold)` com `AppSettings.Default`
  - `enum ListenModeSetting { AlwaysOn, PushToTalk }`
  - `SettingsStore.Load(string? directory = null)` → `(AppSettings Settings, string Path, bool Portable)`
  - `SettingsStore.Save(AppSettings settings, string path)` → `void`
  - `SettingsStore.ResolvePath(string exeDirectory)` → `(string Path, bool Portable)`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/SettingsStoreTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter SettingsStoreTests
```

Esperado: erro de compilação — `RonVoice.Core.Config` não existe.

- [ ] **Step 3: Escrever o record**

Create `RonVoice.Core/Config/AppSettings.cs`:

```csharp
namespace RonVoice.Core.Config;

public enum ListenModeSetting
{
    /// <summary>Padrão de fábrica: escuta sempre, com o portão de foco do jogo.</summary>
    AlwaysOn,
    /// <summary>Escuta só enquanto a tecla configurada estiver pressionada.</summary>
    PushToTalk,
}

public sealed record AppSettings(
    string Language,
    string? GameExecutablePath,
    int MicrophoneDevice,
    ListenModeSetting Mode,
    string? PushToTalkKey,
    double ConfidenceThreshold)
{
    /// <summary>
    /// Sempre-ligado é o padrão por decisão do autor; PTT existe para quem
    /// preferir. O limiar nasce em 0 (desligado) porque depende de microfone,
    /// voz e ambiente — fixar um número seria inventá-lo.
    /// </summary>
    public static AppSettings Default { get; } = new(
        Language: "en",
        GameExecutablePath: null,
        MicrophoneDevice: 0,
        Mode: ListenModeSetting.AlwaysOn,
        PushToTalkKey: null,
        ConfidenceThreshold: 0.0);
}
```

- [ ] **Step 4: Escrever o store**

Create `RonVoice.Core/Config/SettingsStore.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RonVoice.Core.Config;

/// <summary>
/// Persiste as preferências ao lado do executável — é o que faz o modo portable
/// significar alguma coisa: copiar a pasta leva tudo junto.
/// </summary>
public static class SettingsStore
{
    const string FileName = "settings.json";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Ao lado do executável quando dá para gravar ali; senão %APPDATA%\RonVoice.
    /// O caso real do fallback é a pasta estar em Program Files.
    /// </summary>
    public static (string Path, bool Portable) ResolvePath(string exeDirectory)
    {
        if (IsWritable(exeDirectory))
            return (System.IO.Path.Combine(exeDirectory, FileName), true);

        var appData = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RonVoice");
        Directory.CreateDirectory(appData);
        return (System.IO.Path.Combine(appData, FileName), false);
    }

    public static (AppSettings Settings, string Path, bool Portable) Load(string? directory = null)
    {
        var dir = directory ?? AppContext.BaseDirectory;
        var (path, portable) = ResolvePath(dir);

        if (!File.Exists(path)) return (AppSettings.Default, path, portable);

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
            return (loaded ?? AppSettings.Default, path, portable);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Arquivo corrompido não pode impedir o app de abrir: a correção é
            // pela própria tela, e sem abrir o usuário não tem como corrigir.
            return (AppSettings.Default, path, portable);
        }
    }

    public static void Save(AppSettings settings, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));

    static bool IsWritable(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return false;
            var probe = System.IO.Path.Combine(directory, $".ronvoice-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
```

Note que `AppSettings` precisa de um construtor sem parâmetros para o `System.Text.Json`
desserializar num record posicional — o `JsonSerializer` do .NET 10 lida com records
posicionais nativamente pelo construtor primário, então nada a fazer.

- [ ] **Step 5: Rodar e ver passar**

```
& $dotnet test --filter SettingsStoreTests
```

Esperado: 7 testes passando.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Config RonVoice.Tests/SettingsStoreTests.cs
git commit -m "feat: persist preferences beside the executable with an AppData fallback"
```

---

## Task 2: `GameExecutable`

**Files:**
- Create: `RonVoice.Core/Config/GameExecutable.cs`
- Test: `RonVoice.Tests/GameExecutableTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `GameExecutable.ProcessNameOf(string path)` → `string`
  - `GameExecutable.LooksLikeReadyOrNot(string path)` → `bool`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/GameExecutableTests.cs`:

```csharp
using RonVoice.Core.Config;

namespace RonVoice.Tests;

public class GameExecutableTests
{
    /// <summary>
    /// O nome do processo varia por loja. A versao Steam desta maquina chama-se
    /// ReadyOrNotSteam-Win64-Shipping, e assumir o nome padrao fez o app
    /// descartar todas as ordens em silencio ate isso ser descoberto.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Steam\ReadyOrNot\ReadyOrNotSteam-Win64-Shipping.exe",
                "ReadyOrNotSteam-Win64-Shipping")]
    [InlineData(@"D:\Epic\ReadyOrNot\ReadyOrNot-Win64-Shipping.exe",
                "ReadyOrNot-Win64-Shipping")]
    [InlineData(@"C:\Jogos\ReadyOrNot.exe", "ReadyOrNot")]
    public void DerivesTheProcessNameFromThePath(string path, string expected) =>
        Assert.Equal(expected, GameExecutable.ProcessNameOf(path));

    [Fact]
    public void AcceptsAPathWithoutTheExtension() =>
        Assert.Equal("ReadyOrNot", GameExecutable.ProcessNameOf(@"C:\Jogos\ReadyOrNot"));

    [Fact]
    public void EmptyPathThrows() =>
        Assert.Throws<ArgumentException>(() => GameExecutable.ProcessNameOf("  "));

    [Theory]
    [InlineData(@"C:\x\ReadyOrNotSteam-Win64-Shipping.exe", true)]
    [InlineData(@"C:\x\ReadyOrNot-Win64-Shipping.exe", true)]
    [InlineData(@"C:\x\readyornot.exe", true)]
    [InlineData(@"C:\x\chrome.exe", false)]
    [InlineData(@"C:\x\ReadyOrNotLauncher.exe", true)]
    public void RecognisesWhenThePathLooksLikeTheGame(string path, bool expected) =>
        Assert.Equal(expected, GameExecutable.LooksLikeReadyOrNot(path));
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter GameExecutableTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.Core/Config/GameExecutable.cs`:

```csharp
namespace RonVoice.Core.Config;

/// <summary>
/// Converte o executável que o usuário escolheu no nome de processo que o
/// ForegroundGuard compara. O nome varia por loja: a build Steam chama-se
/// ReadyOrNotSteam-Win64-Shipping, e assumir o nome padrão fez o app descartar
/// todas as ordens em silêncio até isso ser descoberto em jogo.
/// </summary>
public static class GameExecutable
{
    public static string ProcessNameOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("caminho vazio", nameof(path));

        return Path.GetFileNameWithoutExtension(path.Trim());
    }

    /// <summary>
    /// Serve para avisar quem escolher o arquivo errado, não para impedir:
    /// builds futuras podem ter nomes que não previmos.
    /// </summary>
    public static bool LooksLikeReadyOrNot(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && ProcessNameOf(path).StartsWith("ReadyOrNot", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Rodar e ver passar**

```
& $dotnet test --filter GameExecutableTests
```

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Config/GameExecutable.cs RonVoice.Tests/GameExecutableTests.cs
git commit -m "feat: derive the game process name from a chosen executable"
```

---

## Task 3: Modo push-to-talk no `ListenGate`

**Files:**
- Modify: `RonVoice.Core/Pipeline/ListenGate.cs`
- Test: `RonVoice.Tests/ListenGateTests.cs` (estender)

**Interfaces:**
- Consumes: nada.
- Produces:
  - `enum ListenMode { AlwaysOn, PushToTalk }`
  - construtor novo: `ListenGate(Func<bool> isGameForeground, Func<bool>? isMuted = null, ListenMode mode = ListenMode.AlwaysOn, Func<bool>? isTalkKeyDown = null)`
  - `ListenGate.Mode` (get/set), `ListenGate.TestBypass` (get/set)
  - `ListenState` ganha o valor `WaitingForKey`

- [ ] **Step 1: Escrever os testes que falham**

Acrescente a `RonVoice.Tests/ListenGateTests.cs`:

```csharp
    [Theory]
    // Em PTT, o foco do jogo continua valendo E a tecla precisa estar pressionada.
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void PushToTalkAlsoRequiresTheKey(bool focused, bool keyDown, bool expected)
    {
        var gate = new ListenGate(
            () => focused, () => false, ListenMode.PushToTalk, () => keyDown);
        Assert.Equal(expected, gate.ShouldProcess());
    }

    [Fact]
    public void PushToTalkWithTheKeyUpReportsWaitingForKey()
    {
        var gate = new ListenGate(
            () => true, () => false, ListenMode.PushToTalk, () => false);
        Assert.Equal(ListenState.WaitingForKey, gate.State);
    }

    [Fact]
    public void MuteStillWinsOverPushToTalk()
    {
        var gate = new ListenGate(
            () => true, () => true, ListenMode.PushToTalk, () => true);
        Assert.Equal(ListenState.Muted, gate.State);
        Assert.False(gate.ShouldProcess());
    }

    [Fact]
    public void SwitchingModeAtRuntimeTakesEffect()
    {
        var gate = new ListenGate(() => true, () => false, ListenMode.PushToTalk, () => false);
        Assert.False(gate.ShouldProcess());

        gate.Mode = ListenMode.AlwaysOn;
        Assert.True(gate.ShouldProcess());
    }

    /// <summary>
    /// Na aba de teste quem esta em foco e' a janela do app, nao o jogo. Sem
    /// esta excecao o teste de voz nunca ouviria nada.
    /// </summary>
    [Fact]
    public void TestBypassOpensTheGateRegardlessOfFocusAndMode()
    {
        var gate = new ListenGate(() => false, () => false, ListenMode.PushToTalk, () => false);
        Assert.False(gate.ShouldProcess());

        gate.TestBypass = true;
        Assert.True(gate.ShouldProcess());
        Assert.Equal(ListenState.Listening, gate.State);
    }

    [Fact]
    public void TestBypassDoesNotOverrideMute()
    {
        var gate = new ListenGate(() => true, () => true) { TestBypass = true };
        Assert.False(gate.ShouldProcess());
    }
```

Acrescente `using RonVoice.Core.Pipeline;` se ainda não estiver no arquivo.

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter ListenGateTests
```

- [ ] **Step 3: Implementar**

Substitua o conteúdo de `RonVoice.Core/Pipeline/ListenGate.cs`:

```csharp
namespace RonVoice.Core.Pipeline;

public enum ListenState { Listening, Idle, Muted, WaitingForKey }

public enum ListenMode
{
    /// <summary>Padrão: escuta sempre que o jogo estiver em foco.</summary>
    AlwaysOn,
    /// <summary>Escuta só enquanto a tecla configurada estiver pressionada.</summary>
    PushToTalk,
}

/// <summary>
/// Responde "devo processar este áudio agora?". Existe como classe própria porque
/// no modo padrão o microfone fica sempre ligado: esta é a única mitigação contra
/// conversa virar ordem, e precisa ser testável sem jogo e sem microfone.
/// </summary>
public sealed class ListenGate
{
    readonly Func<bool> _isGameForeground;
    readonly Func<bool>? _externalMute;
    readonly Func<bool>? _isTalkKeyDown;
    bool _muted;
    ListenState _last;

    public ListenGate(
        Func<bool> isGameForeground,
        Func<bool>? isMuted = null,
        ListenMode mode = ListenMode.AlwaysOn,
        Func<bool>? isTalkKeyDown = null)
    {
        _isGameForeground = isGameForeground;
        _externalMute = isMuted;
        _isTalkKeyDown = isTalkKeyDown;
        Mode = mode;
        _last = State;
    }

    public event Action<ListenState>? StateChanged;

    public ListenMode Mode { get; set; }

    /// <summary>
    /// Abre o portão para a aba de teste, onde quem está em foco é a janela do
    /// app e não o jogo. Não vence o mute: silenciar é uma escolha explícita.
    /// </summary>
    public bool TestBypass { get; set; }

    public bool Muted
    {
        get => _externalMute?.Invoke() ?? _muted;
        set { _muted = value; Poll(); }
    }

    public ListenState State
    {
        get
        {
            if (Muted) return ListenState.Muted;
            if (TestBypass) return ListenState.Listening;
            if (!_isGameForeground()) return ListenState.Idle;
            if (Mode == ListenMode.PushToTalk && !(_isTalkKeyDown?.Invoke() ?? false))
                return ListenState.WaitingForKey;
            return ListenState.Listening;
        }
    }

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

- [ ] **Step 4: Rodar a suíte inteira**

```
& $dotnet test
```

Esperado: tudo verde. Os testes da etapa 5 usam o construtor de dois parâmetros, que
continua válido pelos valores padrão.

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Pipeline/ListenGate.cs RonVoice.Tests/ListenGateTests.cs
git commit -m "feat: add push-to-talk and a test bypass to the listen gate"
```

---

## Task 4: `AudioLevel`

**Files:**
- Create: `RonVoice.Core/Audio/AudioLevel.cs`
- Test: `RonVoice.Tests/AudioLevelTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `AudioLevel.Rms(ReadOnlySpan<byte> pcm16)` → `double` entre 0 e 1.

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/AudioLevelTests.cs`:

```csharp
using RonVoice.Core.Audio;

namespace RonVoice.Tests;

public class AudioLevelTests
{
    static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), samples[i]);
        return bytes;
    }

    [Fact]
    public void SilenceIsZero() =>
        Assert.Equal(0.0, AudioLevel.Rms(Pcm(0, 0, 0, 0)), 6);

    [Fact]
    public void FullScaleIsOne() =>
        Assert.Equal(1.0, AudioLevel.Rms(Pcm(short.MaxValue, short.MinValue + 1)), 3);

    [Fact]
    public void HalfScaleIsAboutAHalf() =>
        Assert.InRange(AudioLevel.Rms(Pcm(16384, -16384, 16384, -16384)), 0.45, 0.55);

    [Fact]
    public void EmptyBufferIsZero() =>
        Assert.Equal(0.0, AudioLevel.Rms([]));

    /// <summary>
    /// A captura entrega blocos de tamanho arbitrario; um byte solto no fim nao
    /// pode derrubar o medidor enquanto o usuario esta falando.
    /// </summary>
    [Fact]
    public void AnOddNumberOfBytesDoesNotThrow() =>
        Assert.Equal(0.0, AudioLevel.Rms(new byte[] { 0 }));
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter AudioLevelTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.Core/Audio/AudioLevel.cs`:

```csharp
namespace RonVoice.Core.Audio;

/// <summary>
/// Nível de áudio de um bloco PCM 16 bits. É o que responde "o microfone está
/// pegando?" sem envolver reconhecimento nenhum — se a barra não se mexe
/// enquanto a pessoa fala, a investigação termina aí.
/// </summary>
public static class AudioLevel
{
    public static double Rms(ReadOnlySpan<byte> pcm16)
    {
        var samples = pcm16.Length / 2;
        if (samples == 0) return 0.0;

        double sum = 0;
        for (var i = 0; i < samples; i++)
        {
            double s = BitConverter.ToInt16(pcm16.Slice(i * 2, 2));
            sum += s * s;
        }

        return Math.Min(1.0, Math.Sqrt(sum / samples) / short.MaxValue);
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

```
& $dotnet test --filter AudioLevelTests
```

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Audio/AudioLevel.cs RonVoice.Tests/AudioLevelTests.cs
git commit -m "feat: measure input level so a dead microphone is visible"
```

---

## Task 5: `ModelDownloader`

**Files:**
- Create: `RonVoice.Core/Speech/ModelDownloader.cs`
- Test: `RonVoice.Tests/ModelDownloaderTests.cs`

**Interfaces:**
- Consumes: `ModelLocator.LooksLikeAModel(string)`.
- Produces:
  - `record ModelSpec(string Language, string DirectoryName, string Url, long Bytes)` com `ModelDownloader.Specs` → `IReadOnlyDictionary<string, ModelSpec>`
  - `ModelDownloader.InstallFromZip(string zipPath, string modelsDir, ModelSpec spec)` → `string` (pasta final), lançando `InvalidDataException` quando o conteúdo não é um modelo
  - `ModelDownloader.DownloadAsync(ModelSpec spec, string modelsDir, IProgress<double>? progress, CancellationToken ct)` → `Task<string>`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/ModelDownloaderTests.cs`:

```csharp
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
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter ModelDownloaderTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.Core/Speech/ModelDownloader.cs`:

```csharp
using System.IO.Compression;

namespace RonVoice.Core.Speech;

public sealed record ModelSpec(string Language, string DirectoryName, string Url, long Bytes);

/// <summary>
/// Baixa e instala modelos Vosk. A ordem — pasta temporária, valida, só então
/// move — não é preciosismo: a biblioteca nativa aborta o processo diante de um
/// modelo inválido em vez de lançar exceção, e o app fecharia sem mensagem
/// nenhuma, de novo a cada abertura.
/// </summary>
public static class ModelDownloader
{
    public static IReadOnlyDictionary<string, ModelSpec> Specs { get; } =
        new Dictionary<string, ModelSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("en", "vosk-model-small-en-us-0.15",
                "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip",
                41205931),
            ["pt"] = new("pt", "vosk-model-small-pt-0.3",
                "https://alphacephei.com/vosk/models/vosk-model-small-pt-0.3.zip",
                32453112),
        };

    public static async Task<string> DownloadAsync(
        ModelSpec spec, string modelsDir, IProgress<double>? progress, CancellationToken ct)
    {
        var zip = Path.Combine(Path.GetTempPath(), $"{spec.DirectoryName}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await http.GetAsync(
                       spec.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? spec.Bytes;

                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(zip);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(total > 0 ? (double)done / total : 0);
                }
            }

            return InstallFromZip(zip, modelsDir, spec);
        }
        finally
        {
            if (File.Exists(zip)) File.Delete(zip);
        }
    }

    /// <summary>
    /// Extrai para uma pasta temporária, valida, e só então substitui o destino.
    /// Falhando em qualquer ponto, o que já existia permanece intacto.
    /// </summary>
    public static string InstallFromZip(string zipPath, string modelsDir, ModelSpec spec)
    {
        Directory.CreateDirectory(modelsDir);
        var staging = Path.Combine(modelsDir, $".staging-{Guid.NewGuid():N}");

        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging);

            var extracted = Path.Combine(staging, spec.DirectoryName);
            if (!Directory.Exists(extracted))
            {
                // Alguns zips não trazem a pasta raiz com o nome esperado.
                var only = Directory.GetDirectories(staging);
                extracted = only.Length == 1 ? only[0] : staging;
            }

            if (!ModelLocator.LooksLikeAModel(extracted))
                throw new InvalidDataException(
                    $"o conteúdo baixado não é um modelo Vosk válido: {spec.Url}");

            var final = Path.Combine(modelsDir, spec.DirectoryName);
            if (Directory.Exists(final)) Directory.Delete(final, recursive: true);
            Directory.Move(extracted, final);
            return final;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

```
& $dotnet test --filter ModelDownloaderTests
```

Esperado: 5 testes passando.

- [ ] **Step 5: Commit**

```bash
git add RonVoice.Core/Speech/ModelDownloader.cs RonVoice.Tests/ModelDownloaderTests.cs
git commit -m "feat: install Vosk models atomically after validating them"
```

---

## Task 6: `VoiceTestRunner`

O motor por trás de "Testar minha voz". Recebe áudio, devolve um veredito. Não envia tecla
nenhuma — é o único ponto do sistema onde reconhecer com sucesso não produz input.

**Files:**
- Create: `RonVoice.Core/Speech/VoiceTestResult.cs`, `RonVoice.Core/Speech/VoiceTestRunner.cs`
- Test: `RonVoice.Tests/VoiceTestRunnerTests.cs`

**Interfaces:**
- Consumes: `ISpeechEngine`, `PhraseMatcher`, `AudioLevel`, `RecognitionResult`.
- Produces:
  - `enum VoiceTestOutcome { Success, NoAudio, OutOfVocabulary, LowConfidence, NoMatch }`
  - `record VoiceTestResult(VoiceTestOutcome Outcome, string HeardText, double Confidence, double PeakLevel, Intent? Intent)`
  - `VoiceTestRunner(ISpeechEngine engine, PhraseMatcher matcher, double confidenceThreshold = 0.0)` com `Feed(ReadOnlyMemory<byte>)`, `Finish()` → `VoiceTestResult`, `PeakLevel`, e evento `LevelChanged`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/VoiceTestRunnerTests.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class VoiceTestRunnerTests
{
    static PhraseMatcher Matcher() =>
        new(CommandMap.Load(CommandMapTests.MapPath), "en");

    static byte[] Loud(int samples = 800)
    {
        var b = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
            BitConverter.TryWriteBytes(b.AsSpan(i * 2), (short)(i % 2 == 0 ? 12000 : -12000));
        return b;
    }

    static byte[] Silence(int samples = 800) => new byte[samples * 2];

    [Fact]
    public void RecognizedCommandIsASuccess()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Loud());
        engine.Emit("stack left");

        var result = runner.Finish();
        Assert.Equal(VoiceTestOutcome.Success, result.Outcome);
        Assert.Equal("door.stack.left", result.Intent!.OrderId);
    }

    /// <summary>
    /// Silencio absoluto significa microfone, nao pronuncia. E' a distincao que
    /// a aba de teste existe para fazer.
    /// </summary>
    [Fact]
    public void SilenceIsReportedAsNoAudioEvenIfNothingWasRecognized()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Silence());

        Assert.Equal(VoiceTestOutcome.NoAudio, runner.Finish().Outcome);
    }

    [Fact]
    public void AudioWithoutRecognitionIsNotConfusedWithSilence()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Loud());
        engine.Emit("");

        Assert.Equal(VoiceTestOutcome.NoMatch, runner.Finish().Outcome);
    }

    [Fact]
    public void UnknownTokenIsOutOfVocabulary()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Loud());
        engine.Emit("[unk]");

        Assert.Equal(VoiceTestOutcome.OutOfVocabulary, runner.Finish().Outcome);
    }

    [Fact]
    public void ConfidenceBelowTheThresholdIsReportedAsSuch()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher(), confidenceThreshold: 0.8);
        runner.Feed(Loud());
        engine.Emit("stack left", confidence: 0.2);

        Assert.Equal(VoiceTestOutcome.LowConfidence, runner.Finish().Outcome);
    }

    [Fact]
    public void SpeechThatMatchesNothingIsNoMatch()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        runner.Feed(Loud());
        engine.Emit("banana pudding clock");

        var result = runner.Finish();
        Assert.Equal(VoiceTestOutcome.NoMatch, result.Outcome);
        Assert.Equal("banana pudding clock", result.HeardText);
    }

    [Fact]
    public void ReportsThePeakLevelSoTheMeterHasSomethingToShow()
    {
        var engine = new FakeSpeechEngine();
        var runner = new VoiceTestRunner(engine, Matcher());
        var levels = new List<double>();
        runner.LevelChanged += levels.Add;

        runner.Feed(Silence());
        runner.Feed(Loud());

        Assert.Equal(2, levels.Count);
        Assert.True(runner.PeakLevel > 0.3, $"pico baixo demais: {runner.PeakLevel}");
    }
}
```

`FakeSpeechEngine` já existe em `RonVoice.Tests/VoicePipelineTests.cs` e é reutilizado aqui.

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter VoiceTestRunnerTests
```

- [ ] **Step 3: Escrever o record**

Create `RonVoice.Core/Speech/VoiceTestResult.cs`:

```csharp
using RonVoice.Core.Matching;

namespace RonVoice.Core.Speech;

public enum VoiceTestOutcome
{
    /// <summary>Reconheceu e casou com uma ordem.</summary>
    Success,
    /// <summary>Nenhum áudio acima do silêncio: é problema de microfone.</summary>
    NoAudio,
    /// <summary>Ouviu, mas a fala está fora da gramática.</summary>
    OutOfVocabulary,
    /// <summary>Reconheceu com confiança abaixo do limiar configurado.</summary>
    LowConfidence,
    /// <summary>Ouviu, mas não bate com nenhum comando.</summary>
    NoMatch,
}

public sealed record VoiceTestResult(
    VoiceTestOutcome Outcome,
    string HeardText,
    double Confidence,
    double PeakLevel,
    Intent? Intent);
```

- [ ] **Step 4: Escrever o runner**

Create `RonVoice.Core/Speech/VoiceTestRunner.cs`:

```csharp
using RonVoice.Core.Audio;
using RonVoice.Core.Matching;

namespace RonVoice.Core.Speech;

/// <summary>
/// O motor de "Testar minha voz". Reconhece e classifica, sem enviar nada ao
/// jogo: é o único ponto do sistema onde um reconhecimento bem-sucedido não
/// produz tecla. Separa duas perguntas que o usuário não distingue sozinho —
/// o microfone está pegando, e a pronúncia está sendo entendida.
/// </summary>
public sealed class VoiceTestRunner
{
    /// <summary>Abaixo disto consideramos que não houve fala, e sim silêncio.</summary>
    const double SilenceFloor = 0.02;

    readonly ISpeechEngine _engine;
    readonly PhraseMatcher _matcher;
    readonly double _confidenceThreshold;
    RecognitionResult? _last;

    public event Action<double>? LevelChanged;

    public double PeakLevel { get; private set; }

    public VoiceTestRunner(
        ISpeechEngine engine, PhraseMatcher matcher, double confidenceThreshold = 0.0)
    {
        _engine = engine;
        _matcher = matcher;
        _confidenceThreshold = confidenceThreshold;
        _engine.OnRecognized += OnRecognized;
    }

    public void Feed(ReadOnlyMemory<byte> audio)
    {
        var level = AudioLevel.Rms(audio.Span);
        if (level > PeakLevel) PeakLevel = level;
        LevelChanged?.Invoke(level);
        _engine.Feed(audio);
    }

    public VoiceTestResult Finish()
    {
        _engine.Flush();
        _engine.OnRecognized -= OnRecognized;

        var heard = _last?.Text ?? "";
        var confidence = _last?.AverageConfidence ?? 0.0;

        // Silêncio vem primeiro: sem áudio, discutir pronúncia não faz sentido.
        if (PeakLevel < SilenceFloor)
            return new VoiceTestResult(
                VoiceTestOutcome.NoAudio, heard, confidence, PeakLevel, null);

        if (_last?.ContainsUnknown == true)
            return new VoiceTestResult(
                VoiceTestOutcome.OutOfVocabulary, heard, confidence, PeakLevel, null);

        if (heard.Length == 0)
            return new VoiceTestResult(
                VoiceTestOutcome.NoMatch, heard, confidence, PeakLevel, null);

        if (_confidenceThreshold > 0 && confidence < _confidenceThreshold)
            return new VoiceTestResult(
                VoiceTestOutcome.LowConfidence, heard, confidence, PeakLevel, null);

        var intent = _matcher.Match(heard);
        return intent is null
            ? new VoiceTestResult(VoiceTestOutcome.NoMatch, heard, confidence, PeakLevel, null)
            : new VoiceTestResult(VoiceTestOutcome.Success, heard, confidence, PeakLevel, intent);
    }

    void OnRecognized(RecognitionResult result)
    {
        if (result.IsFinal && (result.Text.Length > 0 || _last is null)) _last = result;
    }
}
```

- [ ] **Step 5: Rodar a suíte inteira**

```
& $dotnet test
```

Esperado: tudo verde.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.Core/Speech RonVoice.Tests/VoiceTestRunnerTests.cs
git commit -m "feat: classify a voice test into an actionable outcome"
```

**Fim da fase 1.** Toda a lógica nova existe e está testada, sem uma linha de UI. Bom ponto
para revisão antes de seguir.

---

## Task 7: Projeto WPF, manifesto e absorção da bandeja

**Files:**
- Create: `RonVoice.App/` (projeto), `RonVoice.App/app.manifest`, `RonVoice.App/ViewModels/RelayCommand.cs`
- Move: `RonVoice.Tray/TrayIcon.cs` → `RonVoice.App/TrayIcon.cs`; `RonVoice.Tray/GlobalHotkey.cs` → `RonVoice.App/GlobalHotkey.cs`
- Delete: projeto `RonVoice.Tray`
- Modify: `RonVoice.sln`

**Interfaces:**
- Consumes: `TrayIcon`, `GlobalHotkey` da etapa 5.
- Produces: `RelayCommand : ICommand` com `RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)` e `RaiseCanExecuteChanged()`.

- [ ] **Step 1: Criar o projeto**

```
& $dotnet new wpf -o RonVoice.App -n RonVoice.App
& $dotnet sln add RonVoice.App
& $dotnet add RonVoice.App reference RonVoice.Core
```

Em `RonVoice.App/RonVoice.App.csproj`, garanta:

```xml
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <UseWPF>true</UseWPF>
    <!-- interop apenas pelo NotifyIcon; não há Form neste app -->
    <UseWindowsForms>true</UseWindowsForms>
    <!-- exigido pelo gerador do LibraryImport usado nos hooks (SYSLIB1062) -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>

  <ItemGroup>
    <None Include="../data/ron_commands.json" Link="data/ron_commands.json"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Escrever o manifesto**

Create `RonVoice.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <!--
          O jogo roda com integridade mais alta. O Windows descarta input de
          integridade menor SEM gerar erro, então sem elevação o app parece
          funcionar e nenhuma tecla chega. Confirmado em jogo em 2026-07-28.
        -->
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

- [ ] **Step 3: Mover a bandeja e o hotkey**

```
git mv RonVoice.Tray/TrayIcon.cs RonVoice.App/TrayIcon.cs
git mv RonVoice.Tray/GlobalHotkey.cs RonVoice.App/GlobalHotkey.cs
```

Troque o namespace dos dois de `RonVoice.Tray` para `RonVoice.App`.

Em `TrayIcon.cs`, troque `using System.Drawing;` por `using System.Drawing;` (permanece) e
confirme que ele não referencia `System.Windows.Forms.Application` — o ciclo de vida agora
é do WPF.

Remova o projeto antigo:

```
& $dotnet sln remove RonVoice.Tray
git rm -r --cached RonVoice.Tray
```

Apague a pasta `RonVoice.Tray` do disco.

- [ ] **Step 4: Escrever o `RelayCommand`**

Create `RonVoice.App/ViewModels/RelayCommand.cs`:

```csharp
using System.Windows.Input;

namespace RonVoice.App.ViewModels;

/// <summary>ICommand mínimo. Existe para o XAML poder chamar métodos do view model.</summary>
public sealed class RelayCommand(
    Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 5: Build e suíte**

```
& $dotnet build
& $dotnet test
```

Esperado: build limpo, 209 testes ainda verdes. O app abre uma janela vazia — é o esperado
nesta tarefa.

- [ ] **Step 6: Verificar que o UAC aparece**

```
& $dotnet build -c Release
```

Rode `RonVoice.App\bin\Release\net10.0-windows\RonVoice.App.exe` por duplo clique e
confirme que o Windows pede confirmação de administrador. Registre no relatório.

Observação: `dotnet run` pode não disparar o UAC, porque quem executa é o host `dotnet`.
Teste pelo `.exe`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: scaffold the WPF app, require elevation, absorb the tray project"
```

---

## Task 8: `StatusBarViewModel` e a janela principal

**Files:**
- Create: `RonVoice.App/ViewModels/StatusBarViewModel.cs`, `RonVoice.App/ViewModels/MainViewModel.cs`
- Modify: `RonVoice.App/MainWindow.xaml(.cs)`
- Test: `RonVoice.Tests/StatusBarViewModelTests.cs`

**Interfaces:**
- Consumes: `ListenState`, `ListenGate`, `AppSettings`.
- Produces:
  - `StatusBarViewModel` com `Elevated`, `MicrophoneName`, `Language`, `GameFocused`, `ActiveElement`, `Portable`, `ListenState`, `MuteCommand`, e `Summary` → `string`
  - `MainViewModel` com `SelectedTabIndex` e as quatro sub-view-models

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/StatusBarViewModelTests.cs`:

```csharp
using RonVoice.App.ViewModels;
using RonVoice.Core.Pipeline;

namespace RonVoice.Tests;

public class StatusBarViewModelTests
{
    static StatusBarViewModel Vm() => new();

    [Fact]
    public void SummaryNamesEveryPieceOfStateTheUserNeeds()
    {
        var vm = Vm();
        vm.Elevated = true;
        vm.MicrophoneName = "Microfone (WIND)";
        vm.Language = "en";
        vm.ListenState = ListenState.Idle;

        var s = vm.Summary;
        Assert.Contains("Microfone (WIND)", s);
        Assert.Contains("en", s);
    }

    /// <summary>
    /// Sem elevacao nenhuma tecla chega ao jogo e nao ha erro. E' a falha
    /// numero um e ela precisa estar dita, nao inferida.
    /// </summary>
    [Fact]
    public void NotElevatedIsCalledOutExplicitly()
    {
        var vm = Vm();
        vm.Elevated = false;
        Assert.Contains("sem elevação", vm.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotPortableIsCalledOut()
    {
        var vm = Vm();
        vm.Elevated = true;
        vm.Portable = false;
        Assert.Contains("portable", vm.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ListenState.Listening, "escutando")]
    [InlineData(ListenState.Idle, "fora de foco")]
    [InlineData(ListenState.Muted, "mudo")]
    [InlineData(ListenState.WaitingForKey, "tecla")]
    public void EachListenStateHasItsOwnWords(ListenState state, string fragment)
    {
        var vm = Vm();
        vm.Elevated = true;
        vm.ListenState = state;
        Assert.Contains(fragment, vm.StateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RaisesPropertyChangedSoTheBindingUpdates()
    {
        var vm = Vm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ActiveElement = "red";

        Assert.Contains(nameof(vm.ActiveElement), changed);
        Assert.Contains(nameof(vm.Summary), changed);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter StatusBarViewModelTests
```

Esperado: erro de compilação — `RonVoice.Tests` não referencia `RonVoice.App`.

- [ ] **Step 3: Referenciar o app nos testes**

```
& $dotnet add RonVoice.Tests reference RonVoice.App
```

- [ ] **Step 4: Escrever a base observável**

Create `RonVoice.App/ViewModels/ObservableBase.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RonVoice.App.ViewModels;

public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}
```

- [ ] **Step 5: Escrever o `StatusBarViewModel`**

Create `RonVoice.App/ViewModels/StatusBarViewModel.cs`:

```csharp
using RonVoice.Core.Pipeline;

namespace RonVoice.App.ViewModels;

/// <summary>
/// A linha que responde "por que não está funcionando" antes de qualquer
/// suporte. As três falhas do sistema — sem elevação, microfone errado, jogo
/// fora de foco — são todas invisíveis; esta barra é onde elas ficam ditas.
/// </summary>
public sealed class StatusBarViewModel : ObservableBase
{
    bool _elevated;
    bool _portable = true;
    string _microphoneName = "(nenhum)";
    string _language = "en";
    string? _activeElement;
    ListenState _listenState = ListenState.Idle;

    public bool Elevated
    {
        get => _elevated;
        set { if (Set(ref _elevated, value)) Raise(nameof(Summary)); }
    }

    public bool Portable
    {
        get => _portable;
        set { if (Set(ref _portable, value)) Raise(nameof(Summary)); }
    }

    public string MicrophoneName
    {
        get => _microphoneName;
        set { if (Set(ref _microphoneName, value)) Raise(nameof(Summary)); }
    }

    public string Language
    {
        get => _language;
        set { if (Set(ref _language, value)) Raise(nameof(Summary)); }
    }

    public string? ActiveElement
    {
        get => _activeElement;
        set { if (Set(ref _activeElement, value)) Raise(nameof(Summary)); }
    }

    public ListenState ListenState
    {
        get => _listenState;
        set
        {
            if (!Set(ref _listenState, value)) return;
            Raise(nameof(StateText));
            Raise(nameof(Summary));
        }
    }

    public string StateText => ListenState switch
    {
        ListenState.Listening => "escutando",
        ListenState.Idle => "jogo fora de foco",
        ListenState.Muted => "mudo",
        ListenState.WaitingForKey => "aguardando a tecla",
        _ => "",
    };

    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                Elevated ? "elevado" : "SEM ELEVAÇÃO — as teclas não chegam ao jogo",
                $"microfone: {MicrophoneName}",
                $"modelo: {Language}",
                StateText,
            };
            if (ActiveElement is { } e) parts.Add($"elemento: {e}");
            if (!Portable) parts.Add("configuração fora da pasta — modo portable desligado");
            return string.Join("   ·   ", parts);
        }
    }
}
```

- [ ] **Step 6: Escrever o `MainViewModel`**

Create `RonVoice.App/ViewModels/MainViewModel.cs`:

```csharp
namespace RonVoice.App.ViewModels;

/// <summary>
/// Dona das abas e da barra de estado. Não contém regra: só junta as peças,
/// porque a §9 do brief proíbe lógica de negócio neste projeto.
/// </summary>
public sealed class MainViewModel : ObservableBase
{
    int _selectedTabIndex;

    public StatusBarViewModel StatusBar { get; } = new();

    /// <summary>0 = Comandos, 1 = Teste, 2 = Configuração. Abre em Comandos.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => Set(ref _selectedTabIndex, value);
    }
}
```

- [ ] **Step 7: Ligar na janela**

Substitua `RonVoice.App/MainWindow.xaml` por uma janela com `TabControl` de três abas
vazias e a barra de estado embaixo, ligada por `{Binding StatusBar.Summary}`. O
`TabControl` liga `SelectedIndex` a `{Binding SelectedTabIndex}`. Em
`MainWindow.xaml.cs`, o construtor faz `DataContext = new MainViewModel();` e nada mais.

- [ ] **Step 8: Rodar**

```
& $dotnet test --filter StatusBarViewModelTests
& $dotnet build
```

Esperado: testes passando, build limpo, e a janela abre mostrando a barra de estado.

- [ ] **Step 9: Commit**

```bash
git add RonVoice.App RonVoice.Tests RonVoice.Tests/RonVoice.Tests.csproj
git commit -m "feat: show elevation, microphone, model and focus in a permanent status bar"
```

---

## Task 9: Aba Comandos

**Files:**
- Create: `RonVoice.App/ViewModels/OrderRowViewModel.cs`, `RonVoice.App/ViewModels/CommandsViewModel.cs`, `RonVoice.App/Views/CommandsView.xaml(.cs)`
- Test: `RonVoice.Tests/CommandsViewModelTests.cs`

**Interfaces:**
- Consumes: `CommandMap`, `OrderDefinition`, `GameExecutable`.
- Produces:
  - `OrderRowViewModel` com `Id`, `Context`, `NeedsVerification`, `PhrasesEn`, `PhrasesPt`, `PathText`
  - `CommandsViewModel(CommandMap map)` com `Search` (get/set), `Groups` → `IReadOnlyList<CommandGroupViewModel>`, `TotalShown`
  - `CommandGroupViewModel` com `Context`, `Orders`

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/CommandsViewModelTests.cs`:

```csharp
using RonVoice.App.ViewModels;
using RonVoice.Core.Commands;

namespace RonVoice.Tests;

public class CommandsViewModelTests
{
    static CommandsViewModel Vm() => new(CommandMap.Load(CommandMapTests.MapPath));

    [Fact]
    public void ShowsEveryOrderWhenTheSearchIsEmpty() =>
        Assert.Equal(70, Vm().TotalShown);

    [Fact]
    public void GroupsByContext()
    {
        var contexts = Vm().Groups.Select(g => g.Context).ToList();
        Assert.Contains("door", contexts);
        Assert.Contains("person", contexts);
        Assert.Equal(contexts.Count, contexts.Distinct().Count());
    }

    [Fact]
    public void FindsAnOrderByAnEnglishPhrase()
    {
        var vm = Vm();
        vm.Search = "flashbang";
        Assert.Contains(vm.Groups.SelectMany(g => g.Orders),
                        o => o.Id == "door.open.flashbang");
    }

    /// <summary>
    /// O catalogo e' a tela inicial porque o primeiro problema de quem instala
    /// e' nao saber o que falar — e ele pode nao falar ingles.
    /// </summary>
    [Fact]
    public void FindsAnOrderByAPortuguesePhrase()
    {
        var vm = Vm();
        vm.Search = "empilha";
        Assert.Contains(vm.Groups.SelectMany(g => g.Orders),
                        o => o.Id.StartsWith("door.stack", StringComparison.Ordinal));
    }

    [Fact]
    public void FindsAnOrderById()
    {
        var vm = Vm();
        vm.Search = "door.disarm";
        Assert.Single(vm.Groups.SelectMany(g => g.Orders));
    }

    [Fact]
    public void SearchIgnoresAccentsAndCase()
    {
        var vm = Vm();
        vm.Search = "POSIÇÃO";
        var withAccent = vm.TotalShown;
        vm.Search = "posicao";
        Assert.Equal(withAccent, vm.TotalShown);
    }

    [Fact]
    public void AnUnmatchedSearchShowsNothingRatherThanEverything()
    {
        var vm = Vm();
        vm.Search = "xyzzy-nao-existe";
        Assert.Equal(0, vm.TotalShown);
    }

    /// <summary>
    /// 25 ordens estao marcadas confidence: verify e podem nao funcionar em jogo.
    /// Sem o selo, viram "esse comando esta quebrado".
    /// </summary>
    [Fact]
    public void FlagsTheOrdersThatWereNeverVerifiedInGame()
    {
        var flagged = Vm().Groups.SelectMany(g => g.Orders).Count(o => o.NeedsVerification);
        Assert.Equal(25, flagged);
    }

    [Fact]
    public void EachRowCarriesBothLanguagesAndTheMenuPath()
    {
        var row = Vm().Groups.SelectMany(g => g.Orders).First(o => o.Id == "door.stack.left");
        Assert.NotEmpty(row.PhrasesEn);
        Assert.NotEmpty(row.PhrasesPt);
        Assert.Equal("MENU 1 2", row.PathText);
    }

    [Fact]
    public void ChangingTheSearchRaisesPropertyChanged()
    {
        var vm = Vm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Search = "stack";

        Assert.Contains(nameof(vm.Groups), changed);
        Assert.Contains(nameof(vm.TotalShown), changed);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter CommandsViewModelTests
```

- [ ] **Step 3: Escrever a linha**

Create `RonVoice.App/ViewModels/OrderRowViewModel.cs`:

```csharp
using RonVoice.Core.Commands;

namespace RonVoice.App.ViewModels;

/// <summary>Uma ordem no catálogo, já no formato que a tela mostra.</summary>
public sealed class OrderRowViewModel(OrderDefinition order)
{
    public string Id => order.Id;
    public string Context => order.Context;

    /// <summary>
    /// As 25 ordens marcadas `confidence: "verify"` nunca foram confirmadas em
    /// jogo. Sem o aviso, quem usar vai concluir que estão quebradas.
    /// </summary>
    public bool NeedsVerification =>
        string.Equals(order.Confidence, "verify", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> PhrasesEn =>
        order.Phrases.TryGetValue("en", out var p) ? p : [];

    public IReadOnlyList<string> PhrasesPt =>
        order.Phrases.TryGetValue("pt", out var p) ? p : [];

    public string PathText => string.Join(' ', order.Path);

    internal IEnumerable<string> SearchableText()
    {
        yield return Id;
        foreach (var p in PhrasesEn) yield return p;
        foreach (var p in PhrasesPt) yield return p;
    }
}

public sealed class CommandGroupViewModel(string context, IReadOnlyList<OrderRowViewModel> orders)
{
    public string Context => context;
    public IReadOnlyList<OrderRowViewModel> Orders => orders;
    public int Count => orders.Count;
}
```

- [ ] **Step 4: Escrever o view model**

Create `RonVoice.App/ViewModels/CommandsViewModel.cs`:

```csharp
using RonVoice.Core.Commands;
using RonVoice.Core.Matching;

namespace RonVoice.App.ViewModels;

/// <summary>
/// A tela inicial. O primeiro problema de quem instala não é depurar
/// reconhecimento: é não saber o que pode falar. São 70 ordens e 770 frases.
/// </summary>
public sealed class CommandsViewModel : ObservableBase
{
    readonly IReadOnlyList<OrderRowViewModel> _all;
    string _search = "";

    public CommandsViewModel(CommandMap map)
    {
        _all = map.Orders.Values
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => new OrderRowViewModel(o))
            .ToList();
        Groups = Group(_all);
    }

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            Groups = Group(Filter(value));
            Raise(nameof(Groups));
            Raise(nameof(TotalShown));
        }
    }

    public IReadOnlyList<CommandGroupViewModel> Groups { get; private set; }

    public int TotalShown => Groups.Sum(g => g.Count);

    IReadOnlyList<OrderRowViewModel> Filter(string search)
    {
        // Mesma normalização do matcher: busca sem acento e sem caixa, para
        // "posição" e "posicao" acharem a mesma coisa.
        var needle = string.Join(' ', TextNormalizer.Tokenize(search));
        if (needle.Length == 0) return _all;

        return _all
            .Where(o => o.SearchableText().Any(t =>
                string.Join(' ', TextNormalizer.Tokenize(t))
                      .Contains(needle, StringComparison.Ordinal)))
            .ToList();
    }

    static IReadOnlyList<CommandGroupViewModel> Group(IReadOnlyList<OrderRowViewModel> rows) =>
        rows.GroupBy(o => o.Context)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CommandGroupViewModel(g.Key, g.ToList()))
            .ToList();
}
```

- [ ] **Step 5: Rodar e ver passar**

```
& $dotnet test --filter CommandsViewModelTests
```

Esperado: 10 testes passando.

- [ ] **Step 6: Escrever a view**

Create `RonVoice.App/Views/CommandsView.xaml`. Estrutura exigida:

- caixa de busca no topo, `Text="{Binding Search, UpdateSourceTrigger=PropertyChanged}"`
- contador ligado a `TotalShown`
- `ItemsControl` sobre `Groups`; cada grupo mostra `Context` e `Count` no cabeçalho
- dentro de cada grupo, uma linha por ordem com: `Id`, `PathText`, as frases dos dois
  idiomas, e um selo visível quando `NeedsVerification` for verdadeiro
- um botão **Enviar ao jogo** por linha, sem funcionalidade nesta tarefa — será ligado na
  tarefa 12

Ligue `CommandsView` na primeira aba do `MainWindow`, com
`DataContext` vindo de `MainViewModel`.

- [ ] **Step 7: Build e conferência visual**

```
& $dotnet build
```

Abra o app e confirme: a aba Comandos abre primeiro, a busca filtra ao digitar, os grupos
aparecem por contexto, e as ordens `verify` estão sinalizadas. Registre no relatório.

- [ ] **Step 8: Commit**

```bash
git add RonVoice.App RonVoice.Tests/CommandsViewModelTests.cs
git commit -m "feat: add a searchable command catalogue as the opening screen"
```

---

## Task 10: Aba Teste

**Files:**
- Create: `RonVoice.App/ViewModels/TestViewModel.cs`, `RonVoice.App/Views/TestView.xaml(.cs)`
- Test: `RonVoice.Tests/TestViewModelTests.cs`

**Interfaces:**
- Consumes: `VoiceTestResult`, `VoiceTestOutcome`, `Intent`.
- Produces: `TestViewModel` com `IsRecording`, `Level`, `Verdict`, `Detail`, `Succeeded`, `Show(VoiceTestResult)`.

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/TestViewModelTests.cs`:

```csharp
using RonVoice.App.ViewModels;
using RonVoice.Core.Matching;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

public class TestViewModelTests
{
    static VoiceTestResult Result(
        VoiceTestOutcome outcome, string heard = "", Intent? intent = null,
        double confidence = 1.0, double peak = 0.5) =>
        new(outcome, heard, confidence, peak, intent);

    /// <summary>
    /// Silencio significa microfone, nao pronuncia. Se o veredito nao disser
    /// isso, a pessoa vai passar a tarde ajustando como fala.
    /// </summary>
    [Fact]
    public void NoAudioPointsAtTheMicrophoneNotThePronunciation()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.NoAudio, peak: 0.0));

        Assert.False(vm.Succeeded);
        Assert.Contains("microfone", vm.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuccessNamesTheOrderThatMatched()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.Success, "stack left",
                       new Intent(null, "door.stack.left", false)));

        Assert.True(vm.Succeeded);
        Assert.Contains("door.stack.left", vm.Verdict);
    }

    [Fact]
    public void OutOfVocabularyPointsAtTheCommandsTab()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.OutOfVocabulary, "the quarterly report"));

        Assert.False(vm.Succeeded);
        Assert.Contains("comando", vm.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LowConfidenceSuggestsSomethingActionable()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.LowConfidence, "stack left", confidence: 0.2));

        Assert.False(vm.Succeeded);
        Assert.Contains("microfone", vm.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoMatchQuotesWhatWasHeardSoThePersonCanSeeTheMisreading()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.NoMatch, "banana pudding clock"));

        Assert.False(vm.Succeeded);
        Assert.Contains("banana pudding clock", vm.Verdict);
    }

    [Fact]
    public void DetailAlwaysCarriesTheRawTextAndConfidence()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.Success, "stack left",
                       new Intent(null, "door.stack.left", false), confidence: 0.87));

        Assert.Contains("stack left", vm.Detail);
        Assert.Contains("0.87", vm.Detail);
    }

    [Fact]
    public void RecordingResetsThePreviousVerdict()
    {
        var vm = new TestViewModel();
        vm.Show(Result(VoiceTestOutcome.Success, "stack left",
                       new Intent(null, "door.stack.left", false)));
        vm.BeginRecording();

        Assert.True(vm.IsRecording);
        Assert.Equal("", vm.Verdict);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter TestViewModelTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.App/ViewModels/TestViewModel.cs`:

```csharp
using RonVoice.Core.Speech;

namespace RonVoice.App.ViewModels;

/// <summary>
/// Traduz o resultado interno do teste para o que a pessoa lê. O veredito diz o
/// que fazer, não o que houve: os nomes internos são termos nossos e não ajudam
/// quem acabou de instalar.
/// </summary>
public sealed class TestViewModel : ObservableBase
{
    bool _isRecording;
    double _level;
    string _verdict = "";
    string _detail = "";
    bool _succeeded;

    public bool IsRecording { get => _isRecording; private set => Set(ref _isRecording, value); }
    public double Level { get => _level; set => Set(ref _level, value); }
    public string Verdict { get => _verdict; private set => Set(ref _verdict, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public bool Succeeded { get => _succeeded; private set => Set(ref _succeeded, value); }

    public void BeginRecording()
    {
        IsRecording = true;
        Verdict = "";
        Detail = "";
        Succeeded = false;
        Level = 0;
    }

    public void Show(VoiceTestResult result)
    {
        IsRecording = false;
        Succeeded = result.Outcome == VoiceTestOutcome.Success;

        Verdict = result.Outcome switch
        {
            VoiceTestOutcome.Success =>
                $"Funcionou: {result.Intent!.OrderId}"
                + (result.Intent.Element is { } el ? $"  (elemento {el})" : "")
                + (result.Intent.Queue ? "  (enfileirada)" : ""),

            VoiceTestOutcome.NoAudio =>
                "Não ouvi nada. Confira o microfone selecionado na aba Configuração "
                + "e o volume de entrada do Windows.",

            VoiceTestOutcome.OutOfVocabulary =>
                "Ouvi você, mas não era um comando conhecido. "
                + "Veja a aba Comandos para as frases aceitas.",

            VoiceTestOutcome.LowConfidence =>
                "Entendi, mas com pouca certeza. Tente falar mais perto do microfone "
                + "ou num ambiente mais silencioso.",

            VoiceTestOutcome.NoMatch =>
                $"Ouvi \"{result.HeardText}\", mas isso não bate com nenhum comando.",

            _ => "",
        };

        Detail = $"texto reconhecido: \"{result.HeardText}\"   ·   "
               + $"confiança: {result.Confidence:0.00}   ·   "
               + $"pico de áudio: {result.PeakLevel:0.00}";
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

```
& $dotnet test --filter TestViewModelTests
```

Esperado: 7 testes passando.

- [ ] **Step 5: Escrever a view**

Create `RonVoice.App/Views/TestView.xaml`. Estrutura exigida:

- um botão grande **Testar minha voz**, que dispara `BeginRecording`
- uma barra de nível ligada a `Level` (0 a 1), visível enquanto `IsRecording`
- o texto de `Verdict` em destaque, com cor diferente conforme `Succeeded`
- o texto de `Detail` menor, abaixo
- uma instrução curta: "clique, fale uma frase de comando, e solte"

O acionamento real do microfone é ligado na tarefa 12; nesta tarefa a view existe e o
view model responde.

- [ ] **Step 6: Commit**

```bash
git add RonVoice.App RonVoice.Tests/TestViewModelTests.cs
git commit -m "feat: turn a voice test into a plain-language verdict"
```

---

## Task 11: Aba Configuração

**Files:**
- Create: `RonVoice.App/ViewModels/SettingsViewModel.cs`, `RonVoice.App/Views/SettingsView.xaml(.cs)`
- Test: `RonVoice.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `SettingsStore`, `GameExecutable`, `KeybindReader`, `WasapiCapture.ListDevices()`.
- Produces: `SettingsViewModel(AppSettings initial, IReadOnlyList<string> devices, IReadOnlyDictionary<string,string> gameBinds)` com as propriedades ligadas, `ToSettings()` → `AppSettings`, `GameWarning`, `PushToTalkWarning`.

- [ ] **Step 1: Escrever os testes que falham**

Create `RonVoice.Tests/SettingsViewModelTests.cs`:

```csharp
using RonVoice.App.ViewModels;
using RonVoice.Core.Config;

namespace RonVoice.Tests;

public class SettingsViewModelTests
{
    static IReadOnlyList<string> Devices() => ["Microfone (WIND)", "CABLE Output", "Voicemeeter"];

    static IReadOnlyDictionary<string, string> Binds() => new Dictionary<string, string>
    {
        ["Crouch"] = "LeftControl",
        ["OpenSwatCommand"] = "MiddleMouseButton",
        ["Walk"] = "LeftShift",
    };

    static SettingsViewModel Vm(AppSettings? initial = null) =>
        new(initial ?? AppSettings.Default, Devices(), Binds());

    [Fact]
    public void StartsFromTheGivenSettings()
    {
        var vm = Vm(AppSettings.Default with { Language = "pt", MicrophoneDevice = 2 });
        Assert.Equal("pt", vm.Language);
        Assert.Equal(2, vm.MicrophoneDevice);
    }

    [Fact]
    public void RoundTripsBackToSettings()
    {
        var vm = Vm();
        vm.Language = "pt";
        vm.MicrophoneDevice = 1;
        vm.ConfidenceThreshold = 0.7;
        vm.UsePushToTalk = true;
        vm.PushToTalkKey = "F8";

        var s = vm.ToSettings();
        Assert.Equal("pt", s.Language);
        Assert.Equal(1, s.MicrophoneDevice);
        Assert.Equal(0.7, s.ConfidenceThreshold);
        Assert.Equal(ListenModeSetting.PushToTalk, s.Mode);
        Assert.Equal("F8", s.PushToTalkKey);
    }

    [Fact]
    public void AlwaysOnIsTheFactoryDefault()
    {
        Assert.False(Vm().UsePushToTalk);
        Assert.Equal(ListenModeSetting.AlwaysOn, Vm().ToSettings().Mode);
    }

    /// <summary>
    /// O nome do processo vem do arquivo escolhido, porque ele varia por loja.
    /// </summary>
    [Fact]
    public void DerivesTheProcessNameFromTheChosenExecutable()
    {
        var vm = Vm();
        vm.GameExecutablePath = @"C:\Steam\ReadyOrNotSteam-Win64-Shipping.exe";
        Assert.Equal("ReadyOrNotSteam-Win64-Shipping", vm.GameProcessName);
    }

    [Fact]
    public void WarnsWhenTheChosenFileDoesNotLookLikeTheGame()
    {
        var vm = Vm();
        vm.GameExecutablePath = @"C:\Windows\notepad.exe";
        Assert.NotNull(vm.GameWarning);
        Assert.Contains("Ready", vm.GameWarning!);
    }

    [Fact]
    public void DoesNotWarnForAPlausibleExecutable()
    {
        var vm = Vm();
        vm.GameExecutablePath = @"C:\Epic\ReadyOrNot-Win64-Shipping.exe";
        Assert.Null(vm.GameWarning);
    }

    /// <summary>
    /// A tecla de PTT nao pode ser uma que o jogo ja usa, ou o jogador agacha
    /// toda vez que fala. Avisa, nao impede: pode ser intencional.
    /// </summary>
    [Fact]
    public void WarnsWhenThePushToTalkKeyCollidesWithAGameBind()
    {
        var vm = Vm();
        vm.UsePushToTalk = true;
        vm.PushToTalkKey = "LeftControl";

        Assert.NotNull(vm.PushToTalkWarning);
        Assert.Contains("Crouch", vm.PushToTalkWarning!);
    }

    [Fact]
    public void DoesNotWarnForAFreeKey()
    {
        var vm = Vm();
        vm.UsePushToTalk = true;
        vm.PushToTalkKey = "F8";
        Assert.Null(vm.PushToTalkWarning);
    }

    [Fact]
    public void NoPushToTalkWarningWhenPushToTalkIsOff()
    {
        var vm = Vm();
        vm.UsePushToTalk = false;
        vm.PushToTalkKey = "LeftControl";
        Assert.Null(vm.PushToTalkWarning);
    }

    [Fact]
    public void ExposesTheDeviceListForTheDropdown() =>
        Assert.Equal(3, Vm().Microphones.Count);
}
```

- [ ] **Step 2: Rodar e ver falhar**

```
& $dotnet test --filter SettingsViewModelTests
```

- [ ] **Step 3: Implementar**

Create `RonVoice.App/ViewModels/SettingsViewModel.cs`:

```csharp
using RonVoice.Core.Config;

namespace RonVoice.App.ViewModels;

public sealed class SettingsViewModel : ObservableBase
{
    readonly IReadOnlyDictionary<string, string> _gameBinds;

    string _language;
    string? _gameExecutablePath;
    int _microphoneDevice;
    bool _usePushToTalk;
    string? _pushToTalkKey;
    double _confidenceThreshold;

    public SettingsViewModel(
        AppSettings initial,
        IReadOnlyList<string> devices,
        IReadOnlyDictionary<string, string> gameBinds)
    {
        _gameBinds = gameBinds;
        Microphones = devices;

        _language = initial.Language;
        _gameExecutablePath = initial.GameExecutablePath;
        _microphoneDevice = initial.MicrophoneDevice;
        _usePushToTalk = initial.Mode == ListenModeSetting.PushToTalk;
        _pushToTalkKey = initial.PushToTalkKey;
        _confidenceThreshold = initial.ConfidenceThreshold;
    }

    public IReadOnlyList<string> Microphones { get; }

    public string Language { get => _language; set => Set(ref _language, value); }

    public int MicrophoneDevice
    {
        get => _microphoneDevice;
        set => Set(ref _microphoneDevice, value);
    }

    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set => Set(ref _confidenceThreshold, value);
    }

    public string? GameExecutablePath
    {
        get => _gameExecutablePath;
        set
        {
            if (!Set(ref _gameExecutablePath, value)) return;
            Raise(nameof(GameProcessName));
            Raise(nameof(GameWarning));
        }
    }

    /// <summary>Vem do arquivo escolhido: o nome varia por loja.</summary>
    public string? GameProcessName =>
        string.IsNullOrWhiteSpace(GameExecutablePath)
            ? null
            : GameExecutable.ProcessNameOf(GameExecutablePath);

    public string? GameWarning =>
        string.IsNullOrWhiteSpace(GameExecutablePath)
            || GameExecutable.LooksLikeReadyOrNot(GameExecutablePath)
                ? null
                : "Esse arquivo não parece ser o Ready or Not. Se for mesmo, pode ignorar.";

    public bool UsePushToTalk
    {
        get => _usePushToTalk;
        set { if (Set(ref _usePushToTalk, value)) Raise(nameof(PushToTalkWarning)); }
    }

    public string? PushToTalkKey
    {
        get => _pushToTalkKey;
        set { if (Set(ref _pushToTalkKey, value)) Raise(nameof(PushToTalkWarning)); }
    }

    /// <summary>
    /// Avisa se a tecla escolhida já é usada pelo jogo — senão o jogador agacha
    /// toda vez que fala. Avisa, não impede: pode ser intencional.
    /// </summary>
    public string? PushToTalkWarning
    {
        get
        {
            if (!UsePushToTalk || string.IsNullOrWhiteSpace(PushToTalkKey)) return null;

            var clash = _gameBinds
                .FirstOrDefault(b => string.Equals(
                    b.Value, PushToTalkKey, StringComparison.OrdinalIgnoreCase));

            return clash.Key is null
                ? null
                : $"O jogo já usa essa tecla para {clash.Key}.";
        }
    }

    public AppSettings ToSettings() => new(
        Language,
        GameExecutablePath,
        MicrophoneDevice,
        UsePushToTalk ? ListenModeSetting.PushToTalk : ListenModeSetting.AlwaysOn,
        PushToTalkKey,
        ConfidenceThreshold);
}
```

- [ ] **Step 4: Rodar e ver passar**

```
& $dotnet test --filter SettingsViewModelTests
```

Esperado: 11 testes passando.

- [ ] **Step 5: Escrever a view**

Create `RonVoice.App/Views/SettingsView.xaml`. Campos exigidos:

- executável do jogo: caixa de texto somente leitura mais botão que abre
  `Microsoft.Win32.OpenFileDialog` com filtro `*.exe`; abaixo, o `GameProcessName` derivado
  e o `GameWarning` quando houver
- microfone: `ComboBox` sobre `Microphones`, `SelectedIndex` ligado a `MicrophoneDevice`
- idioma: `ComboBox` com `en` e `pt`
- modo de escuta: `CheckBox` "Usar push-to-talk" ligado a `UsePushToTalk`; quando marcado,
  aparece o campo da tecla e o `PushToTalkWarning`
- limiar: `Slider` de 0 a 1, passo 0.05, ligado a `ConfidenceThreshold`, com o valor ao lado
- botão **Salvar**, ligado na tarefa 12

- [ ] **Step 6: Commit**

```bash
git add RonVoice.App RonVoice.Tests/SettingsViewModelTests.cs
git commit -m "feat: configure game, microphone, language and listening mode"
```

---

## Task 12: Ligar tudo — pipeline, bandeja, primeira execução e os dois botões

A tarefa que transforma as peças em aplicativo.

**Files:**
- Create: `RonVoice.App/ViewModels/FirstRunViewModel.cs`, `RonVoice.App/Views/FirstRunView.xaml(.cs)`, `RonVoice.App/ElementHook.cs`
- Modify: `RonVoice.App/App.xaml.cs`, `MainViewModel`, `MainWindow.xaml.cs`, `CommandsView.xaml.cs`, `TestView.xaml.cs`, `SettingsView.xaml.cs`

**Interfaces:**
- Consumes: tudo das tarefas anteriores.
- Produces: o aplicativo funcionando.

- [ ] **Step 1: Primeira execução**

`FirstRunViewModel` expõe `Progress` (0 a 1), `StatusText`, `Failed`, `ErrorMessage`, e
`DownloadAsync(string language, string modelsDir, CancellationToken)` que chama
`ModelDownloader.DownloadAsync` repassando o progresso.

`App.xaml.cs`, ao iniciar: se `ModelLocator.FindModelsDirectory()` não achar o modelo do
idioma configurado, mostra `FirstRunView` antes da `MainWindow`. Falhando o download,
mostra o erro e oferece tentar de novo — não fecha.

- [ ] **Step 2: Montar o pipeline**

Em `App.xaml.cs`, depois da primeira execução:

```csharp
var (settings, settingsPath, portable) = SettingsStore.Load();
var map = CommandMap.Load(Path.Combine(AppContext.BaseDirectory, "data", "ron_commands.json"));
var binds = KeybindReader.FindDefaultIniPath() is { } ini
    ? KeybindReader.Read(ini)
    : new Dictionary<string, string>();

string[]? processNames = settings.GameExecutablePath is { } exe
    ? [GameExecutable.ProcessNameOf(exe)]
    : null;

var gate = new ListenGate(
    () => ForegroundGuard.IsGameForeground(processNames),
    isMuted: null,
    mode: settings.Mode == ListenModeSetting.PushToTalk
        ? ListenMode.PushToTalk : ListenMode.AlwaysOn);
```

O motor, o pipeline e a captura são construídos como no `ListenCommand` da etapa 5. A
`StatusBarViewModel` assina `gate.StateChanged`, e `Elevated` vem de
`ForegroundGuard.IsElevated()`, `Portable` do `SettingsStore.Load`.

- [ ] **Step 3: Indicador de elemento ativo**

Create `RonVoice.App/ElementHook.cs`: um hook global de teclado (`WH_KEYBOARD_LL`) que
observa as teclas ligadas a `SelectElementGold/Blue/Red` nos binds lidos, e publica o
elemento correspondente.

Motivo, da §5.5 do brief: o jogador pode apertar `F5`/`F6`/`F7` direto no teclado, e sem
observar isso o indicador dessincroniza.

Ligue a `StatusBarViewModel.ActiveElement`.

- [ ] **Step 4: Botão "Enviar ao jogo"**

No `CommandsView`, o botão de cada linha:

1. verifica se o processo do jogo está rodando; se não, fica desabilitado com o motivo em
   tooltip
2. minimiza a janela e traz o jogo para o primeiro plano
3. conta três segundos visíveis, com opção de cancelar
4. resolve a ordem pelo `CommandResolver` e envia pelo `SendInputSender`

O passo 2 é o que faz a funcionalidade existir: ao clicar, quem está em foco é a janela do
app, e o `ForegroundGuard` — corretamente — recusaria.

- [ ] **Step 5: Botão "Testar minha voz"**

No `TestView`:

1. `gate.TestBypass = true` — sem isso o portão recusa todo o áudio, porque quem está em
   foco é a janela do app
2. cria um `VoiceTestRunner` com o motor e o matcher correntes
3. liga `runner.LevelChanged` a `TestViewModel.Level`
4. alimenta o runner com o áudio da captura enquanto grava
5. ao parar, chama `runner.Finish()` e passa o resultado a `TestViewModel.Show`
6. `gate.TestBypass = false`

**Nada é enviado ao jogo neste caminho.**

- [ ] **Step 6: Salvar configuração**

O botão Salvar chama `SettingsStore.Save(vm.ToSettings(), settingsPath)` e aplica o que dá
para aplicar a quente: modo de escuta, limiar, microfone. Trocar o idioma recria modelo e
reconhecedor, com a UI em estado ocupado.

- [ ] **Step 7: Bandeja**

`TrayIcon` mostra `ListenState`. Fechar a janela minimiza para a bandeja em vez de sair;
sair é pelo menu da bandeja. `GlobalHotkey` com `Ctrl+Alt+M` alterna o mute.

- [ ] **Step 8: Verificar tudo**

```
& $dotnet build
& $dotnet test
```

Depois, manualmente, com o jogo **fechado**:

- o app pede UAC ao abrir
- a aba Comandos abre primeiro e a busca funciona
- a barra de estado mostra "jogo fora de foco"
- "Testar minha voz" grava, a barra de nível se mexe ao falar, e o veredito aparece
- `Ctrl+Alt+M` alterna para mudo
- fechar a janela manda para a bandeja

Com o jogo **aberto**: a barra vira "escutando", "Enviar ao jogo" fica habilitado, e apertar
`F5`/`F6`/`F7` no teclado muda o elemento na barra.

Registre cada item no relatório.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: wire the app together with first run, tray and both action buttons"
```

---

## Publicação portable

- [ ] **Gerar a pasta**

```
& $dotnet publish RonVoice.App -c Release -r win-x64 --self-contained true -o dist-app
& $dotnet publish RonVoice.Cli -c Release -r win-x64 --self-contained true -o dist-app
```

Os dois na mesma pasta compartilham as DLLs do runtime.

- [ ] **Conferir que é portable de verdade**

Copie `dist-app` para outra pasta, rode, e confirme que `settings.json` é criado ao lado do
executável e que o app abre. Sem os modelos, deve mostrar a tela de primeira execução.

---

## Self-Review

**Cobertura da spec:**

| Seção da spec | Onde é implementada |
|---|---|
| 2 decisões do autor | Tasks 1, 2, 3, 11 |
| 3 portable, onde as coisas ficam | Task 1 |
| 4 elevação por manifesto | Task 7 |
| 5.1 barra de estado | Task 8; elemento ativo na Task 12 |
| 5.2 aba Comandos | Task 9; botão Enviar ao jogo na Task 12 |
| 5.3 aba Teste | Tasks 6 e 10; acionamento na Task 12 |
| 5.4 aba Configuração | Task 11 |
| 5.5 primeira execução | Tasks 5 e 12 |
| 6 arquitetura | Tasks 7 a 12 |
| 7 tratamento de erro | Tasks 1, 5, 11, 12 |
| 8 testes | Tasks 1–6, 8–11 |
| 10 critérios de pronto | Task 12 e a seção de publicação |

Sem lacunas.

**Consistência de tipos:** `AppSettings` é criado na Task 1 e consumido igual nas 11 e 12.
`ListenMode`/`ListenState` da Task 3 são usados nas 8 e 12. `VoiceTestResult` da Task 6 é
consumido na 10 com os mesmos campos. `ObservableBase` da Task 8 é a base de todos os view
models seguintes. `RelayCommand` da Task 7 é usado nas views.

**Riscos registrados de propósito:**

- **A Task 7 remove o projeto `RonVoice.Tray`.** É a decisão de não manter dois executáveis
  disputando o mesmo ícone de bandeja. O código de `TrayIcon` e `GlobalHotkey` é movido, não
  reescrito.
- **XAML não tem teste automatizado.** As tarefas 9, 10, 11 e 12 terminam com conferência
  manual registrada no relatório. É o mesmo tratamento que a bandeja recebeu na etapa 5.
- **A elevação por manifesto só é observável pelo `.exe` publicado**, não por `dotnet run`.
  A Task 7 diz isso explicitamente, porque testar pelo caminho errado daria falso negativo.
