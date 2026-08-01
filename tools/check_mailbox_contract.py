"""tools/check_mailbox_contract.py — prova que o Lua entende o que o C# escreve.

O formato da caixa de correio e' um contrato entre duas linguagens que NAO
compilam juntas. O C# pode mudar a linha e a suite inteira continuar verde,
porque o Lua vive dentro do jogo e nada avisa. Este script e' o unico lugar
onde as duas pontas se encontram de verdade.

    pip install lupa
    python tools/check_mailbox_contract.py

Os testes em CommandMailboxTests prendem o formato letra por letra do lado C#;
aqui a mesma letra e' lida pelo parser que roda no jogo.
"""
import pathlib
import sys

try:
    from lupa import LuaRuntime
except ImportError:
    sys.exit("precisa de lupa: pip install lupa")

RAIZ = pathlib.Path(__file__).resolve().parent.parent
LUA = RAIZ / "tools" / "RonVoiceProbe" / "Scripts" / "mailbox.lua"

# As linhas exatas que CommandMailboxTests afirma que o C# escreve. Se alguem
# mudar o formato la', tem que mudar aqui — e ai lembra do Lua.
ACEITAS = [
    ("1|door.stack.auto|-|0",
     dict(sequence=1, order="door.stack.auto", element=None, queue=False)),
    ("1|door.open.flashbang|red|1",
     dict(sequence=1, order="door.open.flashbang", element="red", queue=True)),
    ("1|-|blue|0",
     dict(sequence=1, order=None, element="blue", queue=False)),
    ("17|door.breach.ram.clear|red|1",
     dict(sequence=17, order="door.breach.ram.clear", element="red", queue=True)),
]

# Meia linha nunca pode virar meia ordem: o mod le' enquanto o C# escreve.
RECUSADAS = [
    "",                              # arquivo vazio
    "lixo",
    "1|",                            # cortada no meio
    "17|door.stack.auto|red",        # falta o campo de fila
    "|a|b|0",                        # sem sequencia
    "x|a|-|0",                       # sequencia nao numerica
    "1|-|-|0",                       # nem ordem nem elemento
]


def main():
    lua = LuaRuntime(unpack_returned_tuples=True)
    mailbox = lua.execute(LUA.read_text(encoding="utf-8"))

    falhas = []

    for linha, esperado in ACEITAS:
        r = mailbox.parse(linha)
        if r is None:
            falhas.append(f"{linha!r}: o Lua devolveu nil")
            continue
        obtido = dict(sequence=int(r.sequence), order=r.order,
                      element=r.element, queue=bool(r.queue))
        if obtido != esperado:
            falhas.append(f"{linha!r}: {obtido} != {esperado}")

    for ruim in RECUSADAS:
        if mailbox.parse(ruim) is not None:
            falhas.append(f"{ruim!r}: devia ser recusada, virou ordem")

    if falhas:
        print("CONTRATO QUEBRADO:")
        for f in falhas:
            print("  ", f)
        return 1

    print(f"contrato Lua <-> C#: OK  "
          f"({len(ACEITAS)} aceitas, {len(RECUSADAS)} recusadas)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
