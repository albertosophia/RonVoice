# RonVoice

**Português** · [English](README.en.md)

Controle por voz para o esquadrão de **Ready or Not**. Você fala, eles obedecem.

Funciona na tela e em VR. É gratuito, roda offline — nada do que você fala sai
do seu computador — e não precisa de instalação.

```
"empilha"                          "stack up"
"abre com flash"                   "open with flash bang"
"vermelho, c dois e limpa"         "red team, c two and clear"
"prepara, abre a porta"            "prep, open the door"
```

---

## O que você precisa

| | |
|---|---|
| Ready or Not | qualquer versão recente |
| **UE4SS + RonVoiceMod** | obrigatório — veja abaixo |
| Windows | 10 ou 11 |

Não precisa de conta, de chave de API nem de internet. O reconhecimento de voz
roda na sua máquina.

## Instalação

**1. O mod, dentro do jogo.**

Baixe `RonVoiceMod-UE4SS.rar` nos [Releases](../../releases) e extraia tudo em:

```
<Ready or Not>\ReadyOrNot\Binaries\Win64
```

É a pasta onde fica o `ReadyOrNotSteam-Win64-Shipping.exe`. Se você já usa
UE4SS, baixe o `RonVoiceMod.rar`, que é só o mod, e jogue a pasta em `Mods\`.

**2. O programa.**

Baixe `RonVoice-app.zip`, descompacte onde quiser e rode `RonVoice.exe`.
O Windows vai pedir permissão de administrador: responda **Sim**. Sem isso o
jogo simplesmente não recebe nada, e não aparece erro nenhum — o Windows
descarta a entrada em silêncio porque o jogo roda com integridade mais alta.

**3. Pronto.** Abra o jogo, entre numa missão, mire numa porta e fale.

## Por que o mod é obrigatório

Porque sem ele não dá para chegar em todas as ordens, e em VR não dá para
chegar em nenhuma.

O caminho normal seria abrir o menu SWAT e digitar. Em VR o menu abre e ignora
os dígitos — quem escolhe passa a ser para onde você está olhando. Não é
lentidão: testei esperas de 60, 300 e 800 ms e não muda nada.

A alternativa óbvia seria uma tecla por ordem, mas o Windows acaba no F24 e o
jogo tem 70 ordens.

Então o RonVoice não aperta tecla nenhuma: ele deixa a ordem num arquivo e o
mod, que roda **dentro** do jogo, chama a função do jogo direto. Sem menu, sem
tecla, sem teto.

E o mod **responde**. Mandar tecla é torcer — o Windows aceita e ninguém conta
se o jogo agiu. Por aqui, quando não funciona, você lê o motivo na tela em vez
de ficar falando com a parede.

## O que dá para falar

A aba **Comandos** lista tudo, com busca. O resumo:

- **arrombar** — abrir e limpar, escopeta, C2, cada um com flash, stinger ou gás
- **porta** — empilhar, espiar, espelho, cunha, destrancar, desarmar armadilha
- **mover** — vem comigo, formações, ir até ali, segurar, cobrir, revistar
- **lançar** — flash, stinger, gás, luz química, escudo
- **pessoas** — algemar, mãos ao alto

**Elemento.** Comece pelo time e a ordem vai só para ele:
`"vermelho, abre e limpa"`, `"azul, empilha"`. Sem dizer nada, vale o time ativo.

**Fila.** Comece com `prepara` (ou `prep`, `no meu comando`) e a ordem fica
engatilhada até você mandar executar.

**Suas próprias frases.** Se você fala de um jeito que não está na lista,
adicione na aba Comandos. Fica salvo num arquivo seu, sobrevive à atualização,
e dá para exportar e mandar para um amigo.

## Idiomas

Português e inglês, escolhidos na aba Configuração. São 427 frases em português
e 438 em inglês — cada ordem aceita várias, porque ninguém fala igual.

## O estado hoje

De 70 ordens:

| | |
|---|---|
| **47** funcionam pelo mod | dessas, 13 ainda não foram confirmadas uma a uma em jogo |
| **5** são tecla direta | `executa`, `mãos ao alto`, e as três ações do próprio jogador |
| **18** estão bloqueadas | chute, aríete e líder-arromba |

As 18 bloqueadas **fechavam o jogo**. O valor que elas passam para a função de
arrombamento veio do enum do próprio jogo, mas estar no enum não é o mesmo que
a função aceitar — e a diferença derrubou uma missão. Elas recusam com o motivo
até haver um jeito comprovado de pedir. Recusar é pior que funcionar e muito
melhor que fechar o jogo de quem está jogando.

## Quando não funciona

**"o mod não respondeu"** — não veio recibo. Ou o jogo está fechado, ou o mod
não carregou. Abra o console do UE4SS e procure:

```
[RonVoice] pronto. caixa de correio em C:\...\AppData\Local\RonVoice
```

**Ele para de escutar quando saio do jogo** — é de propósito. O microfone fica
sempre aberto, e o foco no jogo é o que impede conversa no Discord de virar
ordem. `Ctrl+Alt+M` muta sem tirar a mão do jogo.

**Não quero o microfone aberto** — ligue push-to-talk na aba Configuração.
Clique no campo da tecla e **aperte** a tecla que quiser (os botões laterais do
mouse são os mais cômodos). Segure, fale, solte: a ordem sai ao soltar.

**Falo uma palavra e não acontece nada** — o reconhecedor tem vocabulário
fechado, e o modelo pequeno não conhece palavras inventadas. `flashbang`, `c2` e
`chemlight` não existem para ele; por isso as frases usam **flash bang**,
**c dois** e **luz química**. A aba Teste de voz mostra em verde o que ele
entendeu e em vermelho o que não.

## Testar sem estragar a missão

A aba **Teste de voz** reconhece e mostra o resultado subindo numa lista, mas
não manda nada para o jogo. Dá para usar no meio de uma partida.

## Créditos

O **UE4SS** ([RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)) é de terceiros,
licença MIT, e vai no pacote sem modificação nenhuma.

O reconhecimento de voz é o [Vosk](https://alphacephei.com/vosk/), que roda
local e offline.

O **RoNSpeech** não é necessário e não vai junto, mas foi lendo o código dele
que descobri como o jogo espera receber cada ordem.
