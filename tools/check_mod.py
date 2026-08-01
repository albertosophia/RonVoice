"""tools/check_mod.py — tudo que da' para conferir no mod sem abrir o jogo.

    python tools/check_mod.py

Roda os tres contratos e, antes deles, compila todo Lua do mod. O main.lua nao
da' para carregar aqui fora (ele chama RegisterHook, que so' existe dentro do
jogo), mas da' para COMPILAR — e' o que pega virgula faltando, `end` a mais,
nome de variavel errado. Sem isto, um erro de digitacao no main.lua so'
apareceria com o jogo aberto, e apareceria como o mod nao fazer nada.
"""
import pathlib
import subprocess
import sys

try:
    from lupa import LuaRuntime
except ImportError:
    sys.exit("precisa de lupa: pip install lupa")

RAIZ = pathlib.Path(__file__).resolve().parent.parent
SCRIPTS = RAIZ / "tools" / "RonVoiceMod" / "Scripts"
CONTRATOS = ["check_mailbox_contract.py", "check_dispatch_contract.py",
             "check_runner_contract.py"]


def compila():
    """Todo .lua do mod tem que ao menos compilar."""
    lua = LuaRuntime()
    # load devolve so' a funcao quando compila, e nil mais o erro quando nao;
    # a mensagem de erro e' o unico retorno que interessa aqui.
    carrega = lua.eval("""
        function(fonte, nome)
            local fn, erro = load(fonte, nome)
            return erro
        end
    """)

    falhas = []
    arquivos = sorted(SCRIPTS.glob("*.lua"))
    for arq in arquivos:
        erro = carrega(arq.read_text(encoding="utf-8"), arq.name)
        if erro is not None:
            falhas.append(f"{arq.name}: {erro}")

    return arquivos, falhas


def main():
    arquivos, falhas = compila()
    if falhas:
        print("LUA NAO COMPILA:")
        for f in falhas:
            print("  ", f)
        return 1
    # flush antes dos subprocessos, senao a saida sai fora de ordem
    print(f"sintaxe: OK  ({len(arquivos)} arquivos)", flush=True)

    for contrato in CONTRATOS:
        r = subprocess.run([sys.executable, str(RAIZ / "tools" / contrato)])
        if r.returncode != 0:
            return r.returncode

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
