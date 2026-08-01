# RonVoice

[Português](README.md) · **English**

Voice control for your squad in **Ready or Not**. You speak, they move.

Works on screen and in VR. Free, runs offline — nothing you say leaves your
machine — and there is nothing to install.

```
"stack up"                      "empilha"
"open with flash bang"          "abre com flash"
"red team, c two and clear"     "vermelho, c dois e limpa"
"prep, open the door"           "prepara, abre a porta"
```

> **The app's interface is in Portuguese.** Voice recognition works in English
> or Portuguese — pick it in the Configuração tab — but the buttons and labels
> are Portuguese only. If that is a problem, say so in an issue; it is a small
> change, and nobody has asked yet.

---

## What you need

| | |
|---|---|
| Ready or Not | any recent build |
| **UE4SS + RonVoiceMod** | required — see below |
| Windows | 10 or 11 |

No account, no API key, no internet. Speech recognition runs on your machine.

## Install

**1. The mod, inside the game.**

Download `RonVoiceMod-UE4SS.rar` from [Releases](../../releases) and extract all
of it into:

```
<Ready or Not>\ReadyOrNot\Binaries\Win64
```

That is the folder holding `ReadyOrNotSteam-Win64-Shipping.exe`. If you already
run UE4SS, take `RonVoiceMod.rar` instead — just the mod — and drop the folder
into `Mods\`.

**2. The program.**

Download `RonVoice-app.zip`, unpack it anywhere and run `RonVoice.exe`. Windows
will ask for administrator: answer **yes**. Without it the game receives nothing
and no error appears — the game runs at a higher integrity level, so Windows
drops the input in silence.

**3. That's it.** Open the game, start a mission, look at a door and speak.

## Why the mod is required

Because without it you cannot reach every order, and in VR you cannot reach any.

The obvious route would be opening the SWAT menu and typing. In VR the menu
opens and ignores the digits — what picks the entry becomes wherever you are
looking. It is not lag: waits of 60, 300 and 800 ms were tested and change
nothing.

One key per order does not solve it either. Windows stops at F24 and the game
has 70 orders.

So RonVoice presses no keys. It leaves the order in a file, and the mod —
running **inside** the game — calls the game's own function directly. No menu,
no key, no ceiling.

And the mod **answers back**. Sending a key is hoping: Windows accepts it and
nobody tells you whether the game acted. Here, when something does not work,
you read why on screen instead of talking to a wall.

## What you can say

The **Comandos** tab lists everything, with search. The short version:

- **breaching** — open and clear, shotgun, C2, each with flashbang, stinger or gas
- **doors** — stack up, peek, mirror, wedge, pick the lock, disarm the trap
- **movement** — fall in, formations, move there, hold, cover, search
- **deploy** — flashbang, stinger, gas, chemlight, shield
- **people** — restrain, hands up

**Element.** Lead with the team and only they get the order: `"red team, open
and clear"`, `"blue, stack up"`. Say nothing and it goes to the active team.

**Queue.** Lead with `prep` (or `on my mark`, `standby`) and the order waits
until you call execute.

**Your own phrases.** If you say it differently, add it in the Comandos tab. It
is saved in a file of yours, survives updates, and can be exported and sent to a
friend.

## Languages

English and Portuguese, chosen in the Configuração tab. 438 phrases in English
and 427 in Portuguese — every order takes several, because nobody says it the
same way twice.

Note that the small speech models do not know invented words: `flashbang`, `c2`
and `chemlight` are not in their vocabulary. That is why the phrases use **flash
bang**, **c two** and **chem light**. The old spellings still match if you type
them, but the recogniser will never hear them.

## Where it stands

Of 70 orders:

| | |
|---|---|
| **47** go through the mod | 13 of those have not been confirmed one by one in game |
| **5** are a direct key | `execute`, `hands up`, and the three player actions |
| **18** are blocked | kick, ram and leader-breach |

The 18 blocked ones **were closing the game**. The value they pass to the
breaching function came from the game's own enum — but being in the enum is not
the same as the function accepting it, and the difference took down a mission.
They now refuse with a reason until there is a proven way to ask. Refusing is
worse than working and much better than closing the game on someone mid-mission.

## When it doesn't work

**"o mod não respondeu"** ("the mod didn't answer") — no receipt came back.
Either the game is closed or the mod did not load. Open the UE4SS console and
look for:

```
[RonVoice] pronto. caixa de correio em C:\...\AppData\Local\RonVoice
```

**It stops listening when I leave the game** — on purpose. The microphone is
always open, and game focus is what keeps a Discord call from becoming an order.
`Ctrl+Alt+M` mutes without taking your hand off the game.

**I don't want an open microphone** — turn on push-to-talk in the Configuração
tab. Click the key field and **press** the key you want (the mouse side buttons
are the most comfortable). Hold, speak, release: the order goes out on release.

**I changed the language and it stopped working** — the recogniser builds its
word list once, at startup. The new language only applies after you reopen
RonVoice, and the screen says so from the moment you pick it.

## Testing without ruining the mission

The **Teste de voz** tab recognises and shows the result climbing a list, but
sends nothing to the game. You can use it mid-match.

## Credits

**UE4SS** ([RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)) is third-party, MIT
licensed, and ships in the bundle unmodified.

Speech recognition is [Vosk](https://alphacephei.com/vosk/), local and offline.

**RoNSpeech** is neither required nor bundled, but reading its code is how I
found out how the game expects each order to arrive.
