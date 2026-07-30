# tools/apply_names.py — nomes legiveis para as 70 ordens.
#
# Roda sobre o data/ron_commands.json ja' existente e so' ACRESCENTA o campo
# "name". Nao regenera nada: o build_commands.py divergiu do mapa versionado
# (gera 402/373 frases contra as 399/371 testadas), e regenerar hoje mudaria o
# corpus caladamente. Enquanto essa divergencia nao for reconciliada, extras
# entram por aqui.
#
# Nomes legiveis para as 70 ordens. O id continua sendo a chave — nada muda no
# minhas_frases.json — e o nome entra ao lado, para a tela e a busca.
import json

SINGLES = {
    "door.stack.auto":            "Empilhar na porta",
    "door.stack.split":           "Dividir nos dois lados da porta",
    "door.stack.left":            "Empilhar à esquerda",
    "door.stack.right":           "Empilhar à direita",
    "door.scan.slide":            "Passar a câmera por baixo da porta",
    "door.scan.pie":              "Varrer o cômodo em leque",
    "door.scan.peek":             "Espiar pela porta",
    "door.mirror":                "Usar o espelho na porta",
    "door.wedge":                 "Calçar a porta",
    "door.cover":                 "Cobrir a porta",
    "door.toggle":                "Abrir ou fechar a porta",
    "door.picklock":              "Abrir a fechadura",
    "door.disarm":                "Desarmar a armadilha",
    "move.to":                    "Ir até onde eu estou olhando",
    "move.fallin":                "Reagrupar comigo",
    "move.formation.single":      "Formação: fila indiana",
    "move.formation.double":      "Formação: fila dupla",
    "move.formation.diamond":     "Formação: diamante",
    "move.formation.wedge":       "Formação: cunha",
    "hold":                       "Aguardar no lugar",
    "cover":                      "Cobrir a minha posição",
    "search":                     "Vasculhar e garantir o cômodo",
    "deploy.flashbang":           "Jogar flash",
    "deploy.stinger":             "Jogar stinger",
    "deploy.gas":                 "Jogar gás",
    "deploy.chemlight":           "Jogar chemlight",
    "deploy.shield":              "Pôr o escudo",
    "person.restrain":            "Algemar a pessoa",
    "person.moveto":              "Mandar a pessoa até mim",
    "player.yell":                "Gritar por rendição",
    "player.chemlight":           "Eu jogo um chemlight",
    "player.fireselect":          "Trocar o meu modo de tiro",
    "player.exfil":               "Encerrar a missão",
    "confirm.default":            "Executar a ordem padrão",
}

METHOD = {
    "kick":    "Chutar a porta",
    "shotgun": "Escopeta na dobradiça",
    "c2":      "C2 na porta",
    "ram":     "Aríete na porta",
    "leader":  "Líder arromba",
}

GRENADE = {
    "clear":     "e limpar",
    "flashbang": "com flash",
    "stinger":   "com stinger",
    "gas":       "com gás",
    "launcher":  "com lançador",
    "leader":    "com granada do líder",
}


def name_for(order_id):
    if order_id in SINGLES:
        return SINGLES[order_id]

    parts = order_id.split('.')
    if parts[:2] == ['door', 'open']:
        return "Abrir a porta %s" % GRENADE[parts[2]]
    if parts[:2] == ['door', 'breach']:
        return "%s %s" % (METHOD[parts[2]], GRENADE[parts[3]])
    return None


def main():
    path = 'data/ron_commands.json'
    doc = json.load(open(path, encoding='utf-8'))

    missing, seen = [], {}
    for o in doc['orders']:
        n = name_for(o['id'])
        if n is None:
            missing.append(o['id'])
            continue
        if n in seen:
            raise SystemExit('nome repetido: %r em %s e %s' % (n, seen[n], o['id']))
        seen[n] = o['id']
        o['name'] = n

    if missing:
        raise SystemExit('sem nome: %s' % missing)

    with open(path, 'w', encoding='utf-8') as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print('nomeadas: %d de %d' % (len(seen), len(doc['orders'])))


if __name__ == '__main__':
    main()
