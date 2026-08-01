"""tools/check_runner_contract.py — prova o laco que roda dentro do jogo.

O runner e' o que le' o pedido, planeja, executa e responde. Roda vinte vezes
por segundo dentro do laco do jogo, onde nao ha' como rodar teste e onde um erro
nao aparece: no maximo o esquadrao nao obedece, e ninguem sabe por que.

Tres coisas so' quebram aqui e em lugar nenhum antes:
  - executar o mesmo pedido duas vezes (a ordem sai dobrada)
  - executar e nao responder (o app fica dizendo que o mod nao respondeu)
  - a chamada estourar e o laco morrer junto (o mod para de funcionar calado)

O mailbox e a chamada ao jogo entram de fora; o dispatch e' o de verdade.

    python tools/check_runner_contract.py
"""
import pathlib
import sys

try:
    from lupa import LuaRuntime
except ImportError:
    sys.exit("precisa de lupa: pip install lupa")

SCRIPTS = pathlib.Path(__file__).resolve().parent.parent / "tools" / "RonVoiceMod" / "Scripts"


class Cenario:
    """Um jogo de mentira: guarda o que foi chamado e o que foi respondido."""

    def __init__(self, lua, pedido=None, estoura=False, alvo="ALVO"):
        self.lua = lua
        self.pedido = pedido
        self.estoura = estoura
        self.alvo = alvo
        self.chamadas = []
        self.recibos = []

    def deps(self):
        caixa = self.lua.table_from({
            "read": lambda: self.lua.table_from(self.pedido) if self.pedido else None,
            "acknowledge": lambda seq, status: self.recibos.append((int(seq), status)),
        })

        def chama(plano):
            if self.estoura:
                raise RuntimeError("o jogo recusou")
            self.chamadas.append(plano.fn)

        return self.lua.table_from({
            "mailbox": caixa,
            "call": chama,
            "world": self.lua.table_from({
                "target": self.alvo, "location": "LOCAL", "up": "CIMA",
                "activeTeam": 1,
                "findClass": self.lua.eval("function(c) return 'classe:' .. c end"),
            }),
        })


def carrega():
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute(f'package.path = [[{SCRIPTS.as_posix()}/?.lua]] .. ";" .. package.path')
    return lua, lua.execute("return (require('runner'))")


def pedido(seq=1, order="hold", element=None, queue=False):
    return dict(sequence=seq, order=order, element=element, queue=queue)


def main():
    lua, runner = carrega()
    falhas = []

    def checa(nome, cond, detalhe=""):
        if not cond:
            falhas.append(f"{nome}{': ' + detalhe if detalhe else ''}")

    # --- sem pedido, nada acontece. Responder a um pedido que nao existe faria
    # o app achar que a ordem anterior foi refeita.
    c = Cenario(lua, pedido=None)
    estado = runner.newState()
    runner.tick(estado, c.deps())
    checa("sem pedido nao chama", c.chamadas == [], str(c.chamadas))
    checa("sem pedido nao responde", c.recibos == [], str(c.recibos))

    # --- pedido novo: executa e responde.
    c = Cenario(lua, pedido=pedido(7, "hold"))
    estado = runner.newState()
    runner.tick(estado, c.deps())
    checa("executa a ordem", c.chamadas == ["GiveHoldCommand"], str(c.chamadas))
    checa("responde ok", c.recibos == [(7, "ok")], str(c.recibos))

    # --- o mesmo pedido de novo NAO repete. O arquivo continua la' entre um
    # quadro e outro: sem isto a ordem sairia vinte vezes por segundo.
    c = Cenario(lua, pedido=pedido(7, "hold"))
    estado = runner.newState()
    for _ in range(5):
        runner.tick(estado, c.deps())
    checa("nao repete o mesmo pedido", len(c.chamadas) == 1, f"{len(c.chamadas)} chamadas")
    checa("nao responde duas vezes", len(c.recibos) == 1, f"{len(c.recibos)} recibos")

    # --- pedido seguinte passa.
    c = Cenario(lua, pedido=pedido(7, "hold"))
    estado = runner.newState()
    runner.tick(estado, c.deps())
    c.pedido = pedido(8, "search")
    runner.tick(estado, c.deps())
    checa("o proximo pedido passa",
          c.chamadas == ["GiveHoldCommand", "GiveSearchAndSecureCommand"], str(c.chamadas))

    # --- ordem que o mod nao conhece: responde o motivo, sem chamar nada. E'
    # isto que vira mensagem na tela em vez de silencio.
    c = Cenario(lua, pedido=pedido(9, "nao.existe"))
    runner.tick(runner.newState(), c.deps())
    checa("nao chama o que nao conhece", c.chamadas == [], str(c.chamadas))
    checa("diz por que nao deu",
          len(c.recibos) == 1 and c.recibos[0][0] == 9 and c.recibos[0][1] != "ok",
          str(c.recibos))

    # --- sem porta mirada: mesma coisa, com o motivo certo.
    c = Cenario(lua, pedido=pedido(10, "door.breach.kick.gas"), alvo=None)
    runner.tick(runner.newState(), c.deps())
    checa("sem porta nao arromba", c.chamadas == [], str(c.chamadas))
    checa("sem porta diz o motivo",
          len(c.recibos) == 1 and "porta" in c.recibos[0][1], str(c.recibos))

    # --- a chamada estoura: o laco NAO morre junto, e o recibo conta. Este roda
    # dentro do jogo; um erro solto aqui derruba o mod para sempre.
    c = Cenario(lua, pedido=pedido(11, "hold"), estoura=True)
    estado = runner.newState()
    try:
        runner.tick(estado, c.deps())
    except Exception as e:
        falhas.append(f"o erro vazou do tick: {e}")
    checa("erro vira recibo",
          len(c.recibos) == 1 and c.recibos[0][1] != "ok", str(c.recibos))
    # e o pedido fica marcado como respondido, senao tenta de novo para sempre
    runner.tick(estado, c.deps())
    checa("nao insiste no que falhou", len(c.recibos) == 1, str(c.recibos))

    # --- o elemento escolhe o time. Red=1, Blue=2, Gold=5, como o RoNSpeech faz.
    for elemento, time in [("red", 1), ("blue", 2), ("gold", 5), (None, 1)]:
        checa(f"elemento {elemento} -> time {time}",
              runner.teamFor(elemento, 1) == time,
              str(runner.teamFor(elemento, 1)))

    if falhas:
        print("LACO QUEBRADO:")
        for f in falhas:
            print("  ", f)
        return 1

    print("laco do mod: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
