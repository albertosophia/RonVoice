#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Versao final: stopwords por idioma + fallback quando a filtragem esvazia."""
import json
import math
import sys
import unicodedata
import re
from collections import Counter

ron = json.load(open(sys.argv[1], encoding='utf-8'))

REMOVE = [('deploy.chemlight', 'en', 'drop a chemlight'),
          ('deploy.chemlight', 'pt', 'solta a luz'),
          ('player.yell', 'pt', 'para'),
          ('move.to', 'en', 'go'),
          ('door.breach.leader.leader', 'en', 'leader leader and clear')]
for oid, lg, ph in REMOVE:
    for o in ron['orders']:
        if o['id'] == oid:
            o['phrases'][lg] = [p for p in o['phrases'][lg] if p != ph]

STOP = {
    'en': {'the', 'a', 'an', 'and', 'to', 'of', 'on', 'it', 'that', 'for'},
    'pt': {'o', 'a', 'os', 'as', 'e', 'de', 'do', 'da', 'no', 'na', 'um',
           'uma', 'que'},
}


def norm(s):
    s = unicodedata.normalize('NFKD', s.lower())
    s = ''.join(c for c in s if not unicodedata.combining(c))
    return [t for t in re.sub(r"[^a-z0-9 ]+", ' ', s).split() if t]


ELEMS = {tuple(norm(al)): n for n, e in ron['elements'].items()
         for al in e['en'] + e['pt']}
QUEUE = {tuple(norm(al)) for al in
         ron['modifiers']['queue']['en'] + ron['modifiers']['queue']['pt']}
PHRASES = [(o['id'], lg, norm(p), p) for o in ron['orders']
           for lg in ('en', 'pt') for p in o['phrases'][lg]]

IDF, DEF = {}, {}
for lg in ('en', 'pt'):
    sub = [t for _, l, t, _ in PHRASES if l == lg]
    df = Counter()
    for t in sub:
        df.update(set(t) - STOP[lg])
    n = len(sub)
    IDF[lg] = {t: math.log(1 + n / (1 + c)) for t, c in df.items()}
    DEF[lg] = math.log(1 + n)


def filt(toks, lg):
    s = set(toks) - STOP[lg]
    return s if s else set(toks)          # frase so' de stopwords: usa cru


def score(A, B, lg):
    sa, sb = filt(A, lg), filt(B, lg)
    if not sa or not sb:
        return 0.0

    def w(t):
        return IDF[lg].get(t, DEF[lg])
    i = sa & sb
    return 2 * sum(map(w, i)) / (sum(map(w, sa)) + sum(map(w, sb)))


def strip_seq(toks, seqs):
    for s in sorted(seqs, key=len, reverse=True):
        n = len(s)
        for i in range(len(toks) - n + 1):
            if tuple(toks[i:i + n]) == s:
                return toks[:i] + toks[i + n:], s
    return toks, None


THR, MARGIN = 0.60, 0.05


def match(text, lang):
    toks = norm(text)
    rest, el = strip_seq(toks, ELEMS.keys())
    elname = ELEMS.get(el) if el else None
    rq, q = strip_seq(rest, QUEUE)
    cands = ([(rq, True)] if (q and rq) else []) + [(rest, False)]
    best = None
    for A, isq in cands:
        if not A:
            continue
        sc = sorted(((score(A, B, lang), oid) for oid, lg, B, _ in PHRASES
                     if lg == lang), key=lambda x: -x[0])
        if not sc or sc[0][0] < THR:
            continue
        top = sc[0]
        second = next((s for s in sc if s[1] != top[1]), (0.0, None))
        cand = (top[0], top[1] if (top[0] - second[0]) >= MARGIN else None, isq)
        if best is None or cand[0] > best[0] or (cand[0] == best[0] and isq):
            best = cand
    return (elname, None, False) if best is None else (elname, best[1], best[2])


CASES = [
    ('red team, open the door with flashbang', 'en', 'door.open.flashbang', 'red', False),
    ('open the door with flashbang', 'en', 'door.open.flashbang', None, False),
    ('red team', 'en', None, 'red', False),
    ('stack up left', 'en', 'door.stack.left', None, False),
    ('blue team prep breach and clear', 'en', 'door.breach.leader.clear', 'blue', True),
    ('banana pudim relogio', 'pt', None, None, False),
    ('hold', 'en', 'hold', None, False),
    ('hold position', 'en', 'hold', None, False),
    ('gold team hold', 'en', 'hold', 'gold', False),
    ('team', 'en', None, 'gold', False),
    ('red team hold up', 'en', 'hold', 'red', False),
    ('do it', 'en', 'confirm.default', None, False),
    ('go go go', 'en', 'confirm.default', None, False),
    ('time vermelho abre com flash', 'pt', 'door.open.flashbang', 'red', False),
    ('azul prepara empilha a esquerda', 'pt', 'door.stack.left', 'blue', True),
]

print('=== casos ===')
allok = True
for t, lang, wid, wel, wq in CASES:
    got = match(t, lang)
    ok = got == (wel, wid, wq)
    allok &= ok
    print('%s [%s] %-42r -> %s' % ('OK ' if ok else 'ERR', lang, t, got))
    if not ok:
        print('        esperado (%s, %s, %s)' % (wel, wid, wq))

print()
print('=== corpus gerado ===')
reach = {}
for lang in ('en', 'pt'):
    hit = rej = bad = 0
    for oid, lg, _, raw in PHRASES:
        if lg != lang:
            continue
        _, got, _ = match(raw, lang)
        reach.setdefault((oid, lang), False)
        if got == oid:
            hit += 1
            reach[(oid, lang)] = True
        elif got is None:
            rej += 1
            print('   rejeitada [%s] %r (%s)' % (lang, raw, oid))
        else:
            bad += 1
            print('   ERRADA    [%s] %r %s -> %s' % (lang, raw, oid, got))
    print('%s: acerto %d | rejeitada %d | ERRADA %d' % (lang, hit, rej, bad))

ids = {o['id'] for o in ron['orders']}
print('cobertura  en %d/%d   pt %d/%d'
      % (sum(1 for i in ids if reach.get((i, 'en'))), len(ids),
         sum(1 for i in ids if reach.get((i, 'pt'))), len(ids)))
print()
print('TODOS OS CASOS OK' if allok else '>>> HA CASOS FALHANDO <<<')
