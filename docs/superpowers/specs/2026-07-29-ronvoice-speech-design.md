# RonVoice — design da camada de fala (etapa 5)

Data: 2026-07-29
Fonte de requisitos: `BRIEF.md`, etapa 5 da seção 7
Spec anterior: `2026-07-28-ronvoice-core-design.md` (etapas 1–4, implementada e validada em jogo)
Status: aprovado para virar plano de implementação

Documento em português; código, identificadores e commits em inglês.

---

## 1. Escopo

Esta spec cobre a **etapa 5** do brief — `VoskSpeechEngine` + `GrammarBuilder` — mais o
mínimo de interface que a decisão de "microfone sempre ligado" torna obrigatório: um
ícone de bandeja com estado visível e mute.

Fora de escopo, para a spec seguinte: a janela WPF completa da etapa 6 (catálogo em
árvore, painel ao vivo, config, indicador de elemento ativo).

Pronto quando: falar ao microfone produz o mesmo intent que `ronvoice test` produz por
texto, e ruído ou fala aleatória não dispara nada.

---

## 2. Decisão do usuário que reabre uma "decisão fechada" do brief

O brief lista **push-to-talk** entre as decisões fechadas, com o motivo registrado:
"Sem PTT, conversa no Discord vira ordem. Também torna a segmentação de frase
determinística."

O usuário decidiu, com a consequência apresentada, **abandonar o push-to-talk**: o
software escuta a partir do momento em que abre e para quando fecha, com mute manual.

As duas consequências reais, registradas para que ninguém as redescubra depois:

- **Fala casual dentro do vocabulário vira comando.** O `[unk]` filtra o que está fora
  da gramática, não o que está dentro. "hold" e "go go go" são frases de ordem e também
  coisas que se diz a um colega.
- **A segmentação deixa de ser determinística.** Sem a soltura da tecla marcando o fim
  da frase, quem decide onde ela termina é a detecção de silêncio do Vosk.

**Mitigação adotada:** o reconhecimento só processa áudio enquanto o Ready or Not é a
janela em foco. Alt-tab para o Discord, o navegador ou qualquer outra coisa e ele para
sozinho, sem depender de o jogador lembrar de mutar. Reaproveita o `ForegroundGuard`
já implementado e validado em jogo.

**Risco residual aceito:** falar com colegas por voz *enquanto* o jogo está em foco.
Nenhuma mitigação cobre isso sem uma palavra de ativação, que foi considerada e recusada
pelo custo de latência e de uma palavra a mais por ordem.

---

## 3. Verificações da §10 do brief, resolvidas

O brief mandava confirmar a API do binding antes de assumir. Feito por reflexão sobre o
assembly do pacote NuGet `Vosk 0.3.38`:

```
Vosk.Model(string model_path)
Vosk.Vosk.SetLogLevel(int)
Vosk.VoskRecognizer(Model model, float sample_rate)
Vosk.VoskRecognizer(Model model, float sample_rate, string grammar)
Vosk.VoskRecognizer(Model model, float sample_rate, SpkModel spk_model)
  SetWords(bool) · SetPartialWords(bool) · SetMaxAlternatives(int)
  AcceptWaveform(byte[] data, int len) · (short[]) · (float[])
  Result() · PartialResult() · FinalResult() · Reset()
```

Dois achados que corrigem premissas do brief:

- **Não existe `SetGrammar`.** A gramática é passada no construtor e é imutável na vida
  do reconhecedor. Trocar de idioma exige recriar reconhecedor **e** modelo, já que os
  modelos são por idioma.
- **`SetWords(true)` é o que traz confiança por palavra** no JSON de resultado, e é como
  o portão de confiança será alimentado.

Disponibilidade confirmada: pacote `Vosk` no NuGet até `0.3.38`; `NAudio` disponível;
`alphacephei.com` responde a requisições de faixa, então o download do modelo funciona
a partir deste ambiente.

---

## 4. Estrutura

```
RonVoice.Core/                        continua sem referência de UI
  Speech/
    ISpeechEngine.cs        Start/Stop, evento OnRecognized
    RecognitionResult.cs    record: Text, WordConfidences, IsFinal
    GrammarBuilder.cs       CommandMap + idioma -> string JSON da gramática
    VoskSpeechEngine.cs     implementação padrão
    ModelLocator.cs         acha e valida a pasta do modelo
  Audio/
    IAudioCapture.cs        evento OnAudio(ReadOnlyMemory<byte>)
    WasapiCapture.cs        NAudio, 16 kHz mono, thread própria
    WavFileCapture.cs       mesma interface, lendo de arquivo — usado nos testes
  Pipeline/
    ListenGate.cs           decide se processa: jogo em foco? mudo?
    VoicePipeline.cs        orquestra e publica eventos de estágio
    PipelineEvents.cs       Heard, Matched, Rejected, Sent

RonVoice.Tray/            projeto novo; WinForms apenas pelo NotifyIcon
RonVoice.Cli/             ganha `listen`, `record` e `synth`
data/models/              modelos Vosk; não versionados, baixados no build
```

`RonVoice.Core` continua sem referência a WPF, WinForms ou `System.Windows`. O
`RonVoice.Tray` é quem toca WinForms, e só pelo `NotifyIcon`.

**Nada do que foi validado nas etapas 1–4 muda.** `PhraseMatcher`, `CommandResolver`,
`KeyCatalog`, `KeybindReader` e `SendInputSender` ficam intactos. A camada de fala
entrega texto ao matcher que já existe.

---

## 5. Gramática

### 5.1 Forma

Lista plana composicional (opção A das alternativas avaliadas): as frases de ordem do
idioma, mais os aliases de elemento, mais os aliases do modificador de fila, mais o
token `[unk]`, como entradas independentes.

```jsonc
["open with flashbang", "stack left", ...,      // 399 frases de ordem (en)
 "red team", "blue team", "gold team", ...,     // aliases de elemento
 "prep", "queue", "on my mark", ...,            // aliases de fila
 "[unk]"]
```

O reconhecedor compõe entre entradas; o `PhraseMatcher` já implementado extrai elemento,
fila e ordem de qualquer arranjo de palavras. Fonte de verdade única mantida: o
`ron_commands.json` alimenta a gramática e a tabela de caminhos, como o §6 do brief exige.

### 5.2 O token `[unk]` é obrigatório

Sem ele o Vosk **força** qualquer áudio para dentro da gramática: ruído vira comando
porque foi a opção menos improvável. Com o microfone sempre ligado, este item deixa de
ser higiene e passa a ser o que separa o software de um gerador de ordens aleatórias.

Um teste dedicado alimenta fala fora do vocabulário e afirma que nada é enviado.

### 5.3 A hipótese que a implementação precisa medir primeiro

O design assume que o Vosk **compõe livremente entre as entradas** da lista. Isso não foi
verificado — só a API foi. A primeira tarefa da implementação é medir: montar a gramática
plana e a cartesiana, passar o mesmo áudio pelas duas e comparar.

Se o Vosk casar cada entrada como frase inteira em vez de compor, o fallback é o produto
cartesiano (~3.200 combinações de elemento × fila × ordem), com o custo de FST maior e de
a lógica de composição passar a existir em dois lugares.

**Não avance para o pipeline antes de responder isso.** Todo o resto depende da resposta.

---

## 6. Contratos

```csharp
public sealed record RecognitionResult(
    string Text,
    IReadOnlyList<WordConfidence> Words,
    bool IsFinal);

public sealed record WordConfidence(string Word, double Confidence);

public interface ISpeechEngine : IDisposable
{
    event Action<RecognitionResult>? OnRecognized;
    void Start();
    void Stop();
    /// <summary>Descarta o que estiver em curso. Chamado quando o portão fecha.</summary>
    void Reset();
}

public interface IAudioCapture : IDisposable
{
    event Action<ReadOnlyMemory<byte>>? OnAudio;
    void Start();
    void Stop();
}

public sealed class ListenGate
{
    public ListenGate(Func<bool> isGameForeground, Func<bool> isMuted);
    public bool ShouldProcess();
}
```

Áudio: 16 kHz, mono, PCM 16 bits — o que o modelo espera, e o que evita reamostragem.

**Limiar de confiança:** `SetWords(true)` faz o Vosk devolver confiança por palavra. O
portão descarta o resultado quando a média ponderada por palavra fica abaixo de um limiar
configurável. **O valor padrão não é fixado por esta spec**: ao contrário do limiar do
`PhraseMatcher`, que foi medido contra o catálogo inteiro, este depende de microfone, voz
e ambiente. Começa em `0.0` — isto é, desligado, deixando `[unk]` e a margem de
ambiguidade do matcher fazerem o trabalho — e é calibrado com o corpus real da seção 10.3.
Fixar um número agora seria inventá-lo.

---

## 7. Pipeline e threading

```
microfone (16 kHz mono, contínuo)
  -> ListenGate      jogo em foco? não mudo?    senão descarta e reseta
  -> VoskEngine      gramática fechada; descarta [unk] e confiança baixa
  -> PhraseMatcher   já existe, intacto
  -> CommandResolver já existe, intacto
  -> InputSender     já existe, intacto
```

Três filas, um consumidor cada, como o §6 do brief especifica:

1. Captura publica em `Channel<byte[]>` a partir da thread do NAudio.
2. Uma única task de reconhecimento consome, alimenta `AcceptWaveform` e publica
   `RecognitionResult`.
3. Uma única task de envio consome `Channel<KeySequence>` e chama o `IInputSender`.

Falar duas ordens seguidas as enfileira em vez de embaralhar teclas.

**Quando o portão fecha, o reconhecedor é resetado.** Sem isso, uma frase pela metade
dita antes do alt-tab completaria depois e viraria ordem — modo de falha específico da
decisão de sempre-ligado.

**A UI não fica no fluxo.** Cada estágio publica um evento (`Heard`, `Matched`,
`Rejected`, `Sent`) e a bandeja apenas assina. Latência não pode depender da UI.

---

## 8. Bandeja

Um `NotifyIcon` com quatro estados visíveis:

| estado | quando |
|---|---|
| escutando | jogo em foco, não mudo |
| ocioso | jogo fora de foco |
| mudo | mute manual |
| falha | microfone ou modelo indisponível |

Menu de contexto: mutar/desmutar, trocar idioma, abrir a pasta de logs, sair.
Mais um atalho global para mutar sem tirar a mão do jogo.

O atalho de mute usa o mesmo mecanismo de hook global que a etapa 6 vai precisar para
observar `F5`/`F6`/`F7` e manter o indicador de elemento em sincronia (§5.5 do brief).

---

## 9. Tratamento de erro

| situação | comportamento |
|---|---|
| DLL nativa do Vosk não carrega | erro nomeando a dependência; não sobe |
| pasta do modelo ausente ou inválida | erro com caminho esperado e origem do download; não sobe |
| modelo de idioma diferente do configurado | detecta na carga e recusa |
| microfone ausente no startup | erro claro; não sobe |
| microfone desconectado em uso | ícone vira falha, avisa, tenta reconectar |
| resultado contendo `[unk]` | descarta em silêncio |
| confiança média por palavra abaixo do limiar | descarta em silêncio |
| jogo perde o foco no meio de uma frase | descarta e reseta o reconhecedor |
| casamento incompleto | descarta, publica `Rejected` |

---

## 10. Testes

Três camadas, com o que cada uma prova declarado honestamente.

### 10.1 Lógica pura — testa como o resto do projeto

- **`GrammarBuilder`**: contém todas as frases do idioma pedido — **399 em `en`, 371 em
  `pt`** —, mais os aliases de elemento e de fila, mais `[unk]`; sem duplicatas; JSON
  válido; e o conteúdo muda conforme o idioma.
- **`ListenGate`**: com predicados injetados, cobre as quatro combinações de foco e mute.

### 10.2 Pipeline com dublês — sem áudio nenhum

`ISpeechEngine` falso emitindo texto sob demanda e `IInputSender` falso registrando o que
sairia. Testa a cadeia completa texto → intent → teclas, incluindo o reset ao fechar o
portão e o enfileiramento de duas ordens seguidas.

### 10.3 Vosk real, dirigido por WAV

`WavFileCapture` implementa `IAudioCapture` lendo de arquivo, então o pipeline real roda
sem microfone. Exposto como `ronvoice listen --from-wav <arquivo>`.

**Corpus sintetizado.** Um comando `ronvoice synth` gera WAVs a 16 kHz das frases do mapa
usando a síntese de voz do próprio Windows, e o teste afirma que o texto reconhecido volta
igual. Determinístico, roda no CI, pega regressão de gramática.

**O que isso não prova:** acerto com voz humana. Voz sintética é limpa demais.

**Corpus real.** `ronvoice record` grava falas rotuladas num corpus que cresce com o uso.
É o que mede acerto de verdade e o que justifica ajustar o limiar.

**O teste negativo é o mais importante desta etapa:** fala fora do vocabulário entra,
nada é enviado. Sintetizável, e é a §5.6 do brief virando asserção executável.

---

## 11. Modelos

Não versionados (`data/models/` já está no `.gitignore`). Baixados por script no build,
de `alphacephei.com`, verificado alcançável.

- inglês: `vosk-model-small-en-us-0.15`
- português: `vosk-model-small-pt-0.3`

Modelo `small` por decisão fechada do brief: com gramática fechada o modelo grande não
agrega, e o instalador fica leve.

Como a gramática é imutável no reconhecedor e os modelos são por idioma, **um idioma fica
carregado por vez**. Trocar recria modelo e reconhecedor, com uma pausa perceptível de
alguns segundos — aceitável, já que a troca é rara e o inglês é o uso diário.

---

## 12. Pendências

1. **Se o Vosk compõe entre entradas da gramática** — seção 5.3. Bloqueia todo o resto.
2. Latência real da cadeia com o modelo `small` na máquina do autor.
3. Taxa de falsa ativação em uso real, com o jogo em foco e conversa por voz acontecendo.
   É o risco residual aceito na seção 2, e só o uso mede.
4. Se a síntese de voz do Windows produz áudio que o modelo `small` reconhece bem o
   bastante para o corpus sintetizado ser útil. Se não for, essa camada de teste cai e
   sobra a 10.2 mais o corpus real.

---

## 13. Critérios de pronto

| item | pronto quando |
|---|---|
| gramática | `GrammarBuilder` produz JSON válido com todas as frases do idioma (399 en / 371 pt) e `[unk]`, e a hipótese da 5.3 está respondida |
| reconhecimento | `ronvoice listen --from-wav` produz o mesmo intent que `ronvoice test` produz por texto, para o mesmo enunciado |
| rejeição | fala fora do vocabulário não dispara nada |
| sempre-ligado | com o jogo fora de foco, nada é processado; ao voltar o foco, o reconhecedor começa limpo |
| bandeja | os quatro estados aparecem corretamente e o mute funciona pelo atalho global |
