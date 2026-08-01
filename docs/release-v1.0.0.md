Controle por voz para o esquadrão de Ready or Not. Você fala, eles obedecem.

Funciona na tela e em VR. Gratuito, roda offline — nada do que você fala sai do
seu computador — e não precisa de instalação.

```
"empilha"                       "stack up"
"abre com flash"                "open with flash bang"
"vermelho, c dois e limpa"      "red team, c two and clear"
"prepara, abre a porta"         "prep, open the door"
```

## O que baixar

| arquivo | quando |
|---|---|
| **RonVoice-app.zip** | sempre — é o programa |
| **RonVoiceMod-UE4SS.rar** | se você **não** tem UE4SS |
| **RonVoiceMod.rar** | se você **já** tem UE4SS |

O mod vai em `<Ready or Not>\ReadyOrNot\Binaries\Win64` (extraia tudo ali).
O programa vai onde você quiser; rode como administrador.

## Por que precisa do mod

O caminho normal seria abrir o menu SWAT e digitar. Em VR o menu abre e ignora
os dígitos — quem escolhe passa a ser para onde você está olhando. E uma tecla
por ordem não resolve: o Windows acaba no F24 e o jogo tem 70 ordens.

Então o RonVoice não aperta tecla. Ele deixa a ordem num arquivo e o mod, que
roda dentro do jogo, chama a função do jogo direto. Sem menu, sem teto — e com
resposta: quando não dá, você lê o motivo na tela em vez de falar com a parede.

O **RoNSpeech não é mais necessário**. Se você tem, pode desligar.

## O que funciona

Das 70 ordens: **47** vão pelo mod e **5** são tecla direta.

Arrombar (abrir, escopeta, C2, cada um com flash, stinger ou gás), empilhar,
espiar, espelho, cunha, destrancar, desarmar armadilha, formações, ir até ali,
segurar, cobrir, revistar, lançar granada, luz química, escudo, algemar.

Português e inglês, 427 e 438 frases. Cada ordem aceita várias, e dá para
adicionar as suas.

## O que está bloqueado

As **18** ordens de chute, aríete e líder-arromba. Elas fechavam o jogo.

O valor que passavam para a função de arrombamento veio do enum do próprio
jogo — mas estar no enum não é o mesmo que a função aceitar, e a diferença
derrubou uma missão. Ficam recusando com o motivo até haver um jeito comprovado
de pedir. Recusar é pior que funcionar e muito melhor que fechar o jogo de quem
está jogando.

## Créditos

**UE4SS** ([RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)) é de terceiros,
licença MIT, e vai no pacote sem modificação nenhuma.

O reconhecimento de voz é o [Vosk](https://alphacephei.com/vosk/), local e
offline.

O **RoNSpeech** não vai junto e não é preciso, mas foi lendo o código dele que
descobri como o jogo espera receber cada ordem.
