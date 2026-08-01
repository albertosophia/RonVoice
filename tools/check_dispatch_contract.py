"""tools/check_dispatch_contract.py — prova que cada id vira a chamada certa.

O dispatch.lua e' onde "door.breach.kick.gas" vira
GiveBreachAndClearCommand(alvo, 3, time, local, <classe do gas>, ...). Errar um
argumento nao da erro nenhum: o esquadrao obedece a ordem errada, no meio da
missao. E isso roda dentro do jogo, onde ninguem consegue rodar teste.

Por isso o dispatch decide sem agir: devolve um PLANO — qual funcao, quais
argumentos — e quem executa e' o main.lua. O plano da' para conferir aqui fora.

    pip install lupa
    python tools/check_dispatch_contract.py
"""
import pathlib
import sys

try:
    from lupa import LuaRuntime
except ImportError:
    sys.exit("precisa de lupa: pip install lupa")

RAIZ = pathlib.Path(__file__).resolve().parent.parent
SCRIPTS = RAIZ / "tools" / "RonVoiceMod" / "Scripts"

GAS = "/Game/Blueprints/Items/WeaponsRevised/Grenade_CSGas_V2.Grenade_CSGas_V2_C"
FLASH = "/Game/Blueprints/Items/WeaponsRevised/Grenade_Flashbang_V2.Grenade_Flashbang_V2_C"


def carrega():
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute(f'package.path = [[{SCRIPTS.as_posix()}/?.lua]] .. ";" .. package.path')
    # Os parenteses cortam o segundo valor que require devolve em Lua 5.4.
    return lua, lua.execute("return (require('dispatch'))")


def contexto(lua, **kw):
    """O que o jogo daria: porta mirada, time ativo, onde o jogador esta."""
    base = dict(target="ALVO", team=1, location="LOCAL", up="CIMA",
                findClass=lua.eval("function(caminho) return 'classe:' .. caminho end"))
    base.update(kw)
    return lua.table_from(base)


def planeja(dispatch, id_, ctx):
    """plan devolve um valor quando da' certo e dois quando recusa; em Lua isso
    e' natural, no Python vira tupla so' as vezes."""
    r = dispatch.plan(id_, ctx)
    return r if isinstance(r, tuple) else (r, None)


def args(plano):
    """Os argumentos do plano, na ordem, com nil preservado."""
    return [plano.args[i] for i in range(1, int(plano.argc) + 1)]


CASOS = [
    # (id, contexto extra, funcao esperada, argumentos esperados)
    ("door.breach.kick.gas", {}, "GiveBreachAndClearCommand",
     ["ALVO", 3, 1, "LOCAL", "classe:" + GAS, None,
      False, False, False, False, 0, True]),

    ("door.breach.shotgun.clear", {}, "GiveBreachAndClearCommand",
     ["ALVO", 4, 1, "LOCAL", None, None,
      False, False, False, False, 0, True]),

    # Lancador e granada do lider viram os argumentos 7 e 8. O 8 nunca aparece
    # sozinho no RoNSpeech, so' junto do 7 — entao lider e' os dois.
    ("door.open.launcher", {}, "GiveBreachAndClearCommand",
     ["ALVO", 1, 1, "LOCAL", None, None,
      True, False, False, False, 0, True]),
    ("door.open.leader", {}, "GiveBreachAndClearCommand",
     ["ALVO", 1, 1, "LOCAL", None, None,
      True, True, False, False, 0, True]),

    ("door.breach.c2.flashbang", {}, "GiveBreachAndClearCommand",
     ["ALVO", 6, 1, "LOCAL", "classe:" + FLASH, None,
      False, False, False, False, 0, True]),

    ("door.stack.auto", {}, "GiveStackUpCommand",
     ["ALVO", 1, "LOCAL", "CIMA", True, 0]),
    ("door.stack.right", {}, "GiveStackUpCommand",
     ["ALVO", 1, "LOCAL", "CIMA", True, 3]),

    ("door.scan.pie", {}, "GiveScanDoorCommand", ["ALVO", 1, "LOCAL", 2]),
    ("door.toggle", {}, "GiveOpenDoorCommand", ["ALVO", 1, "LOCAL"]),
    ("door.picklock", {}, "GivePickLockCommand", ["ALVO", 1, "LOCAL"]),
    ("door.wedge", {}, "GiveWedgeDoorCommand", ["ALVO", 1, "LOCAL"]),
    ("door.disarm", {}, "GiveDisarmTrapOnDoorCommand", ["ALVO", 1, "LOCAL"]),

    ("hold", {}, "GiveHoldCommand", [1]),
    ("cover", {}, "GiveCoverAreaCommand", [1, "LOCAL"]),
    ("move.to", {}, "GiveMoveCommand", [1, "LOCAL"]),
    ("search", {}, "GiveSearchAndSecureCommand", [1, "LOCAL", True]),
    ("move.fallin", {}, "GiveFallInCommand", [1, 0]),
    ("move.formation.wedge", {}, "GiveFallInCommand", [1, 3]),

    ("deploy.gas", {}, "GiveDeployGrenadeAtLocation", [1, "LOCAL", "classe:" + GAS]),
    ("deploy.chemlight", {}, "GiveDropChemlightAtLocation", [1, "LOCAL"]),
    ("person.restrain", {}, "GiveRestrainCommand", ["ALVO", 1, "LOCAL"]),

    # O elemento pedido por voz manda no time; sem ele, vale o time ativo.
    ("hold", dict(team=2), "GiveHoldCommand", [2]),
]

# Pedidos que NAO podem virar plano: agir mesmo assim seria mandar a ordem no
# escuro. Melhor recusar e o app avisar.
RECUSADOS = [
    ("nao.existe", {}, "id que nao esta na tabela"),
    ("door.breach.kick.gas", dict(target=None), "arrombar sem porta mirada"),
    ("door.stack.auto", dict(target=None), "empilhar sem porta mirada"),
]


def main():
    lua, dispatch = carrega()
    falhas = []

    for id_, extra, fn, esperado in CASOS:
        plano, motivo = planeja(dispatch, id_, contexto(lua, **extra))
        if plano is None:
            falhas.append(f"{id_}: recusou ({motivo}), devia planejar")
            continue
        if plano.fn != fn:
            falhas.append(f"{id_}: chama {plano.fn}, devia chamar {fn}")
            continue
        obtido = args(plano)
        if obtido != esperado:
            falhas.append(f"{id_}:\n       veio {obtido}\n       devia {esperado}")

    for id_, extra, porque in RECUSADOS:
        plano, _ = planeja(dispatch, id_, contexto(lua, **extra))
        if plano is not None:
            falhas.append(f"{id_}: planejou {plano.fn}, devia recusar — {porque}")

    # Toda entrada da tabela tem que ser despachavel: um `call` que o dispatch
    # nao conhece e' uma ordem que nunca acontece, e nada avisaria.
    orders = lua.execute("return (require('orders'))")
    for id_ in orders.orders:
        plano, motivo = planeja(dispatch, id_, contexto(lua))
        if plano is None:
            falhas.append(f"{id_}: a tabela tem, o dispatch nao sabe ({motivo})")

    if falhas:
        print("DESPACHO QUEBRADO:")
        for f in falhas:
            print("  ", f)
        return 1

    print(f"despacho: OK  ({len(CASOS)} planos conferidos, "
          f"{len(RECUSADOS)} recusas, {len(list(orders.orders))} ids varridos)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
