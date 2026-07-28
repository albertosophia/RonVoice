#!/usr/bin/env python3
"""
vapdec.py — decompilador de perfis VoiceAttack (.vap) no formato binario novo.

  uso:  python3 vapdec.py perfil.vap saida_base
        -> saida_base.json  (arvore completa, campos crus)
        -> saida_base.txt   (listagem legivel: comandos e acoes)

================================ O FORMATO ================================

CAMADA 1 — container
    O .vap inteiro e' um stream DEFLATE *cru* (sem header zlib nem gzip).
    Em Python:   zlib.decompress(bytes, -15)
    Em .NET:     new DeflateStream(fs, CompressionMode.Decompress)
    (perfis antigos do VoiceAttack sao XML puro; da' pra detectar pelo '<?xml')

CAMADA 2 — registros auto-descritivos
    Tudo e' little-endian. Um REGISTRO na posicao p:

        int32   len          tamanho total do registro em bytes
        int32   n            indice do ultimo campo (ha' n+1 campos)
        int32   off[0..n]    offset de cada campo, relativo a p
        bytes   payload

        campo i  = buf[p+off[i] : p+off[i+1]]     para i < n
        campo n  = buf[p+off[n] : p+len]          (ultimo campo)
        proximo registro irmao = p + len

    Invariante util pra validar: off[0] == 12 + 4*n

    Nao existe tag de tipo: o TIPO DE CADA CAMPO E' DEDUZIDO PELO TAMANHO
    DA FATIA. Esse e' o truque central do formato — a tabela de offsets
    substitui os marcadores de tipo, o que deixa o arquivo compacto e
    permite acrescentar campos no fim sem quebrar versoes antigas.

        1 byte    -> bool / byte
        2 bytes   -> int16
        4 bytes   -> int32   (0xFFFFFFFF = null)
        8 bytes   -> int64 ou double (IEEE754)
        16 bytes  -> GUID (formato .NET, mixed-endian)
        17 bytes  -> [bool presente][GUID]  (GUID anulavel)
        [int32 len][utf8 ...]              -> string
        [int32 count][string ...]          -> lista de strings
        [int32 count][registro ...]        -> lista de registros filhos
        [int32 count][int16 ...]           -> lista de codigos de tecla (VK)

    O root do arquivo e' um registro na posicao 0 (o perfil).
    root.campo[2] = lista de comandos; cada comando tem campo[2] = lista
    de acoes.
"""
import datetime
import json
import struct
import sys
import uuid
import zlib

U32 = lambda b, p: struct.unpack_from('<I', b, p)[0]
I32 = lambda b, p: struct.unpack_from("<i", b, p)[0]
DT_EPOCH = datetime.datetime(1970, 1, 1)

# --------------------------------------------------------------------------
# camada semantica (inferida por analise estatistica do perfil, nao oficial)
# --------------------------------------------------------------------------
PROFILE_FIELDS = {0: 'id', 1: 'name', 2: 'commands', 48: 'categories'}

COMMAND_FIELDS = {
    0: 'id', 1: 'spoken_phrase', 2: 'actions', 4: 'enabled',
    7: 'category', 17: 'exec_type', 21: 'linked_id', 50: 'group_id',
}

ACTION_FIELDS = {
    0: 'id', 1: 'type', 2: 'duration', 3: 'number', 4: 'keys',
    5: 'param1', 6: 'param2', 7: 'guid_param', 8: 'device',
    10: 'value', 12: 'flag', 13: 'bool_value', 16: 'block_seq',
    17: 'block_level', 18: 'operand_left', 19: 'operator',
    20: 'op_flag', 23: 'cond_type', 30: 'extra_conditions',
}

ACTION_TYPES = {
    0:  'PressKey',            # f04 = teclas, f02 = duracao em segundos
    8:  'KeyDown',
    9:  'KeyUp',
    12: 'Say (TTS)',
    14: 'PlaySound',           # f05 = arquivo/som interno, f10 = volume
    16: 'ExecuteCommandById',  # f05 = GUID do comando alvo
    19: 'BeginCondition (If)',
    20: 'EndCondition (EndIf)',
    21: 'SetTextVariable',     # f05 = variavel, f06 = valor
    23: 'WriteToLog',
    28: 'Comment',             # f05 = texto do comentario
    29: 'Else',
    36: 'SetBooleanVariable',
    63: 'ElseIf',
}

VK = {0x08: 'Backspace', 0x09: 'Tab', 0x0D: 'Enter', 0x10: 'Shift',
      0x11: 'Ctrl', 0x12: 'Alt', 0x1B: 'Esc', 0x20: 'Space',
      0x25: 'Left', 0x26: 'Up', 0x27: 'Right', 0x28: 'Down',
      0xA0: 'LShift', 0xA1: 'RShift', 0xA2: 'LCtrl', 0xA3: 'RCtrl',
      0xA4: 'LAlt', 0xA5: 'RAlt'}
VK.update({k: chr(k) for k in range(0x30, 0x5B)})            # 0-9, A-Z
VK.update({0x70 + i: 'F%d' % (i + 1) for i in range(12)})     # F1-F12


def key_name(code):
    return VK.get(code, 'VK_0x%02X' % code)


# --------------------------------------------------------------------------
# parser
# --------------------------------------------------------------------------
def unwrap(path):
    raw = open(path, 'rb').read()
    if raw.lstrip()[:5] == b'<?xml':
        raise SystemExit('esse .vap ja e XML puro, e so abrir num editor')
    return zlib.decompress(raw, -15)


class Rec:
    def __init__(self, buf, pos):
        self.buf, self.pos = buf, pos
        self.len = U32(buf, pos)
        self.n = U32(buf, pos + 4)
        if self.n > 4096 or pos + self.len > len(buf):
            raise ValueError('registro implausivel')
        self.off = [U32(buf, pos + 8 + 4 * i) for i in range(self.n + 1)]
        if self.off[0] != 12 + 4 * self.n:
            raise ValueError('off[0] nao bate (nao e registro)')
        self.end = pos + self.len

    def slices(self):
        b = [self.pos + o for o in self.off] + [self.end]
        for i in range(self.n + 1):
            yield i, b[i], b[i + 1]


def is_str(b):
    return len(b) >= 4 and U32(b, 0) == len(b) - 4


def try_strlist(buf, s, e):
    count = U32(buf, s)
    if not 0 < count <= 20000:
        return None
    p, out = s + 4, []
    for _ in range(count):
        if p + 4 > e:
            return None
        ln = U32(buf, p)
        if ln == 0xFFFFFFFF:
            out.append(None)
            p += 4
            continue
        if ln > e - p - 4:
            return None
        try:
            out.append(buf[p + 4:p + 4 + ln].decode('utf-8'))
        except UnicodeDecodeError:
            return None
        p += 4 + ln
    return out if p == e else None


def try_reclist(buf, s, e):
    if e - s < 16:
        return None
    count = U32(buf, s)
    if not 0 < count <= 20000:
        return None
    p, out = s + 4, []
    for _ in range(count):
        try:
            r = Rec(buf, p)
        except Exception:
            return None
        if r.end > e:
            return None
        out.append(r)
        p = r.end
    return out if p == e else None


def try_keylist(buf, s, e):
    count = U32(buf, s)
    if count and (e - s - 4) == count * 2 and count < 32:
        return [key_name(struct.unpack_from('<H', buf, s + 4 + 2 * i)[0])
                for i in range(count)]
    return None


def decode(buf, s, e, depth=0):
    n, b = e - s, buf[s:e]
    if n == 0:
        return None
    if n == 1:
        return bool(b[0]) if b[0] < 2 else b[0]
    if n == 2:
        return struct.unpack('<h', b)[0]
    if n == 4:
        v = I32(b, 0)
        return None if v == -1 else v
    # AMBIGUIDADE: um campo de 8/16/17 bytes pode ser numero/GUID *ou* uma
    # string curta ([int32 len][utf8]). Testa string primeiro; so' aceita se
    # o tamanho declarado bater E o conteudo for texto plausivel.
    if n in (8, 12, 16, 17) and is_str(b) and all(32 <= c < 127 for c in b[4:]):
        return b[4:].decode('utf-8')
    if n == 8:
        i = struct.unpack('<q', b)[0]
        f = struct.unpack('<d', b)[0]
        return round(f, 6) if (f == 0 or 1e-6 < abs(f) < 1e9) else i
    if n == 12:
        # [int64 segundos desde a epoch unix][int32 kind] -> DateTime .NET
        sec, kind = struct.unpack('<qi', b)
        try:
            dt = DT_EPOCH + datetime.timedelta(seconds=sec)
            return dt.isoformat() if kind == 0 else [dt.isoformat(), kind]
        except OverflowError:
            return {'_raw': b.hex(), '_len': n}
    if n == 16:
        return str(uuid.UUID(bytes_le=b))
    if n == 17:
        return str(uuid.UUID(bytes_le=b[1:])) if b[0] else None
    if is_str(b):
        return b[4:].decode('utf-8', 'replace')
    if depth < 24:
        for fn in (try_keylist, try_strlist):
            r = fn(buf, s, e)
            if r is not None:
                return r
        rl = try_reclist(buf, s, e)
        if rl is not None:
            return [walk(buf, r, depth + 1) for r in rl]
        for pad in (0, 4):
            try:
                r = Rec(buf, s + pad)
                if r.end == e:
                    return walk(buf, r, depth + 1)
            except Exception:
                pass
    # fallback: structs planos sem tabela de offsets (ex.: condicoes extras
    # de um If). Nao tem framing proprio; ao menos extrai os operandos.
    out = {'_raw': b[:96].hex(), '_len': n}
    strs, p = [], 0
    while p + 4 <= n:
        ln = U32(b, p)
        if 0 < ln <= n - p - 4 and all(32 <= c < 127 for c in b[p + 4:p + 4 + ln]):
            strs.append(b[p + 4:p + 4 + ln].decode())
            p += 4 + ln
        else:
            p += 1
    if strs:
        out['_strings'] = strs
    return out


def walk(buf, rec, depth=0):
    return {'f%02d' % i: decode(buf, s, e, depth) for i, s, e in rec.slices()}


def name_fields(obj, mapping):
    out = {}
    for k, v in obj.items():
        out[mapping.get(int(k[1:]), k)] = v
    return out


# --------------------------------------------------------------------------
# saida legivel
# --------------------------------------------------------------------------
def render(tree, fh):
    p = name_fields(tree, PROFILE_FIELDS)
    w = fh.write
    w('PERFIL: %s\n' % p['name'])
    w('ID:     %s\n' % p['id'])
    cats = p.get('categories') or []
    w('CATEGORIAS (%d): %s\n' % (len(cats), ', '.join(cats)))
    cmds = [name_fields(c, COMMAND_FIELDS) for c in (p['commands'] or [])]
    by_id = {c['id']: c['spoken_phrase'] for c in cmds}
    w('COMANDOS: %d\n' % len(cmds))

    for c in cmds:
        w('\n' + '=' * 78 + '\n')
        w('%s\n' % c['spoken_phrase'])
        w('  categoria: %s | id: %s | ativo: %s\n'
          % (c.get('category'), c['id'], c.get('enabled')))
        indent = 1
        for a in (c['actions'] or []):
            a = name_fields(a, ACTION_FIELDS)
            t = a['type']
            tn = ACTION_TYPES.get(t, 'Tipo%d' % t)
            if t in (20, 29, 63):
                indent = max(1, indent - 1)
            pad = '  ' * indent
            if t == 0:
                det = '%s  (%ss)' % ('+'.join(a.get('keys') or []),
                                     a.get('duration') or 0)
            elif t in (8, 9):
                det = '+'.join(a.get('keys') or [])
            elif t == 16:
                tgt = a.get('param1')
                det = '%s   -> "%s"' % (tgt, by_id.get(tgt, '?'))
            elif t == 28:
                det = repr(a.get('param1'))
            elif t in (19, 63):
                det = '%s  op=%s  %r   [bloco %s/%s]' % (
                    a.get('operand_left'), a.get('operator'), a.get('param2'),
                    a.get('block_seq'), a.get('block_level'))
            elif t == 21:
                det = '%s = %r' % (a.get('param1'), a.get('param2'))
            elif t == 36:
                det = '%s = %s' % (a.get('param1'), bool(a.get('bool_value')))
            elif t == 14:
                det = '%s  vol=%s' % (a.get('param1'), a.get('value'))
            else:
                det = '; '.join('%s=%r' % (k, v) for k, v in a.items()
                                if k not in ('id', 'type') and v not in
                                (0, None, False) and not isinstance(v, dict))
            w('%s[%-22s] %s\n' % (pad, tn, det))
            if t in (19, 29, 63):
                indent += 1


def main(src, base):
    buf = unwrap(src)
    root = Rec(buf, 0)
    tree = walk(buf, root)
    with open(base + '.json', 'w') as f:
        json.dump(tree, f, indent=1, ensure_ascii=False)
    with open(base + '.txt', 'w') as f:
        render(tree, f)
    print('descomprimido: %d bytes | comandos: %d'
          % (len(buf), len(tree['f02'])))
    print('->', base + '.json,', base + '.txt')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else 'perfil')
