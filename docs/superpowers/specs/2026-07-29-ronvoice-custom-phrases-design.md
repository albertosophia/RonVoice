# RonVoice — frases próprias e verificação guiada (etapa 7)

Data: 2026-07-29
Fonte de requisitos: decisão do autor de lançar o projeto gratuitamente ao público
Specs anteriores: `2026-07-28-ronvoice-core-design.md`, `2026-07-29-ronvoice-speech-design.md`,
`2026-07-29-ronvoice-app-design.md`
Status: aprovado para virar plano de implementação

Documento em português; código, identificadores e commits em inglês.

---

## 1. Por que estas duas coisas

O projeto vai ser público e gratuito. As duas funcionalidades desta spec atacam os dois
motivos mais prováveis de alguém desistir nos primeiros minutos.

**Frases próprias.** O mapa tem 770 frases, todas escolhidas pelo autor. Cada pessoa fala
diferente: um diz "abre com flash", outro "manda a bang", outro "joga a luz e entra". Quem
não fala como o mapa conclui que o programa não funciona. É a diferença entre um projeto
usado e um abandonado na primeira sessão.

**Verificação guiada.** As falhas deste sistema são silenciosas por natureza — sem
elevação, microfone errado, jogo fora de foco, modelo ausente. Sem uma tela que verifique
tudo e diga o que falta, cada relato de "não funciona" vira uma conversa de quatro
perguntas.

---

## 2. Frases próprias

### 2.1 Formato e escopo

Arquivo `minhas_frases.json`, ao lado do executável, junto do `settings.json`:

```json
{
  "door.open.flashbang": ["manda a bang", "joga a luz e entra"],
  "hold": ["fica quieto"]
}
```

**Só acrescenta frases a ordens que já existem.** Não remove frases do mapa, não cria
ordens novas, não define caminho de teclas.

Motivo do limite: uma ordem nova exigiria que o usuário escrevesse a sequência do menu, e
uma sequência errada manda teclas erradas ao jogo sem que ele entenda por quê — a mesma
classe de falha silenciosa que a seção 5 do brief inteira tenta evitar. O caso real que
motiva a funcionalidade ("eu digo outra coisa") é coberto sem esse risco.

### 2.2 Validação — o coração da funcionalidade

Roda ao carregar o arquivo. Quatro casos, respostas diferentes:

| situação | comportamento |
|---|---|
| id de ordem inexistente | ignora a entrada; avisa qual id não foi reconhecido |
| frase colide com frase de **outra** ordem | recusa **apenas aquela frase**; avisa com qual ordem colidiu |
| frase já existe na **mesma** ordem | ignora em silêncio — duplicata inofensiva |
| frase vazia ou só espaços | ignora |

**A checagem de colisão é obrigatória e usa a mesma normalização do matcher**
(`TextNormalizer.Tokenize`). Usar outra comparação faria a checagem mentir.

Ela existe porque este projeto já sofreu exatamente essa falha: `drop chemlight` estava em
`deploy.chemlight` e em `player.chemlight`, e **as duas ordens ficavam mudas** — o matcher
rejeitava por ambiguidade e não havia erro em lugar nenhum. Registrado na seção 2.7 da
spec das etapas 1–4. Sem esta validação, qualquer usuário reproduziria o mesmo defeito no
próprio arquivo.

Uma frase recusada **nunca** derruba o carregamento: o resto do arquivo continua valendo.
Arquivo inteiro malformado cai para "nenhuma frase própria" e avisa, sem impedir o app de
abrir.

### 2.3 O que a tela mostra

Os avisos aparecem na aba Comandos, não num arquivo de log. Quem escreveu o arquivo precisa
ver que uma linha dele foi recusada, e por quê — num log ninguém olha.

As frases próprias ficam **marcadas visualmente** no catálogo, distintas das de fábrica.
Sem isso a pessoa não sabe o que ela mesma acrescentou.

Um botão **Recarregar** relê o arquivo sem fechar o app. Ajustar uma frase e ter de
reabrir tornaria o ciclo penoso justamente para quem está personalizando.

### 2.4 Onde isso vive

`RonVoice.Core`, como função pura sobre o mapa já carregado:

```
CustomPhrases.Apply(CommandMap map, string? filePath)
    -> (CommandMap Merged, IReadOnlyList<PhraseIssue> Issues)
```

Fora do `CommandMap`, porque carregar o mapa e personalizá-lo são responsabilidades
distintas e a personalização precisa ser testável sozinha.

O `PhraseMatcher` e o `GrammarBuilder` recebem o mapa já mesclado e não mudam. As frases
próprias entram na gramática do reconhecedor pelo mesmo caminho das originais — é o que
faz o Vosk conseguir ouvi-las.

---

## 3. Verificação guiada

### 3.1 As cinco checagens

| checagem | quando falha, o que diz |
|---|---|
| elevação | "abra como administrador, senão as teclas não chegam ao jogo" |
| modelo do idioma | oferece baixar ali mesmo |
| microfone capta som | pede para a pessoa falar e mostra o medidor de nível |
| jogo encontrado | "escolha o executável na aba Configuração" |
| `Input.ini` encontrado | "usando as teclas padrão; se você remapeou algo, pode não funcionar" |

Cada uma tem três estados: **ok**, **aviso** (funciona, mas com ressalva) e **falha**
(não vai funcionar). `Input.ini` ausente é aviso, não falha — o app funciona com os
`keybind_defaults`.

### 3.2 A do microfone é diferente

É a única que exige ação da pessoa, e é a que mais importa. Ela pede que fale, mostra o
nível ao vivo e conclui pelo pico: se a barra não se mexeu, o microfone está mudo ou é o
dispositivo errado.

Reaproveita `AudioLevel.Rms` e o mesmo piso de silêncio do `VoiceTestRunner`, para os dois
darem o mesmo veredito sobre o mesmo áudio.

Responde antes do fato a pergunta que todo mundo faz depois: "ele está me ouvindo?".

### 3.3 Quando roda

Automaticamente na primeira execução, depois do download do modelo.

E fica um botão **Verificar tudo** na aba Configuração, porque as condições mudam depois:
troca de microfone, atualização do jogo, pasta movida. Uma verificação que só roda uma vez
envelhece.

Termina com uma frase única: *"está pronto — fale `stack up` com o jogo aberto"*, ou a
lista do que falta com o que fazer em cada item.

### 3.4 Onde isso vive

A lógica em `RonVoice.Core`, como uma lista de checagens que devolvem resultado — sem UI,
testável com dublês:

```
record CheckResult(string Name, CheckStatus Status, string Message)
enum CheckStatus { Ok, Warning, Failed }
```

A parte do microfone recebe o pico medido, em vez de gravar sozinha: quem grava é a UI, e
assim a lógica continua testável sem hardware.

---

## 4. Tratamento de erro

| situação | comportamento |
|---|---|
| `minhas_frases.json` ausente | normal; nenhuma frase própria |
| arquivo malformado | nenhuma frase própria, aviso na tela, app abre |
| id de ordem inexistente | entrada ignorada, aviso nomeando o id |
| frase colidindo | frase recusada, aviso nomeando as duas ordens |
| pasta não gravável | vale o mesmo fallback do `settings.json`, já implementado |
| verificação com falha | app abre assim mesmo; a barra de estado e a verificação mostram o que falta |

Nada aqui impede o app de abrir. Quem tem o arquivo errado precisa da tela para descobrir
qual é o erro.

---

## 5. Testes

Tudo o que importa é lógica pura, e portanto testável:

- **`CustomPhrases.Apply`** — acrescenta à ordem certa; ignora id inexistente com aviso;
  recusa frase que colide com outra ordem, nomeando ambas; aceita duplicata na mesma ordem
  sem ruído; arquivo malformado devolve mapa original mais um aviso; arquivo ausente é
  silêncio.
- **Colisão pela normalização do matcher** — `"Abre a Porta!"` colide com `"abre a porta"`.
  É o teste que garante que a checagem não mente.
- **Depois de mesclar, as invariantes do mapa continuam valendo** — nenhuma frase resolve
  para ordem errada, e as 70 ordens seguem alcançáveis. Reaproveita as asserções agregadas
  do corpus, agora com frases próprias no meio.
- **As checagens** — cada uma nos três estados, com predicados injetados.

A tela em si continua sem teste automatizado, com verificação manual registrada, como nas
etapas anteriores.

---

## 6. Critérios de pronto

| item | pronto quando |
|---|---|
| frases próprias | uma frase acrescentada por arquivo é reconhecida ao falar, e aparece marcada no catálogo |
| colisão | acrescentar frase que já existe em outra ordem é recusada com aviso nomeando as duas, e **nenhuma das duas ordens fica muda** |
| recarregar | editar o arquivo e clicar em Recarregar aplica sem fechar o app |
| verificação | as cinco checagens rodam, a do microfone reage à voz, e o resultado diz o que fazer |
| reexecução | o botão Verificar tudo pode ser usado quantas vezes quiser |

---

## 7. Fora de escopo

- **Remover ou desligar frases de fábrica.** Considerado e adiado: resolve um problema real
  (uma frase que atrapalha), mas ainda não observado em uso.
- **Ordens novas com caminho de teclas próprio.** Recusado pelo risco descrito em 2.1.
- **Compartilhar arquivos de frases dentro do app.** O arquivo é texto simples; as pessoas
  trocam sozinhas.
