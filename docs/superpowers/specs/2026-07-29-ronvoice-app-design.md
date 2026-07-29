# RonVoice — design do aplicativo (etapa 6)

Data: 2026-07-29
Fonte de requisitos: `BRIEF.md`, etapa 6 da seção 7, mais decisões do autor registradas na seção 2
Specs anteriores: `2026-07-28-ronvoice-core-design.md` (etapas 1–4, validada em jogo),
`2026-07-29-ronvoice-speech-design.md` (etapa 5)
Status: aprovado para virar plano de implementação

Documento em português; código, identificadores e commits em inglês.

---

## 1. Escopo e propósito

Esta spec cobre a **etapa 6** do brief: a janela do aplicativo. Mas o propósito mudou em
relação ao que o brief previa, e a mudança governa todo o resto.

O brief tratava a UI como painel de controle do autor. **O autor decidiu tornar o projeto
público.** Isso desloca o usuário-alvo de "quem escreveu o mapa de comandos" para "quem
baixou um zip e nunca viu um terminal", e é o critério que decide cada escolha aqui.

Consequência direta: o catálogo de comandos deixa de ser um dos quatro blocos e passa a
ser a tela inicial. Para quem instala, o primeiro problema não é depurar reconhecimento —
é **não saber o que pode falar**. São 70 ordens e 770 frases.

Fora de escopo: instalador. O formato de distribuição é **portable**, por decisão do autor.
Um instalador pode vir depois, e nada aqui o impede.

---

## 2. Decisões do autor registradas

| decisão | consequência |
|---|---|
| **Distribuição portable**, sem instalador | tudo ao lado do executável; nada no registro |
| **Push-to-talk é opcional; o padrão de fábrica é sempre-ligado** | mantém o comportamento da etapa 5 como padrão e devolve o PTT a quem quiser |
| **Seletor do executável do jogo** | o nome do processo varia por loja; a versão Steam chama-se `ReadyOrNotSteam-Win64-Shipping` |
| **Aba de teste** | o usuário fala e vê se está sendo reconhecido |
| **Configuração ao lado do executável** | copiar a pasta leva tudo |

Acrescentados no design, por serem da mesma classe de falha silenciosa:

- **Seletor de microfone.** A máquina do autor lista **12 dispositivos de entrada**
  (Voicemeeter, cabos virtuais VB-Audio, mic do Steam Streaming). O padrão é o índice 0,
  que pode ser um cabo mudo. O sintoma é idêntico ao de todo o resto dar errado: nada
  acontece, nenhum erro.
- **Elevação por manifesto.** Deixa de ser instrução em arquivo-texto.
- **Barra de estado permanente**, porque as três falhas do sistema são invisíveis.

---

## 3. Portable: onde as coisas ficam

```
RonVoice\
  RonVoice.App.exe          a janela
  RonVoice.Cli.exe          continua, para depurar
  settings.json             ao lado do exe
  data\ron_commands.json
  data\models\              baixado na primeira execução
```

**Regra de escrita:** tenta gravar `settings.json` ao lado do executável. Se não conseguir
— o caso real é a pasta estar em `Program Files`, onde o Windows bloqueia escrita — cai
para `%APPDATA%\RonVoice` e **avisa na tela** que saiu do modo portable. Nunca falha em
silêncio.

---

## 4. Elevação

O executável declara `requireAdministrator` no manifesto. O Windows pede o UAC ao abrir.

Motivo: o jogo roda com integridade mais alta que um processo comum, e o Windows descarta
input de integridade menor **sem gerar erro**. Foi confirmado em jogo em 2026-07-28 e está
registrado na seção 11 da spec das etapas 1–4. Sem o manifesto, cada usuário público
repetiria a mesma depuração: abre, fala, nada acontece, nenhuma mensagem.

**Custo aceito:** o app pede UAC toda vez que abre, mesmo para só consultar o catálogo.

**Alternativa considerada e descartada:** separar o envio de teclas num processo elevado
próprio, mantendo a janela sem privilégio. Dobra a complexidade — dois processos, canal
entre eles, ciclo de vida — para economizar um clique.

A barra de estado mostra a elevação de qualquer forma, para o caso de alguém abrir o app
por um caminho que contorne o manifesto.

---

## 5. As telas

### 5.1 Barra de estado — permanente, em todas as abas

```
● elevado   ● microfone: Microfone (WIND)   ● modelo: en   ○ jogo: fora de foco   [ mutar ]
```

Existe porque as falhas do sistema são invisíveis: sem elevação, microfone errado, jogo
fora de foco. Quando alguém disser "não funciona", esta linha responde antes de qualquer
suporte.

Mostra também o **elemento ativo** (gold/blue/red). Como o jogador pode apertar
`F5`/`F6`/`F7` direto no teclado, o app observa essas teclas no mesmo hook global do mute,
senão o indicador dessincroniza — é a exigência da §5.5 do brief.

### 5.2 Aba Comandos — a tela inicial

Busca no topo. Árvore agrupada por `context` (porta, pessoa, geral, qualquer).

Cada ordem mostra:

- as frases nos dois idiomas
- o contexto — para onde o jogador precisa estar olhando
- um selo nas **25 ordens marcadas `confidence: "verify"`**, que podem não funcionar em
  jogo. Sem o aviso, viram "esse comando está quebrado"
- um botão **Enviar ao jogo**, que executa a ordem sem passar por reconhecimento

O nome é deliberadamente diferente do "Testar minha voz" da aba Teste, porque os dois
respondem perguntas opostas. Aqui a fala sai da equação: serve para descobrir se **a ordem
em si** funciona no jogo — útil sobretudo nas 25 marcadas `verify`. Lá, o jogo sai da
equação: serve para descobrir se **a voz** está sendo entendida.

**Ele não pode simplesmente enviar.** Ao clicar, quem está em foco é a janela do app, e o
`ForegroundGuard` — corretamente — recusaria. Forçar seria pior: as teclas iriam para a
própria janela.

Comportamento: ao clicar, o app **se minimiza**, devolve o foco ao jogo e conta três
segundos visíveis antes de enviar, com opção de cancelar. É o mesmo problema que o
`--delay` do CLI resolve, com a diferença de que aqui o app sabe qual janela ativar em vez
de depender de o usuário fazer alt-tab.

Se o jogo não estiver rodando, o botão fica desabilitado com o motivo em tooltip, em vez de
falhar ao ser clicado.

### 5.3 Aba Teste — "a minha voz está funcionando?"

Não é um monitor passivo. É um gesto deliberado: a pessoa clica em **Testar minha voz**,
fala uma frase de comando, e recebe um veredito.

O propósito é responder duas perguntas que o usuário não sabe separar sozinho: *o microfone
está pegando?* e *a minha pronúncia está sendo entendida?*

**Enquanto grava**, um medidor de nível ao vivo. Se a barra não se mexe enquanto a pessoa
fala, o problema é o microfone e a investigação acabou aí — sem depender de reconhecimento
nenhum. É o que separa as duas causas antes de qualquer outra coisa.

**Dois detalhes que fazem a funcionalidade existir:**

1. **O `ListenGate` é ignorado no modo de teste.** Durante o teste quem está em foco é a
   janela do app, não o jogo, e o portão — corretamente — recusaria todo o áudio. Sem essa
   exceção, o teste nunca ouviria nada.
2. **Nada é enviado ao jogo.** É o único ponto do sistema onde reconhecer com sucesso não
   produz tecla. É teste de voz, não de comando.

**O veredito diz o que fazer, não o que houve.** Os motivos de rejeição são termos internos;
na tela viram causa e ação:

| resultado interno | o que a pessoa lê |
|---|---|
| nenhum áudio acima do silêncio | "Não ouvi nada. Confira o microfone selecionado e o volume de entrada do Windows." |
| `Unknown` (veio `[unk]`) | "Ouvi você, mas não era um comando conhecido. Veja a aba Comandos para as frases aceitas." |
| `LowConfidence` | "Entendi, mas com pouca certeza. Tente falar mais perto do microfone ou num ambiente mais silencioso." |
| `NoMatch` | "Ouvi \"<texto>\", mas não bate com nenhum comando." |
| ambíguo | "Ouvi \"<texto>\", que pode ser dois comandos diferentes. Tente uma frase mais específica." |
| casou | "Funcionou: <ordem>" mais o elemento, a fila e as teclas que sairiam |

Em todos os casos a tela mostra também o texto cru reconhecido e a confiança, para quem
quiser ir mais fundo — mas embaixo, não como resposta principal.

Isto substitui o "painel ao vivo" do brief. A informação é a mesma; o formato é o que a
torna útil para quem não escreveu o programa. Os três portões de rejeição do §6 do brief
são silenciosos **de propósito**, porque latência é o requisito — esta aba é o único lugar
onde eles ficam observáveis.

### 5.4 Aba Configuração

| campo | comportamento |
|---|---|
| executável do jogo | seletor de arquivo; deriva o nome do processo do arquivo escolhido |
| microfone | lista os dispositivos de entrada; padrão é o índice 0 |
| idioma | `en` / `pt`; trocar recria modelo e reconhecedor |
| modo de escuta | **sempre ligado** (padrão) ou **push-to-talk** |
| tecla de PTT | só quando PTT está ativo; avisa se colidir com bind do jogo |
| limiar de confiança | padrão `0`, isto é, desligado |

**Sobre a colisão de teclas:** o app já lê o `Input.ini` do jogo. Ao escolher a tecla de
PTT, compara com os binds e avisa — "essa tecla já é usada para agachar". Não impede;
avisa.

**Sobre o limiar:** continua sem valor padrão fixado, pelo motivo registrado na spec da
etapa 5 — depende de microfone, voz e ambiente, e inventar um número seria fingir medição.

### 5.5 Primeira execução

Antes de qualquer aba, uma tela única baixando os modelos com barra de progresso.
São 73 MB. Sem ela o app não abre.

---

## 6. Arquitetura

```
RonVoice.App/                     WPF; zero lógica de negócio (§9 do brief)
  App.xaml.cs                     ciclo de vida, bandeja, hook global
  app.manifest                    requireAdministrator
  Views/                          XAML e code-behind mínimo
    MainWindow · CommandsView · TestView · SettingsView · FirstRunView
  ViewModels/                     TODA a lógica; é o que fica testável
    MainViewModel                 abas, estado, ciclo do pipeline
    StatusBarViewModel            elevação, microfone, modelo, foco, elemento
    CommandsViewModel             busca e agrupamento do catálogo
    TestViewModel                 assina os eventos do pipeline
    SettingsViewModel             valida e persiste
    FirstRunViewModel             progresso do download

RonVoice.Core/                    acréscimos, sem UI
  Config/
    AppSettings.cs                record das preferências
    SettingsStore.cs              carrega e grava, com fallback de caminho
    GameExecutable.cs             caminho do exe -> nome do processo
  Speech/
    ModelDownloader.cs            baixa, valida, move
```

**A regra que torna isto testável:** toda a lógica nos view models, nada no code-behind.
É a §9 do brief levada a sério, e é o que separa o que dá para testar do que não dá.

Nada das etapas 1–5 muda. A janela assina os eventos que o `VoicePipeline` já publica.

---

## 7. Tratamento de erro

**O caso mais grave é o download do modelo.** Se falhar pela metade e deixar uma pasta
incompleta, a biblioteca nativa do Vosk **aborta o processo** em vez de lançar exceção: o
app fecha sem mensagem, e volta a fechar na abertura seguinte. Um usuário público não tem
como sair disso.

Regra: baixa para pasta temporária, valida a estrutura, **só então move**. Nunca extrai em
cima do destino. Falhando, o estado anterior permanece intacto. Na abertura, valida antes
de entregar ao Vosk — o `ModelLocator` já reconhece os dois formatos de pasta que os
modelos usam.

| situação | comportamento |
|---|---|
| `settings.json` corrompido | volta ao padrão, avisa, não fecha |
| pasta não gravável | cai para `%APPDATA%`, avisa que saiu do modo portable |
| executável do jogo não existe mais | marca na config; o resto continua funcionando |
| microfone some em uso | barra de estado vermelha, tenta reconectar |
| tecla de PTT recusada pelo Windows | avisa e pede outra; nunca silencia |
| troca de idioma | UI em estado ocupado enquanto recria modelo e reconhecedor |
| não elevado | barra de estado avisa que as teclas não chegarão |

---

## 8. Testes

Janela WPF não se testa automaticamente, e esta spec não finge que sim.

**Testável, e é o que importa:**

- `CommandsViewModel` — busca e agrupamento sobre as 70 ordens
- `GameExecutable` — derivação do nome do processo a partir do caminho escolhido, incluindo
  a variante Steam
- `SettingsStore` — ida e volta, fallback de caminho, arquivo corrompido
- detecção de colisão da tecla de PTT contra um `Input.ini` de fixture
- `ModelDownloader` — validação e o move atômico; que uma extração inválida **não**
  substitui a pasta boa
- `TestViewModel` — o mapeamento de cada resultado interno para a mensagem que a pessoa lê,
  incluindo o caso "não ouvi nada", que não vem do pipeline e sim da ausência de áudio
  acima do silêncio

**Não testável, com verificação manual registrada:** XAML, bandeja, o prompt do UAC, e o
hook global de teclado.

---

## 9. Pendências

1. Taxa de falsa ativação com voz humana real, em partida, com conversa acontecendo. É o
   risco residual da decisão de sempre-ligado, registrado na spec da etapa 5, e só o uso
   mede.
2. Reconhecimento em português nunca foi exercitado com áudio — só existe voz sintética
   inglesa na máquina do autor.
3. As 19 ordens com `close_menu` e as 25 marcadas `verify` continuam sem validação em jogo.
   O botão "Enviar ao jogo" da aba Comandos é o que torna essa validação viável para o
   autor e para terceiros, já que dispensa acertar a fala primeiro.
4. Antivírus e SmartScreen. O app envia teclas sintéticas, registra hotkey global e escuta
   o microfone: é, comportamentalmente, indistinguível de um keylogger, e heurística de
   antivírus tende a reclamar. Sem assinatura de código, parte dos usuários verá alerta.
   Não bloqueia esta etapa; bloqueia um lançamento tranquilo.

---

## 10. Critérios de pronto

| item | pronto quando |
|---|---|
| portable | copiar a pasta para outra máquina leva configuração, modelos e preferências |
| elevação | abrir o app pede UAC; a barra de estado reflete o resultado |
| catálogo | busca encontra qualquer uma das 770 frases e agrupa por contexto; as 25 `verify` aparecem sinalizadas |
| botão Enviar ao jogo | minimiza, devolve o foco ao jogo, conta três segundos e envia; desabilitado com motivo quando o jogo não está rodando |
| Testar minha voz | grava com medidor de nível, ignora o portão de foco, não envia tecla nenhuma, e devolve um veredito em português com a causa e o que fazer |
| configuração | trocar microfone, jogo, idioma e modo de escuta sobrevive a fechar e reabrir |
| primeira execução | numa máquina limpa, sem `data/models`, o app baixa e abre |
