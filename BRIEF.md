# RonVoice — brief de implementação

Controle por voz dos NPCs de **Ready or Not**. Alternativa própria ao VoiceAttack.

Este documento é a fonte de verdade do projeto. As decisões da seção
"Decisões fechadas" já foram analisadas e **não devem ser reabertas** — cada uma
tem um motivo registrado. Se algo aqui parecer errado durante a implementação,
levante a questão antes de mudar, não mude silenciosamente.

Documento em português; código, identificadores e commits em inglês.

---

## 1. O que o software faz

O jogador fala uma ordem em linguagem natural. O software reconhece, traduz para
a sequência de teclas equivalente do menu de comandos do jogo, e envia como input
de teclado/mouse.

```
"red team, open the door with flashbang"   ->   F7  MMB  2  2
"stack up left"                            ->   MMB  1  2
"hold"                                     ->   MMB  4
```

Idiomas: **inglês (principal)** e **português (opcional)**. O inglês é o que o
autor usa no dia a dia; o português precisa existir e continuar funcionando, mas
não recebe polimento equivalente.

---

## 2. Decisões fechadas

| Decisão | Motivo |
|---|---|
| **C#/.NET, Windows-only** | O trabalho pesado é Win32: `SendInput`, `RegisterHotKey`, `GetForegroundWindow`. O jogo é Windows. |
| **Vosk como motor de fala** | Gramática fechada em runtime, modelo pt-BR disponível, roda em CPU, modelo empacotado com o app. |
| **Gramática fechada, não transcrição livre** | O reconhecedor só precisa escolher entre N frases conhecidas. Ganha em latência e acerto sobre transcrição livre, mesmo com modelo mais fraco. |
| **Whisper NÃO é o motor principal** | Já foi avaliado e descartado. Pode existir como implementação alternativa atrás de `ISpeechEngine`, nunca como padrão. |
| **WPF para a UI** | Leve, maduro, sem dor de deploy, e resolve janela topmost quando houver overlay. |
| **Push-to-talk** | Sem PTT, conversa no Discord vira ordem. Também torna a segmentação de frase determinística. |
| **Modelo Vosk `small`** | Com gramática fechada o modelo grande não agrega. Instalador leve. |

---

## 3. Estrutura do repositório

```
RonVoice.Core/          biblioteca, sem nenhuma referência de UI
  Speech/
    ISpeechEngine.cs        interface: Start, Stop, evento OnRecognized
    VoskSpeechEngine.cs     implementação padrão
    GrammarBuilder.cs       ron_commands.json -> lista de frases da gramática
  Matching/
    PhraseMatcher.cs        string reconhecida -> Intent
    Intent.cs               record: Element?, OrderId, Queue
  Commands/
    CommandMap.cs           carrega e indexa ron_commands.json
    CommandResolver.cs      Intent -> KeySequence
    KeybindReader.cs        lê Input.ini do jogo
  Input/
    IInputSender.cs
    SendInputSender.cs      SendInput com scan codes
    KeySequence.cs
  Pipeline/
    VoicePipeline.cs        orquestra tudo, publica eventos de estágio

RonVoice.Cli/           console para testar sem UI e sem jogo
RonVoice.App/           WPF
RonVoice.Tests/         xUnit, foco em PhraseMatcher e CommandResolver

data/
  ron_commands.json     mapa de comandos (fornecido)
  models/               modelos Vosk (não versionar; baixar no build)
```

`RonVoice.Core` não pode referenciar WPF nem `System.Windows`. Se precisar,
o design está errado.

---

## 4. Contrato de dados: `ron_commands.json`

Já existe e está pronto. **Não regenerar nem reestruturar.** 70 ordens.

```jsonc
{
  "keybind_defaults": { "swat_command_menu": "MiddleMouse", "hold_command": "LeftShift", ... },
  "timing": { "key_hold_ms": 35, "gap_between_keys_ms": 35, "menu_open_settle_ms": 60 },
  "elements": { "red": { "key": "F7", "en": [...], "pt": [...] } },
  "modifiers": { "queue": { "en": [...], "pt": [...] } },
  "orders": [
    {
      "id": "door.breach.kick.flashbang",
      "context": "door",
      "path": ["MENU", "3", "1", "2"],
      "confidence": "confirmed",
      "phrases": { "en": ["kick with flashbang", ...], "pt": ["chuta com flash", ...] }
    }
  ]
}
```

Tokens de `path`:

- `MENU` — clique do meio do mouse (abre o menu SWAT)
- dígito `1`–`9` — tecla numérica
- `KEY:NOME` — tecla direta, fora do menu (ex.: `KEY:INTERACT`)

`context` (`door` / `person` / `default` / `any`) é informativo: diz para onde o
jogador precisa estar olhando. **O software não valida isso** — veja seção 5.

`confidence: "verify"` marca as 25 ordens ainda não confirmadas em jogo. A UI deve
sinalizá-las visualmente.

---

## 5. Conhecimento não-óbvio

Esta seção é o motivo de o documento existir. Tudo aqui foi obtido por engenharia
reversa de um perfil VoiceAttack funcional. **Ignorar qualquer item resulta em
software que não funciona, geralmente sem erro visível.**

### 5.1 Scan codes, não virtual keys

O jogo é Unreal e lê input via DirectInput/RawInput. Ele **ignora** `SendKeys` e
mensagens `WM_KEYDOWN`. Obrigatório: `SendInput` com a flag `KEYEVENTF_SCANCODE`,
`wVk = 0` e o scan code em `wScan`.

Sintoma se errar: nenhum erro, nada acontece no jogo.

### 5.2 Toda tecla precisa ser segurada ~35 ms

Press-and-release no mesmo tick é perdido pelo jogo. Segure `key_hold_ms` entre o
keydown e o keyup, e espere `gap_between_keys_ms` antes da próxima. 35 ms ≈ 2
frames a 60fps — é o valor que o perfil VoiceAttack usa (`0.033s`).

Sintoma se errar: funciona 70% das vezes, o que é pior que nunca funcionar.

### 5.3 O modificador de fila envolve apenas a ÚLTIMA tecla

Quando o jogador diz "prepara" / "on my mark", a ordem deve ficar engatilhada em
vez de executar. Isso se faz segurando `hold_command` (LShift) **em volta da
última tecla do caminho**, não durante o caminho inteiro.

```
normal:     MMB  3  1  2
enfileirado: MMB  3  1  [LShift↓ 2 LShift↑]
```

Segurar LShift durante a navegação cancela o menu.

### 5.4 Algumas ordens precisam de um `MENU` de fechamento

Nem todo caminho fecha o menu sozinho. Várias ordens do perfil original terminam
com um segundo clique do meio. Onde o mapa traz `MENU` no fim do `path`, envie.

Sintoma se errar: o menu fica aberto e a ordem seguinte entra no lugar errado.
**Este deve ser o primeiro comportamento testado em jogo.**

### 5.5 O estado de seleção vive no jogo, não no app

`F7` significa "vermelho, atenção" — a seleção permanece ativa no jogo até ser
trocada. Portanto:

- frase mencionou elemento -> envie a tecla dele antes do caminho
- frase não mencionou -> **não envie nada**, herda a seleção corrente

Consequência boa: o app é imune a erro de segmentação. "red team" e "open the
door with flashbang" ditos separadamente produzem o mesmo resultado que a frase
inteira de uma vez. Não guarde estado de seleção para tomar decisão.

**Não implemente elemento padrão** (tipo "sem elemento = gold"). Isso quebra o
fluxo de falar o time primeiro e a ordem depois.

Em vez disso, a UI mostra um indicador grande do elemento ativo. Como o jogador
pode apertar `F5`/`F6`/`F7` direto no teclado, observe essas teclas no mesmo hook
global do push-to-talk e atualize o indicador, senão ele dessincroniza.

### 5.6 O token `[unk]` é obrigatório na gramática

Sem ele, o Vosk **força** qualquer áudio para dentro da gramática — ruído vira
comando, porque foi a opção menos improvável. Com `[unk]` na lista, fala fora do
vocabulário cai em desconhecido e é descartada.

Sintoma se errar: o app manda ordens sozinho.

### 5.7 Nunca fixe teclas no código

Leia os binds reais de:

```
%LOCALAPPDATA%\ReadyOrNot\Saved\Config\WindowsNoEditor\Input.ini
```

Use `keybind_defaults` do JSON apenas como fallback quando o arquivo não existir
ou não trouxer o bind. O perfil original já usava `Space` onde o padrão do jogo é
`Z` — binds fixos quebram para qualquer um que tenha remapeado algo.

### 5.8 O app precisa rodar como administrador

Se o jogo estiver elevado e o app não, o input não chega e **não há erro**.
Detecte e avise na UI.

---

## 6. Pipeline

```
Microfone (PTT, 16 kHz mono)
  -> VoskEngine        gramática fechada; descarta [unk] e baixa confiança
  -> PhraseMatcher     extrai (element?, orderId, queue) ou nada
  -> CommandResolver   ordem + binds -> KeySequence
  -> InputSender       scan codes, timing; descarta se o jogo não está em foco
  -> jogo
```

Três portões de rejeição: `[unk]`/confiança, casamento incompleto, jogo fora de
foco. Ordem que passa dos três executa sem confirmação — latência é o requisito.

`ron_commands.json` alimenta **dois** pontos: a gramática (o que o app consegue
ouvir) e a tabela de caminhos (o que consegue executar). Fonte de verdade única —
não deve existir estado em que o app entenda uma frase que não sabe executar.

**A UI não fica no fluxo.** Cada estágio publica um evento
(`Recognized`, `Matched`, `Rejected`, `Sent`) e a tela apenas assina. O pipeline
não pode ter latência dependente da UI.

Threading: captura de áudio em thread própria; reconhecimento consumindo fila;
**envio de teclas em fila com um único consumidor**, sem bloquear o
reconhecimento. Isso dá o comportamento correto quando o jogador fala duas ordens
seguidas — elas enfileiram em vez de embaralhar teclas.

---

## 7. Ordem de construção

Cada etapa tem um critério de pronto verificável. Não avance sem ele.

**1. `CommandMap` + `KeybindReader`**
Pronto quando: carrega as 70 ordens e resolve `MENU`/dígitos/`KEY:` para teclas
reais lendo o `Input.ini`, com fallback nos defaults.

**2. `PhraseMatcher`**
Pronto quando: os testes da seção 8 passam nos dois idiomas.

**3. `RonVoice.Cli`**
Pronto quando: `ronvoice test "red team, open the door with flashbang"` imprime o
intent casado e a sequência de teclas resolvida, sem jogo e sem microfone.
Esta é a ferramenta de depuração mais importante do projeto — construa cedo.

**4. `SendInputSender`**
Pronto quando: `ronvoice send "stack up"` movimenta o menu no jogo de verdade.
Valide primeiro o item 5.4 (menu de fechamento).

**5. `VoskSpeechEngine` + `GrammarBuilder`**
Pronto quando: falar ao microfone produz o mesmo intent que o `test` de texto,
e ruído/fala aleatória cai em `[unk]` sem disparar nada.

**6. `RonVoice.App` (WPF)**
Quatro blocos: catálogo das ordens em árvore por contexto, com busca e as frases
dos dois idiomas; painel ao vivo (transcrito, casado, score, teclas enviadas);
config (idioma, tecla de PTT, caminho do Input.ini, limiar); estado (jogo em
foco, microfone, modelo, elemento ativo).

---

## 8. Testes

`PhraseMatcher` é o único componente com regra de negócio real. Os outros são
adaptadores finos. Ele deve ser o mais testado e o mais burro possível:
entra string, sai `Intent` ou nada.

Corpus por idioma em `RonVoice.Tests/corpus/{en,pt}.tsv`, formato
`frase<TAB>orderId esperado<TAB>element esperado<TAB>queue esperado`.

Casos que precisam estar cobertos:

```
red team, open the door with flashbang   -> door.open.flashbang   red    false
open the door with flashbang             -> door.open.flashbang   null   false
red team                                 -> null                  red    false
stack up left                            -> door.stack.left       null   false
blue team prep breach and clear          -> door.open.clear       blue   true
banana pudim relógio                     -> null                  null   false
```

O corpus PT é o que mantém o modo português vivo, já que o autor não vai usá-lo
no dia a dia. Rode no CI.

---

## 9. O que NÃO fazer

- Não usar `SendKeys`, `keybd_event` ou mensagens de janela — só `SendInput` com scan code.
- Não fixar teclas no código.
- Não implementar elemento padrão quando a frase não menciona um.
- Não tentar detectar o contexto (porta/pessoa) na v1. O jogador é responsável por
  estar olhando para o alvo certo, igual ao VoiceAttack. O campo `context` serve
  só para informar na UI.
- Não usar transcrição livre no caminho principal.
- Não pedir confirmação antes de executar uma ordem.
- Não traduzir o jargão do jogo no modo português. Ninguém fala "granada de luz";
  fala "flashbang". As frases PT do mapa já são híbridas de propósito — mantenha.
- Não colocar lógica de negócio no `RonVoice.App`.

---

## 10. A verificar durante a implementação

- API exata do binding C# do Vosk (`VoskRecognizer`, gramática no construtor,
  `SetGrammar`, `SetWords` para obter confiança por palavra). Confira a versão
  atual do pacote em vez de assumir as assinaturas.
- Nome exato das seções e chaves do `Input.ini` do Ready or Not.
- As 25 ordens marcadas `confidence: "verify"` no mapa — validar em jogo e
  corrigir o JSON.
- Se o Ready or Not roda algum anti-cheat na versão atual.

---

## 11. Material fornecido

- `data/ron_commands.json` — mapa de comandos, pronto
- `build_commands.py` — gerador do mapa, caso precise regenerar após validação
- `vapdec.py` — decompilador de perfis `.vap` do VoiceAttack. Não faz parte do
  produto; serve para minerar outros perfis públicos e extrair sequências de
  teclas já validadas pela comunidade.
