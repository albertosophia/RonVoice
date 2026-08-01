Correções de uso. O mod não mudou — se você já instalou, só troque o programa.

## O programa agora se chama RonVoice.exe, e tem ícone

Era `RonVoice.App.exe`. O `.App` é nome de projeto, não nome de programa. E um
executável sem ícone recebe a folha em branco do Windows, que lê como "algum
arquivo" e não como um programa.

## A aba Configuração diz em qual dos dois estados você está

O botão lê **Salvo** quando não há nada pendente e **Salvar alterações** quando
há. Antes ele estava sempre clicável, o que não distingue "já salvei" de
"esqueci de salvar".

Salvar não abre mais caixa de mensagem: o botão já responde.

## O aviso de reiniciar aparece na hora de escolher

Trocar o idioma só passa a valer quando você reabre o RonVoice — o
reconhecimento monta a lista de palavras uma vez, na abertura.

Isso antes era dito numa caixa **depois** de salvar. Agora aparece no instante
em que você escolhe o idioma, e fica na tela até você reabrir.

Se você trocou o idioma e "parou de funcionar", era isto: o app continuava
ouvindo no idioma anterior. Não tem nada a ver com a ordem de abrir o jogo e o
programa — abra na ordem que quiser.

## O limiar de confiança passou a valer ao salvar

Ele ficava preso na abertura. Mexer no controle mudava o arquivo e mais nada até
reabrir, sem uma linha dizendo — e era invisível nos dois sentidos: subir e nada
ficar mais exigente, ou baixar e o app continuar recusando o que você acabou de
liberar.

Com isso, o idioma é a única configuração que ainda pede reabrir.
