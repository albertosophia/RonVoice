"""tools/make_icon.py — gera o ícone do RonVoice.

    pip install pillow
    python tools/make_icon.py

Escreve RonVoice.App/RonVoice.ico e uma prévia em docs/icon-preview.png.

Cada tamanho é DESENHADO, não reduzido. Encolher um 256 para 16 vira borrão
cinza: o que sobra de um traço de 2px depois de dividir por 16 é ruído. Nos
tamanhos pequenos o desenho também é mais simples — a haste e a base do
microfone viram sujeira a 16px, então some com elas e fica só a cápsula, que
é a parte que se reconhece.

As cores são as do tema do app: fundo de console escuro, âmbar de sinal.
"""
import pathlib

from PIL import Image, ImageDraw

FUNDO = (20, 22, 26, 255)        # #14161A — o mesmo chão da janela
AMBAR = (232, 163, 61, 255)      # #E8A33D — o âmbar de sinal
BORDA = (58, 63, 74, 255)        # a linha que separa painel de chão

TAMANHOS = [256, 128, 64, 48, 32, 24, 16]


def desenha(tam: int) -> Image.Image:
    # 4x e depois reduz: as bordas arredondadas e o arco ficam lisos, mas as
    # PROPORÇÕES são escolhidas neste tamanho, que é o que evita o borrão.
    s = tam * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    raio = int(s * 0.22)
    d.rounded_rectangle([0, 0, s - 1, s - 1], raio, fill=FUNDO,
                        outline=BORDA, width=max(1, s // 64))

    # O microfone inteiro em TODOS os tamanhos. Simplificar o 16 para só a
    # cápsula parecia razoável e não é: uma pílula âmbar sozinha lê como
    # comprimido. O que faz reconhecer é a silhueta com o berço — então nos
    # tamanhos pequenos o traço engrossa em vez de o desenho encolher.
    miudo = tam <= 24

    largura = s * (0.28 if miudo else 0.24)
    altura = s * (0.34 if miudo else 0.38)
    cx = s / 2
    topo = s * (0.16 if miudo else 0.15)

    d.rounded_rectangle(
        [cx - largura / 2, topo, cx + largura / 2, topo + altura],
        largura / 2, fill=AMBAR)

    # O berço abraça a cápsula: os braços SOBEM ao lado dela, e o fundo do arco
    # passa abaixo. Um arco largo, nascendo na altura da base da cápsula, vira
    # uma taça — foi o primeiro desenho, e não se reconhecia.
    grosso = max(2, int(s * (0.085 if miudo else 0.052)))
    base_capsula = topo + altura
    arco_l = largura * 1.62
    arco_t = topo + altura * 0.46
    arco_b = base_capsula + largura * 0.42

    d.arc([cx - arco_l / 2, arco_t, cx + arco_l / 2, arco_b],
          start=0, end=180, fill=AMBAR, width=grosso)

    pe = s * (0.82 if miudo else 0.86)
    d.line([cx, arco_b - grosso / 2, cx, pe], fill=AMBAR, width=grosso)

    # A barra do pé some abaixo de 32: vira uma linha de um pixel colada no
    # arco, e o que se ganha em fidelidade se perde em borrão.
    if not miudo:
        d.line([cx - largura * 0.62, pe, cx + largura * 0.62, pe],
               fill=AMBAR, width=grosso)

    return img.resize((tam, tam), Image.LANCZOS)


def escreve_ico(caminho: pathlib.Path, imagens: list) -> None:
    """Monta o .ico à mão, com um PNG por tamanho.

    O save(format="ICO", sizes=[...]) do Pillow NÃO usa imagens diferentes: ele
    pega a maior e reduz para cada tamanho, e os desenhos simplificados dos
    tamanhos pequenos vão para o lixo sem avisar. O formato é simples — um
    cabeçalho, uma entrada de 16 bytes por tamanho, e os PNGs em seguida — então
    sai mais barato escrever do que descobrir como convencer a biblioteca.
    """
    import io
    import struct

    blobs = []
    for im in imagens:
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        blobs.append(buf.getvalue())

    cabecalho = struct.pack("<HHH", 0, 1, len(imagens))
    offset = len(cabecalho) + 16 * len(imagens)

    entradas = b""
    for im, blob in zip(imagens, blobs):
        lado = 0 if im.width >= 256 else im.width   # 0 quer dizer 256
        entradas += struct.pack("<BBBBHHII", lado, lado, 0, 0, 1, 32,
                                len(blob), offset)
        offset += len(blob)

    caminho.write_bytes(cabecalho + entradas + b"".join(blobs))


def main():
    raiz = pathlib.Path(__file__).resolve().parent.parent
    imagens = [desenha(t) for t in TAMANHOS]

    ico = raiz / "RonVoice.App" / "RonVoice.ico"
    escreve_ico(ico, imagens)
    print(f"{ico.relative_to(raiz)}  ({ico.stat().st_size // 1024} KB, "
          f"{len(TAMANHOS)} tamanhos)")

    # Prévia lado a lado, para dar para olhar sem abrir o Windows.
    larg = sum(t for t in TAMANHOS) + 12 * len(TAMANHOS)
    tira = Image.new("RGBA", (larg, 272), (12, 13, 15, 255))
    x = 6
    for t, im in zip(TAMANHOS, imagens):
        tira.paste(im, (x, (272 - t) // 2), im)
        x += t + 12

    prev = raiz / "docs" / "icon-preview.png"
    tira.save(prev)
    print(f"{prev.relative_to(raiz)}")


if __name__ == "__main__":
    main()
