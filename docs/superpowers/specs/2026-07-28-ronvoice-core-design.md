# RonVoice — design do núcleo (etapas 1–4)

Data: 2026-07-28
Fonte de requisitos: `BRIEF.md`
Status: aprovado para virar plano de implementação

Documento em português; código, identificadores e commits em inglês.

---

## 1. Escopo

Esta spec cobre as **etapas 1 a 4** da seção 7 do brief:

1. `CommandMap` + `KeybindReader`
2. `PhraseMatcher`
3. `RonVoice.Cli`
4. `SendInputSender`

Fora de escopo, para specs posteriores: `VoskSpeechEngine` + `GrammarBuilder` (etapa 5)
e `RonVoice.App` em WPF (etapa 6).

O critério de pronto do conjunto é o da etapa 4 do brief: `ronvoice send "stack up"`
movimenta o menu no jogo de verdade. O escopo foi cortado aqui porque as etapas 1–4
concentram todo o conhecimento não-óbvio da seção 5 do brief, e é onde uma premissa
errada custa mais caro se descoberta tarde.

---

## 2. Correções ao BRIEF.md

Levantadas antes de implementar, como a introdução do brief pede. Nenhuma foi
alterada silenciosamente.

### 2.1 Caminho do `Input.ini` (§5.7)

O brief aponta para `%LOCALAPPDATA%\ReadyOrNot\Saved\Config\WindowsNoEditor\Input.ini`,
que é a convenção do Unreal Engine 4. O jogo migrou para UE5 e o arquivo real está em:

```
%LOCALAPPDATA%\ReadyOrNot\Saved\Config\Windows\Input.ini
```

Verificado na máquina de desenvolvimento: existe, 25 606 bytes, seção única
`[/Script/Engine.InputSettings]`.

**Decisão:** o `KeybindReader` tenta `Windows\` primeiro e `WindowsNoEditor\` como
segundo caminho, para não quebrar em instalações antigas. O caminho também é
configurável, como o brief já previa.

### 2.2 Os dígitos do menu são rebindáveis (§5.7)

O `Input.ini` real expõe `SwatInputKeyOne` … `SwatInputKeyNine` como ações
independentes. Tratar os dígitos do `path` como teclas numéricas literais viola a
§5.7 pelo mesmo motivo que ela existe — e o bug seria invisível em qualquer máquina
com binds padrão.

**Decisão:** todo token do `path` resolve por *ActionName*, dígitos incluídos.

### 2.3 O `MENU` de fechamento pertence à fila, não à ordem (§5.4)

Descompilando `Ready or Not v5.1.vap` com `vapdec.py`, o comando primitivo
`Context Menu Button Close` é byte-a-byte idêntico a `Context Menu Button`
(clique do meio, 0,1 s de hold) e aparece **exclusivamente dentro do ramo
`if queue`**, imediatamente após `Hold Command Release`:

```
Context Menu Button                    <- abre
[if queue] Hold Command Press          <- LShift down
Command Key 8                          <- última tecla
[if queue] Hold Command Release        <- LShift up
[if queue] Context Menu Button Close   <- fecha
```

Ele está presente em 12 dos 38 comandos com suporte a fila. Os 26 restantes — quase
todos os breach de caminho longo — não têm. A explicação mecânica plausível é que,
com o go-code segurado, o menu não fecha sozinho na seleção.

Além disso, **nenhuma das 70 ordens do `ron_commands.json` termina com `MENU`**, então
o comportamento descrito no brief nunca dispararia.

**Decisão:** o fechamento vira um campo booleano `close_menu` por ordem, aplicado
somente quando `queue == true`. O token `MENU` no fim de um `path` continua sendo
respeitado se algum dia aparecer, mas não é o mecanismo principal.

### 2.4 O hold do clique do meio é 100 ms, não 35 ms (§5.2)

No perfil original, `Command Key N` e `Select X Element` usam `0.033s`, mas
`Context Menu Button` e `Context Menu Button Close` usam `0.1s`.

**Decisão:** o clique do menu usa hold de 100 ms. O `menu_open_settle_ms: 60` do JSON
é o *gap* depois do clique, não o hold — os dois convivem.

### 2.5 `path` não identifica uma ordem

Descoberto ao tentar semear o `close_menu`: o menu do jogo é contextual, então o mesmo
caminho de teclas significa coisas diferentes.

| `path` | ordens que colidem |
|---|---|
| `MENU 1` | `move.to` (default) · `person.restrain` (person) |
| `MENU 2 2` | `move.fallin` (default) · `door.open.flashbang` (door) |
| `MENU 6` | `door.wedge` (door) · `search` (default) |
| `MENU 1 3` | `door.stack.right` (door) · `door.disarm` (door) — mesmo contexto |

**Decisão:** nenhum índice, cache ou tabela do sistema pode usar `path` como chave.
A chave é sempre `id`.

### 2.6 O caso 5 da §8 do brief está errado

O brief espera `blue team prep breach and clear` → `door.open.clear`. As frases reais
de `door.open.clear` são `open and clear`, `open clear`, `open go dynamic`,
`move in and clear`, `move in clear` — nenhuma contém "breach".

Pontuando `breach and clear` contra o catálogo inteiro:

```
0.775  door.breach.leader.clear   <breach for me and clear>
0.770  door.breach.leader.clear   <breach and wait and clear>
0.667  door.breach.leader.gas     <breach and wait gas and clear>
```

`door.open.clear` não aparece no top-6. No jogo, *breach* (arrombar) e *open* (abrir)
são ramos diferentes do menu, então o mapa está certo e a expectativa do brief parece
ter sido escrita de memória.

**Decisão:** o caso passa a esperar `door.breach.leader.clear` (`path` `MENU 3 5 1`).
O `ron_commands.json` não muda por causa disso.

### 2.7 O mapa tem frases duplicadas entre ordens diferentes

Cinco frases apontam para dois ids distintos e são indistinguíveis após normalização.
Nenhum algoritmo resolve isso — é defeito de dados:

| frase | ordem A | ordem B |
|---|---|---|
| `drop chemlight` / `drop a chemlight` | `deploy.chemlight` (verify) | `player.chemlight` (confirmed) |
| `solta luz` / `solta a luz` | `deploy.chemlight` (verify) | `player.chemlight` (confirmed) |
| `para` (pt) | `hold` (confirmed) | `player.yell` (confirmed) |
| `go` / `go go go` | `move.to` (confirmed) | `confirm.default` (confirmed) |
| `leader and clear` / `leader leader and clear` | `door.breach.leader.clear` (confirmed) | `door.breach.leader.leader` (verify) |

**Decisão:** remover a frase duplicada da ordem menos provável. Critérios, nesta ordem:
preservar a ordem `confirmed` sobre a `verify`; preservar a frase idiomática. Cinco
remoções pontuais, listadas na seção 10. Nenhuma ordem é regenerada ou reestruturada.

---

## 3. Estrutura

```
RonVoice.Core/            net10.0-windows, sem UseWPF, sem UseWindowsForms
  Commands/
    CommandMap.cs         ron_commands.json -> IReadOnlyDictionary<string, OrderDefinition>
    OrderDefinition.cs    record
    KeybindReader.cs      Input.ini -> IReadOnlyDictionary<string, string>  (ActionName -> UeKeyName)
    ActionNames.cs        token do path -> ActionName            (tabela estática)
    KeyCatalog.cs         UeKeyName -> InputToken                (tabela estática)
    CommandResolver.cs    Intent + binds -> KeySequence
  Matching/
    Intent.cs             record
    TextNormalizer.cs
    PhraseMatcher.cs      string -> Intent?
  Input/
    InputToken.cs         ScanCode | MouseButton
    KeyStep.cs            record
    KeySequence.cs
    IInputSender.cs
    SendInputSender.cs
    ForegroundGuard.cs

RonVoice.Cli/             net10.0-windows
RonVoice.Tests/           net10.0-windows, xUnit
  corpus/en.tsv
  corpus/pt.tsv
  fixtures/*.ini

data/
  ron_commands.json       movido da raiz do repositório
```

`RonVoice.Core` não referencia WPF nem `System.Windows`. O TFM é `-windows` porque o
projeto é Windows-only por decisão fechada e o `SendInputSender` é P/Invoke puro; isso
não arrasta nenhuma dependência de UI.

---

## 4. Contratos

### 4.1 `OrderDefinition`

```csharp
public sealed record OrderDefinition(
    string Id,
    string Context,          // "door" | "person" | "default" | "any" — informativo
    IReadOnlyList<string> Path,
    bool CloseMenu,
    string Confidence,       // "confirmed" | "verify"
    IReadOnlyDictionary<string, IReadOnlyList<string>> Phrases);  // "en" | "pt"
```

`Context` é carregado e exposto para a UI, mas **não participa de nenhuma decisão**
(§9 do brief).

### 4.2 `Intent`

```csharp
public sealed record Intent(string? Element, string? OrderId, bool Queue);
```

Invariante: `Element` e `OrderId` não são ambos nulos. Um `Intent` com apenas
`Element` é válido e produz somente a tecla de seleção — é o que faz `"red team"`
dito sozinho funcionar (§5.5).

### 4.3 `InputToken`

```csharp
public abstract record InputToken;
public sealed record ScanCodeToken(ushort Scan, bool Extended) : InputToken;
public sealed record MouseToken(MouseButton Button)            : InputToken;
```

Tecla e botão de mouse são tipos distintos porque geram estruturas `INPUT` diferentes.
Colapsar os dois num `ushort` é a forma silenciosa de errar a §5.1.

### 4.4 `KeyStep` e `KeySequence`

```csharp
public enum StepKind { Press, Down, Up }

public sealed record KeyStep(
    StepKind Kind,
    InputToken Token,
    int HoldMs,        // ignorado quando Kind != Press
    int GapAfterMs);

public sealed record KeySequence(IReadOnlyList<KeyStep> Steps);
```

`KeySequence` é dado puro: carrega o tempo, não o executa. Isso torna a §5.2 testável
sem tocar em Win32.

`Down` e `Up` existem exclusivamente para o LShift da fila. Nenhum outro passo os usa.

### 4.5 `IInputSender`

```csharp
public interface IInputSender
{
    void Send(KeySequence sequence, CancellationToken ct = default);
}
```

---

## 5. Resolução de teclas

Cadeia, do token do `path` até o `INPUT`:

```
token  ->  ActionName  ->  bind do Input.ini  ->  nome de tecla UE  ->  InputToken
                                    |
                                    +-- ausente ou "None" -> keybind_defaults
```

### 5.1 `ActionNames` — tabela token → ActionName

| token do JSON | ActionName | chave em `keybind_defaults` |
|---|---|---|
| `MENU` | `OpenSwatCommand` | `swat_command_menu` |
| `1` … `9` | `SwatInputKeyOne` … `Nine` | `command_keys[n-1]` |
| `KEY:DEFAULT_COMMAND` | `IssueDefaultCommand` | `default_command` |
| `KEY:INTERACT` | `Use` | `interact_yell` |
| `KEY:X` | `FireSelect` | — (literal `X`) |
| `KEY:C` | `DropChem` | — (literal `C`) |
| `KEY:PAGEUP` | `VoteYes` | — (literal `PageUp`) |
| — (modificador de fila) | `HoldGoCode` | `hold_command` |
| — (elemento `gold`) | `SelectElementGold` | `select_gold` |
| — (elemento `blue`) | `SelectElementBlue` | `select_blue` |
| — (elemento `red`) | `SelectElementRed` | `select_red` |

Tokens `KEY:` sem ActionName conhecido são tratados como nome de tecla literal e vão
direto para o `KeyCatalog`.

### 5.2 `KeybindReader`

Lê apenas linhas `ActionMappings=(ActionName="X",...,Key=Y)` da seção
`[/Script/Engine.InputSettings]`. Descarta entradas de gamepad e VR (`Gamepad_*`,
`OculusTouch_*`, `Vive_*`, `ValveIndex_*`, `MixedReality_*`, `MotionController_*`).

Uma ação pode aparecer mais de uma vez (`Fire` tem gamepad e mouse). Vence o primeiro
bind de teclado/mouse encontrado.

Devolve `ActionName -> UeKeyName` e nada mais. Não conhece ordens, não conhece `MENU`.

### 5.3 `KeyCatalog`

Tabela estática de nome de tecla UE para `InputToken`. Cobre letras, dígitos, F1–F12,
numpad, modificadores, setas, botões de mouse e as teclas nomeadas que aparecem no
arquivo real (`SpaceBar`, `BackSpace`, `CapsLock`, `PageUp`, `PageDown`, `Delete`,
`Divide`, `ThumbMouseButton`, `ThumbMouseButton2`, …).

Scan codes são do conjunto 1 (set 1), com `Extended` marcado onde o prefixo `E0` é
necessário — setas, `Delete`, `PageUp`/`PageDown`, `RightAlt`, `RightControl`, numpad
`Divide` e `Enter`.

Nome desconhecido não vira palpite: a resolução falha e nomeia a tecla no erro.

### 5.4 `CommandResolver`

```
resolve(Intent, CommandMap, binds) -> KeySequence | erro
```

Montagem:

1. Se `Intent.Element` != null → `Press` da tecla do elemento, hold 35, gap 35.
2. Se `Intent.OrderId` == null → termina aqui.
3. Para cada token do `path`, exceto o último:
   - `MENU` → `Press` mouse do meio, hold 100, gap 60 (`menu_open_settle_ms`)
   - demais → `Press`, hold 35, gap 35
4. Último token:
   - se `Queue` → `Down` LShift · `Press` último, hold 35, gap 35 · `Up` LShift
   - senão → `Press` último, hold 35, gap 35
5. Se `Queue && order.CloseMenu` → `Press` mouse do meio, hold 100.

Exemplo — `"red team, prep open with flashbang"`, ordem `door.open.flashbang`
(`MENU 2 2`), que está na semente de `close_menu` da seção 10.2:

```
Press  ScanCode(F7)        hold  35   gap 35     <- elemento
Press  Mouse(Middle)       hold 100   gap 60     <- abre o menu
Press  ScanCode(2)         hold  35   gap 35     <- caminho
Down   ScanCode(LShift)
Press  ScanCode(2)         hold  35   gap 35     <- última tecla, envolvida
Up     ScanCode(LShift)
Press  Mouse(Middle)       hold 100              <- fecha: só porque queue && close_menu
```

É a mesma sequência do exemplo de abertura do brief (`F7 MMB 2 2`), acrescida do
envelope de fila.

---

## 6. `PhraseMatcher`

Entra string, sai `Intent?`. Sem estado entre chamadas.

### 6.1 Normalização

Minúsculas, remoção de diacríticos, remoção de pontuação, colapso de espaços.
`"Red team, open the door!"` → `"red team open the door"`.

### 6.2 Extração de elemento — casamento mais longo primeiro

`team` é alias de `gold` e é substring de `red team` e `blue team`. Varredura alias a
alias na ordem do JSON faria `"red team"` resolver para **gold**, mandando F5 no lugar
de F7 — e o caso 1 da §8 do brief falharia sem erro visível.

Regra: ordenar todos os aliases de todos os elementos por número de palavras
decrescente e casar o primeiro que couber na sequência de tokens. Removê-lo do texto.

### 6.3 Extração de fila — dois candidatos

`hold` é alias do modificador de fila **e** é a frase da ordem `hold`. Mesma colisão em
`hold position`, `hold up`, `segura posicao`, `espera ai`.

Remoção gulosa do modificador destruiria a ordem `hold`. Regra: montar dois candidatos e
pontuar ambos.

| entrada | candidato com fila | candidato sem fila | vencedor |
|---|---|---|---|
| `hold` | `""` → descartado | `hold` → **1.000** `hold` | sem fila |
| `prep open and clear` | `open and clear` → **1.000** `door.open.clear` | `prep open and clear` → 0.593 | com fila |

Números medidos contra o mapa real, não estimados.

Empate exato desempata **a favor da fila**: enfileirar o que era para executar deixa os
NPCs parados e o jogador percebe; executar o que era para enfileirar arromba a porta cedo.

### 6.4 Pontuação: F1 ponderado por IDF, stopwords por idioma

Fórmulas simples de sobreposição de tokens **não funcionam** neste catálogo. Testadas
contra os casos da §8 do brief, jaccard, `|∩|/max`, cobertura e F1 puro falham todas no
caso 1, porque `open the door` é literalmente uma frase de `door.toggle` e empata com
`open with flashbang` de `door.open.flashbang`.

O que resolve é pesar cada token pelo inverso da sua frequência no catálogo — `flashbang`
discrimina, `door` não:

```
peso(t)  = log(1 + N / (1 + df(t)))      N = frases do idioma, df = em quantas ocorre
score    = 2 · Σ peso(A ∩ B) / (Σ peso(A) + Σ peso(B))       sobre conjuntos de tokens
```

Com isso, `open the door with flashbang` dá `0.839` para `door.open.flashbang` contra
`0.711` para `door.toggle` — margem `0.128`, folgada sobre o portão de `0.05`.

(Medido no mapa já com as remoções da seção 10.1. Antes delas os valores eram
`0.832` e `0.728`: tirar frases muda as frequências e portanto os pesos IDF.)

**Stopwords são por idioma, nunca uma lista só.** `do` é artigo em português (*de+o*) e
verbo em inglês; uma lista compartilhada esvazia a frase inglesa `do it`, que passa a
pontuar zero contra si mesma. Listas:

```
en: the a an and to of on it that for
pt: o a os as e de do da no na um uma que
```

`with` e `com` **não** são stopwords: são o que separa `open with flashbang` de
`open the door`.

Se a filtragem esvaziar o conjunto (frase composta só de stopwords), usa-se o conjunto
cru de tokens.

### 6.5 Limiar e portão de ambiguidade

Varredura de limiar × margem sobre as 775 frases do mapa **antes** das remoções da
seção 2.7, com um subconjunto de 10 casos adversariais:

| margem | casos (de 10) | EN acerto/rejeita/**errada** | PT acerto/rejeita/**errada** |
|---|---|---|---|
| 0.00 | 10 | 398 / 1 / **3** | 371 / 0 / **2** |
| **0.05** | **10** | 394 / 8 / 0 | 369 / 4 / 0 |
| 0.10 | 9 | 394 / 8 / 0 | 369 / 4 / 0 |
| 0.15 | 7 | 361 / 41 / 0 | 352 / 21 / 0 |

O limiar é irrelevante entre `0.60` e `0.80` — as linhas são idênticas, porque um
casamento correto pontua alto e ruído pontua ~0. **A margem é o único parâmetro que
importa**, e ela paga o próprio custo: sem ela o matcher manda 5 ordens erradas.

- **Limiar `0.60`** — piso contra ruído. Exposto na config, mas não é o botão útil.
- **Margem `0.05`** — se a melhor pontuação não superar a segunda de ordem diferente
  por essa margem, rejeita.

Aplicadas as cinco remoções da seção 2.7, o resultado final é:

```
en: 399 frases | acerto 399 | rejeitada 0 | ERRADA 0
pt: 371 frases | acerto 371 | rejeitada 0 | ERRADA 0
cobertura: 70/70 ordens alcançáveis em cada idioma
```

Estes números são o critério de pronto da etapa 2 e devem ser reproduzidos pelos testes.

### 6.6 Saída

- casou ordem → `Intent(element, orderId, queue)`
- só elemento → `Intent(element, null, false)`
- nada → `null`

---

## 7. Tratamento de erro

Princípio único: **resolução incerta não envia nada.** O sistema nunca chuta tecla.

| Situação | Comportamento |
|---|---|
| `Input.ini` ausente nos dois caminhos | usa `keybind_defaults` inteiro; aviso alto no CLI |
| ActionName ausente do arquivo | cai no default daquele token |
| `Key=None` | cai no default; sem default, **rejeita a ordem** |
| nome de tecla UE fora do `KeyCatalog` | rejeita a ordem e nomeia a tecla no erro |
| ordem não encontrada no `CommandMap` | rejeita |
| jogo fora de foco | descarta, exceto com `--force` |
| processo não elevado | detecta no startup e avisa; não envia às cegas |

O caso `Key=None` não é hipotético: na máquina de desenvolvimento, `Yell=None`.

---

## 8. Testes

`PhraseMatcher` é o único componente com regra de negócio real e leva o peso dos testes.
Os demais são adaptadores finos.

### 8.1 Corpus

Formato do brief: `frase<TAB>orderId<TAB>element<TAB>queue`, em
`RonVoice.Tests/corpus/{en,pt}.tsv`.

**Corpus gerado** — uma linha por frase do `ron_commands.json` (399 en, 371 pt após as
remoções da seção 2.7), afirmando que cada frase resolve para a própria ordem. Rede de
regressão para normalização, stopwords e colisão nova de alias quando o mapa mudar.
Gerado por `ronvoice corpus` e versionado.

Duas asserções agregadas acompanham o corpus gerado, e são o que realmente protege o
sistema:

- **zero frases resolvendo para a ordem errada** — é a falha que compromete a missão;
- **cobertura 70/70 em cada idioma** — nenhuma ordem pode ficar inalcançável por causa
  de uma remoção de frase ou de um ajuste de margem.

**Corpus adversarial, escrito à mão** — os 6 casos da §8 do brief (com o caso 5
corrigido conforme a seção 2.6), mais os que este design descobriu:

```
red team, open the door with flashbang   door.open.flashbang        red    false
open the door with flashbang             door.open.flashbang        -      false
red team                                 -                          red    false
stack up left                            door.stack.left            -      false
blue team prep breach and clear          door.breach.leader.clear   blue   true
banana pudim relógio                     -                          -      false
hold                                     hold                       -      false
hold position                            hold                       -      false
gold team hold                           hold                       gold   false
team                                     -                          gold   false
red team hold up                         hold                       red    false
do it                                    confirm.default            -      false
go go go                                 confirm.default            -      false
time vermelho abre com flash             door.open.flashbang        red    false
azul prepara empilha a esquerda          door.stack.left            blue   true
```

Cada linha existe por um motivo registrado: `team` isolado prova o casamento mais longo
da §6.2; `hold` e `red team hold up` provam o backtracking da §6.3; `do it` prova as
stopwords por idioma da §6.4; as duas últimas mantêm o modo português vivo, incluindo
elemento e fila em PT.

### 8.2 Demais componentes

- **`CommandResolver`** — golden tests com binds fixos em fixture, comparando a
  `KeySequence` inteira (tokens, holds, gaps) para: sem elemento; com elemento;
  enfileirada com `close_menu`; enfileirada sem `close_menu`; token `KEY:`;
  mistura mouse + tecla.
- **`KeybindReader`** — fixtures: o `Input.ini` real copiado, um sem as chaves de SWAT,
  um com `Key=None`, e caminho inexistente.
- **`CommandMap`** — carrega 70 ordens; todo token de `path` resolve; nenhum id duplicado.
- **`SendInputSender`** — sem teste unitário. Em vez disso, `ronvoice send --dry-run`
  imprime o array de `INPUT` exato que sairia. A validação real é no jogo.

---

## 9. CLI

```
ronvoice test "<frase>" [--lang en|pt]      intent casado + KeySequence resolvida
ronvoice send "<frase>" [--dry-run] [--force]
ronvoice keymap                             imprime a tabela de binds resolvida
ronvoice corpus                             regenera corpus/{en,pt}.tsv do JSON
```

`test` não toca em Win32 e não precisa do jogo — é a ferramenta de depuração central do
projeto e deve existir antes do `SendInputSender`.

`send` exige o Ready or Not em primeiro plano por padrão; `--force` permite mandar para
a janela em foco, para testar num editor de texto.

---

## 10. Alterações no `ron_commands.json`

Duas mudanças, ambas pontuais. Nenhuma ordem é regenerada ou reestruturada, nenhuma
chave existente muda de formato.

### 10.1 Remoção de cinco frases duplicadas

Consequência da seção 2.7. Critério: preservar a ordem `confirmed` sobre a `verify`,
e preservar a frase idiomática.

| remover de | idioma | frase | motivo |
|---|---|---|---|
| `deploy.chemlight` | en | `drop a chemlight` | colide com `drop chemlight` de `player.chemlight`, que é `confirmed` |
| `deploy.chemlight` | pt | `solta a luz` | colide com `solta luz` de `player.chemlight`, que é `confirmed` |
| `player.yell` | pt | `para` | colide com `para` de `hold`; "para" como ordem de parar o time é a leitura natural, e `player.yell` mantém 6 frases |
| `move.to` | en | `go` | colide com `go go go` de `confirm.default`, que é idiomático para "executar"; `move.to` mantém `move there`, `go there`, `move up`, `push there` |
| `door.breach.leader.leader` | en | `leader leader and clear` | colide com `leader and clear` de `door.breach.leader.clear`, que é `confirmed`; sobram 7 frases |

Verificado: após as cinco remoções, as 70 ordens continuam alcançáveis nos dois idiomas.

### 10.2 `command_keys` passa a usar nomes de tecla UE

Descoberto durante a implementação da Task 6. O bloco `keybind_defaults` é o fallback
para valores que viriam do `Input.ini`, e o `Input.ini` escreve `Key=One`, não `Key=1`.
Todos os campos irmãos já eram nomes de tecla (`Z`, `LeftShift`, `Tab`, `F5`, `F`);
`command_keys` era a exceção, com dígitos literais que o `KeyCatalog` não resolve.

```
"command_keys": ["1", ... "9"]      ->  ["One", ... "Nine"]
```

Sem isso, toda ordem cujo dígito do menu caísse no fallback — jogador que remapeou, ou
`Input.ini` ausente — seria rejeitada. `build_commands.py` foi atualizado junto, para o
gerador não divergir do mapa.

### 10.3 Campo `close_menu`

Adição de um campo booleano opcional por ordem. Aditivo: ausente é `false`.

Semente inicial, extraída dos ramos `if queue` do `.vap` e casada por `path` — **19 ids**.
Os 12 comandos do `.vap` citados na seção 2.3 viram 19 ordens porque vários deles são
comandos com cadeia `If/ElseIf` que cobrem várias ordens de uma vez (o comando de
`deploy` sozinho ramifica em flash, sting, gas, chemlight e shield):

```
cover              door.disarm            door.stack.left     move.fallin
deploy.chemlight   door.open.flashbang    door.stack.right    move.to
deploy.flashbang   door.open.gas          door.stack.split    person.restrain
deploy.gas         door.open.stinger      door.toggle         search
deploy.shield      deploy.stinger         door.wedge
```

**Esta lista é hipótese, não verdade.** O casamento foi por `path`, e a seção 2.5 mostra
que `path` é ambíguo: quando `MENU 2 2` fechava o menu no perfil original, não há como
saber se era `move.fallin` ou `door.open.flashbang`. Os dois foram marcados. Quatro
ramos do `.vap` não casaram com nenhuma ordem e ficaram de fora.

Corrigir a lista em jogo é o **primeiro teste da etapa 4**, como a §5.4 do brief exige.

---

## 11. Pendências para validação em jogo

Não bloqueiam a implementação; bloqueiam o "pronto" da etapa 4.

1. `close_menu` das 19 ordens semeadas e das demais 51.
2. As 25 ordens marcadas `confidence: "verify"` no mapa.
3. Se o clique de fechamento também é necessário em modo não-enfileirado para caminhos
   curtos.
4. Se o Ready or Not roda anti-cheat na versão atual (§10 do brief).
5. Se `door.breach.leader.clear` é mesmo o destino de "breach and clear" (seção 2.6) —
   basta abrir o menu e conferir o ramo `MENU 3 5 1`.

---

## 12. Critérios de pronto

| Etapa | Pronto quando |
|---|---|
| 1 | `CommandMap` carrega as 70 ordens e todo token de `path` resolve para `InputToken` lendo o `Input.ini` real, com fallback nos defaults |
| 2 | Corpus gerado: 399 en e 371 pt com **zero erradas** e cobertura **70/70** em cada idioma; corpus adversarial: 15/15 |
| 3 | `ronvoice test "red team, open the door with flashbang"` imprime intent e `KeySequence`, sem jogo e sem microfone |
| 4 | `ronvoice send "stack up"` movimenta o menu no jogo, e a §5.4 foi validada primeiro |

Os números da etapa 2 não são aspiracionais: foram obtidos rodando o algoritmo desta
spec contra o mapa real antes de escrever qualquer C#. O protótipo de validação está em
`docs/superpowers/specs/prototype/phrase_matcher.py`, versionado junto com esta spec como
referência executável — a implementação em C# deve reproduzir os mesmos números.
