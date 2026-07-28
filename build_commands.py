#!/usr/bin/env python3
"""
build_commands.py — gera ron_commands.json, o mapa de comandos do Ready or Not.

Fontes cruzadas:
  1. perfil VoiceAttack "Ready or Not v5.1" descompilado (sequencias reais que
     funcionam em jogo)
  2. lista de caminhos de menu da comunidade AutoHotkey (game build 67473)
  3. guias de comandos do jogo

confidence:
  "confirmed" -> perfil e lista da comunidade batem
  "verify"    -> so uma fonte, ou as duas divergem: testar em jogo antes de usar
"""
import json

# --------------------------------------------------------------------------
# eixos ortogonais: nao entram no caminho de menu, sao aplicados por fora
# --------------------------------------------------------------------------
ELEMENTS = {
    "gold": {"key": "F5", "en": ["gold", "gold team", "team", "all", "everyone"],
             "pt": ["ouro", "time ouro", "equipe ouro", "todos", "geral"]},
    "blue": {"key": "F6", "en": ["blue", "blue team"],
             "pt": ["azul", "time azul", "equipe azul"]},
    "red":  {"key": "F7", "en": ["red", "red team"],
             "pt": ["vermelho", "time vermelho", "equipe vermelha"]},
}

MODIFIERS = {
    "queue": {
        "how": "segurar HOLD_COMMAND (LShift) em volta da ULTIMA tecla do caminho",
        "en": ["queue", "hold", "prep", "prepare", "on my mark", "standby"],
        "pt": ["prepara", "aguarda ordem", "no meu comando", "segura", "espera"],
    }
}

# --------------------------------------------------------------------------
# matriz de breach: 5 metodos x 6 formas de limpar = 30 ordens geradas
# --------------------------------------------------------------------------
BREACH_METHODS = {
    1: ("kick",    ["kick", "kick it", "kick the door"],
                   ["chuta", "chuta a porta", "arromba chutando"]),
    2: ("shotgun", ["shotgun", "shoot the hinges", "breaching shotgun"],
                   ["escopeta", "arromba com escopeta", "atira na dobradica"]),
    3: ("c2",      ["c2", "blow it", "blow the door", "charge it"],
                   ["c2", "explode", "explode a porta", "detona a porta"]),
    4: ("ram",     ["ram", "ram it", "battering ram"],
                   ["ariete", "arromba com ariete", "arromba na marra"]),
    5: ("leader",  ["leader", "breach and wait", "breach for me"],
                   ["lider", "arromba e espera", "arromba pra mim"]),
}

CLEAR_OPTIONS = {
    1: ("clear",     ["and clear", "clear", "go dynamic"],
                     ["e limpa", "limpa", "entra e limpa"]),
    2: ("flashbang", ["with flashbang", "flash and clear", "bang and clear",
                      "with flash", "with bang"],
                     ["com flash", "com flashbang", "flash e limpa",
                      "com granada de luz"]),
    3: ("stinger",   ["with stinger", "sting and clear", "with sting",
                      "with rubber ball"],
                     ["com stinger", "com sting", "stinger e limpa",
                      "com bola de borracha"]),
    4: ("gas",       ["with gas", "with cs gas", "gas and clear", "with tear gas"],
                     ["com gas", "com gas lacrimogeneo", "gas e limpa"]),
    5: ("launcher",  ["with launcher", "with grenade launcher", "launcher and clear"],
                     ["com lancador", "com lanca granadas"]),
    6: ("leader",    ["with leader", "leader and clear", "wait for me"],
                     ["com lider", "espera por mim", "e me espera"]),
}


def breach_matrix():
    out = []
    for mkey, (mid, men, mpt) in BREACH_METHODS.items():
        for ckey, (cid, cen, cpt) in CLEAR_OPTIONS.items():
            out.append({
                "id": "door.breach.%s.%s" % (mid, cid),
                "context": "door",
                "path": ["MENU", str(mkey), str(ckey)],
                "menu_path": ["Breach", mid, cid],
                "confidence": "confirmed" if ckey <= 4 else "verify",
                "phrases": {
                    "en": ["%s %s" % (m, c) for m in men for c in cen][:8],
                    "pt": ["%s %s" % (m, c) for m in mpt for c in cpt][:8],
                },
            })
    # o "3" de Breach entra antes do metodo
    for o in out:
        o["path"] = ["MENU", "3"] + o["path"][1:]
    return out


def open_matrix():
    out = []
    for ckey, (cid, cen, cpt) in CLEAR_OPTIONS.items():
        out.append({
            "id": "door.open.%s" % cid,
            "context": "door",
            "path": ["MENU", "2", str(ckey)],
            "menu_path": ["Open", cid],
            "confidence": "confirmed" if ckey <= 4 else "verify",
            "phrases": {
                "en": ["open %s" % c for c in cen] +
                      ["move in %s" % c for c in cen[:2]],
                "pt": ["abre %s" % c for c in cpt] +
                      ["entra %s" % c for c in cpt[:2]],
            },
        })
    return out


# --------------------------------------------------------------------------
# ordens avulsas
# --------------------------------------------------------------------------
SINGLES = [
    # ---- contexto PORTA ----
    ("door.stack.auto",   "door", ["MENU", "1", "4"], ["Stack Up", "auto"], "confirmed",
     ["stack up", "stack", "stack on the door", "post up"],
     ["empilha", "empilha na porta", "forma na porta", "posiciona na porta"]),
    ("door.stack.split",  "door", ["MENU", "1", "1"], ["Stack Up", "split"], "confirmed",
     ["split", "split up", "split the stack"],
     ["divide", "divide o time", "separa"]),
    ("door.stack.left",   "door", ["MENU", "1", "2"], ["Stack Up", "left"], "confirmed",
     ["stack left", "left side", "on the left"],
     ["empilha a esquerda", "lado esquerdo", "pela esquerda"]),
    ("door.stack.right",  "door", ["MENU", "1", "3"], ["Stack Up", "right"], "confirmed",
     ["stack right", "right side", "on the right"],
     ["empilha a direita", "lado direito", "pela direita"]),

    ("door.scan.slide",   "door", ["MENU", "4", "1"], ["Scan", "slide"], "confirmed",
     ["slide", "slide it", "scan slide"],
     ["desliza", "escaneia deslizando"]),
    ("door.scan.pie",     "door", ["MENU", "4", "2"], ["Scan", "pie"], "confirmed",
     ["pie", "pie it", "slice the pie"],
     ["fatia", "fatia a porta"]),
    ("door.scan.peek",    "door", ["MENU", "4", "3"], ["Scan", "peek"], "confirmed",
     ["peek", "peek it", "take a peek"],
     ["espia", "da uma espiada"]),

    ("door.mirror",       "door", ["MENU", "5"], ["Mirror Under Door"], "verify",
     ["mirror", "mirror under door", "mirror it", "check under the door"],
     ["espelho", "espelho embaixo da porta", "olha embaixo da porta"]),
    ("door.wedge",        "door", ["MENU", "6"], ["Wedge Door"], "confirmed",
     ["wedge", "wedge it", "wedge the door", "jam the door"],
     ["trava a porta", "calca a porta", "poe a cunha"]),
    ("door.cover",        "door", ["MENU", "7"], ["Cover Door"], "confirmed",
     ["cover the door", "cover this door", "watch the door"],
     ["cobre a porta", "vigia a porta", "fica na porta"]),
    ("door.toggle",       "door", ["MENU", "8"], ["Open/Close Door"], "confirmed",
     ["open the door", "close the door", "shut the door", "just open it"],
     ["abre a porta", "fecha a porta", "so abre a porta"]),
    ("door.picklock",     "door", ["MENU", "2"], ["Pick Lock"], "verify",
     ["pick the lock", "pick it", "unlock it", "lockpick"],
     ["abre a fechadura", "arromba a fechadura", "destranca"]),
    ("door.disarm",       "door", ["MENU", "1", "3"], ["Disarm Trap"], "verify",
     ["disarm the trap", "disarm it", "defuse the trap"],
     ["desarma a armadilha", "desarma isso", "neutraliza a armadilha"]),

    # ---- contexto ESPACO ABERTO / padrao ----
    ("move.to",           "default", ["MENU", "1"], ["Move To"], "confirmed",
     ["move there", "go there", "move up", "go", "push there"],
     ["vai la", "avanca", "move pra la", "vai ali"]),
    ("move.fallin",       "default", ["MENU", "2", "2"], ["Position", "Fall In"], "confirmed",
     ["fall in", "on me", "form up", "regroup", "follow me", "group up"],
     ["comigo", "na minha", "forma comigo", "reagrupa", "me segue"]),
    ("move.formation.single", "default", ["MENU", "2", "2", "1"],
     ["Position", "Fall In", "Single File"], "verify",
     ["single file", "single file formation", "line up"],
     ["fila indiana", "em fila", "formacao em fila"]),
    ("move.formation.double", "default", ["MENU", "2", "2", "2"],
     ["Position", "Fall In", "Double File"], "verify",
     ["double file", "double file formation"],
     ["fila dupla", "formacao dupla"]),
    ("move.formation.diamond", "default", ["MENU", "2", "2", "3"],
     ["Position", "Fall In", "Diamond"], "verify",
     ["diamond", "diamond formation"],
     ["diamante", "formacao diamante"]),
    ("move.formation.wedge", "default", ["MENU", "2", "2", "4"],
     ["Position", "Fall In", "Wedge"], "verify",
     ["wedge formation", "wedge up"],
     ["cunha", "formacao cunha"]),

    ("hold",              "default", ["MENU", "4"], ["Hold"], "confirmed",
     ["hold", "hold position", "hold up", "halt", "stop", "freeze there"],
     ["aguarda", "segura posicao", "para", "parado", "espera ai"]),
    ("cover",             "default", ["MENU", "3"], ["Cover"], "verify",
     ["cover me", "cover my back", "watch my six", "watch my back"],
     ["me cobre", "cobre minhas costas", "fica de olho atras"]),
    ("search",            "default", ["MENU", "6"], ["Search & Secure"], "confirmed",
     ["search", "search the room", "search and secure", "sweep it",
      "secure the area", "clear the area"],
     ["vasculha", "revista o comodo", "limpa a area", "varre o comodo",
      "assegura a area"]),

    ("deploy.flashbang",  "default", ["MENU", "5", "1"], ["Deploy", "Flashbang"], "confirmed",
     ["deploy flashbang", "throw a flashbang", "toss a flash", "bang it"],
     ["joga flash", "solta flashbang", "joga granada de luz"]),
    ("deploy.stinger",    "default", ["MENU", "5", "2"], ["Deploy", "Stinger"], "confirmed",
     ["deploy stinger", "throw a stinger", "toss a sting"],
     ["joga stinger", "solta stinger"]),
    ("deploy.gas",        "default", ["MENU", "5", "3"], ["Deploy", "CS Gas"], "confirmed",
     ["deploy gas", "throw cs gas", "gas them"],
     ["joga gas", "solta gas lacrimogeneo", "gaseia"]),
    ("deploy.chemlight",  "default", ["MENU", "5", "4"], ["Deploy", "Chemlight"], "verify",
     ["deploy chemlight", "drop a chemlight", "mark it"],
     ["joga chemlight", "marca com luz", "solta a luz"]),
    ("deploy.shield",     "default", ["MENU", "5", "5"], ["Deploy", "Shield"], "verify",
     ["deploy shield", "shield up", "bring the shield"],
     ["poe o escudo", "traz o escudo"]),

    # ---- contexto PESSOA (suspeito / civil) ----
    ("person.restrain",   "person", ["MENU", "1"], ["Restrain"], "verify",
     ["restrain him", "cuff him", "arrest him", "zip tie him", "restrain them"],
     ["algema ele", "prende ele", "amarra ele", "algema"]),
    ("person.moveto",     "person", ["MENU", "2"], ["Move To"], "verify",
     ["move him there", "suspect move there", "move him"],
     ["leva ele", "move o suspeito", "tira ele dai"]),

    # ---- acoes diretas do jogador (sem menu) ----
    ("player.yell",       "any", ["KEY:INTERACT"], ["Yell for compliance"], "confirmed",
     ["hands up", "get down", "freeze", "police", "on the ground",
      "yell", "comply"],
     ["maos ao alto", "deita no chao", "policia", "para", "no chao",
      "manda deitar"]),
    ("player.chemlight",  "any", ["KEY:C"], ["Deploy chemlight (player)"], "confirmed",
     ["chemlight", "drop chemlight", "light out"],
     ["chemlight", "solta luz", "marca aqui"]),
    ("player.fireselect", "any", ["KEY:X"], ["Fire select"], "confirmed",
     ["fire select", "switch fire mode", "full auto", "semi auto"],
     ["modo de tiro", "muda o tiro", "automatico", "semi automatico"]),
    ("player.exfil",      "any", ["KEY:PAGEUP"], ["Vote / exfil"], "verify",
     ["we're done here", "let's go", "clear for exfil", "let's go home",
      "mission complete"],
     ["acabou aqui", "vamos embora", "liberado pra sair", "missao completa"]),

    # ---- confirmar pedido de ordem ----
    ("confirm.default",   "any", ["KEY:DEFAULT_COMMAND"], ["Default command"], "confirmed",
     ["execute", "do it", "go go go", "affirmative", "confirm"],
     ["executa", "manda ver", "vai vai vai", "positivo", "confirma"]),
]


def singles():
    out = []
    for cid, ctx, path, mpath, conf, en, pt in SINGLES:
        out.append({
            "id": cid, "context": ctx, "path": path, "menu_path": mpath,
            "confidence": conf, "phrases": {"en": en, "pt": pt},
        })
    return out


DOC = {
    "_readme": [
        "Mapa de comandos do Ready or Not para controle por voz.",
        "",
        "COMO EXECUTAR UMA ORDEM:",
        "  1. (opcional) tecla do elemento: F5 gold / F6 blue / F7 red",
        "  2. abrir o menu SWAT: clique do meio (MENU)",
        "  3. pressionar cada tecla de 'path', em ordem, ~35ms cada",
        "  4. se o modificador 'queue' foi dito, segurar LShift em volta da",
        "     ULTIMA tecla do caminho (assim a ordem fica engatilhada)",
        "",
        "path usa tokens: MENU = clique do meio; digitos = teclas 1-9;",
        "KEY:X = tecla direta (fora do menu).",
        "",
        "ATENCAO — o menu e' CONTEXTUAL. O mesmo caminho significa coisas",
        "diferentes conforme o que esta na mira (porta / pessoa / espaco",
        "aberto). O campo 'context' diz para onde o jogador precisa estar",
        "olhando. O software nao tem como saber isso: e' responsabilidade",
        "do jogador, igual ao VoiceAttack.",
        "",
        "As teclas NAO devem ser fixas no codigo: leia o Input.ini do jogo",
        "em %LOCALAPPDATA%\\ReadyOrNot\\Saved\\Config\\WindowsNoEditor\\",
    ],
    "keybind_defaults": {
        "swat_command_menu": "MiddleMouse",
        "default_command": "Z",
        "hold_command": "LeftShift",
        "back": "Tab",
        "select_gold": "F5", "select_blue": "F6", "select_red": "F7",
        "command_keys": ["1", "2", "3", "4", "5", "6", "7", "8", "9"],
        "interact_yell": "F",
        "_nota": "o perfil v5.1 usava Space como default_command; confirme no Input.ini",
    },
    "timing": {
        "key_hold_ms": 35,
        "gap_between_keys_ms": 35,
        "menu_open_settle_ms": 60,
        "_nota": "35ms ~ 2 frames a 60fps; o perfil VoiceAttack usa 0.033s",
    },
    "elements": ELEMENTS,
    "modifiers": MODIFIERS,
}


def main():
    orders = singles() + open_matrix() + breach_matrix()
    DOC["orders"] = orders
    with open('ron_commands.json', 'w') as f:
        json.dump(DOC, f, indent=1, ensure_ascii=False)
    conf = sum(1 for o in orders if o['confidence'] == 'confirmed')
    print('ordens: %d (%d confirmadas, %d a verificar)'
          % (len(orders), conf, len(orders) - conf))
    print('frases EN: %d | PT: %d'
          % (sum(len(o['phrases']['en']) for o in orders),
             sum(len(o['phrases']['pt']) for o in orders)))


if __name__ == '__main__':
    main()
