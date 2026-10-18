# Sizes only. (1) #120 inside one compressed stream: body notes in diagrams replaced by a short reference.
# (2) The per-payload $z threshold inside the container: decoded working set vs compressed size.
import sys, json, gzip, base64, re
from compression import zstd
import brotli
def inflate(z): return gzip.decompress(base64.b64decode(z)).decode('utf-8')
def walk(o, f):
    if isinstance(o, dict):
        if '$z' in o: return f(inflate(o['$z']))
        return {k: walk(v, f) for k, v in o.items() if v is not None and v != []}
    if isinstance(o, list): return [walk(x, f) for x in o]
    return f(o) if isinstance(o, str) else o
NOTE = re.compile(r'(?ms)^([ \t]*h?note\b[^\n]*\n)(.*?)(^[ \t]*end ?h?note\b)')
stats = {'notes': 0, 'replaced_chars': 0}
def strip_notes(d):
    for f in d['features']:
        for s in f.get('scenarios', []):
            ds = s.get('diagrams')
            if not ds: continue
            out = []
            for dg in ds:
                def rep(m):
                    body = m.group(2)
                    if len(body) < 200: return m.group(0)
                    stats['notes'] += 1; stats['replaced_chars'] += len(body)
                    return m.group(1) + 'ref b:00000000\n' + m.group(3)
                out.append(NOTE.sub(rep, dg))
            s['diagrams'] = out
    return d
c = lambda o: json.dumps(o, ensure_ascii=False, separators=(',', ':')).encode()
def rewrap(o, threshold):
    if isinstance(o, dict): return {k: rewrap(v, threshold) for k, v in o.items()}
    if isinstance(o, list): return [rewrap(x, threshold) for x in o]
    if isinstance(o, str) and len(o) >= threshold:
        return {'$h': 'b:00000000', '$n': len(o), '$z': base64.b64encode(gzip.compress(o.encode(), 6)).decode()}
    return o
for p in sys.argv[1:]:
    stats.update(notes=0, replaced_chars=0)
    d = walk(json.load(open(p, encoding='utf-8')), lambda x: x)
    base = c(d)
    nd = strip_notes(json.loads(base))
    nb = c(nd)
    print(f'== {p[-34:]}  content {len(base):,}')
    print(f'  #120: {stats["notes"]} body notes ({stats["replaced_chars"]:,} chars) -> content {len(nb):,}')
    for name, fn in (('gzip 6', lambda b: gzip.compress(b, 6)), ('zstd 9', lambda b: zstd.compress(b, 9)), ('zstd 19', lambda b: zstd.compress(b, 19)), ('brotli 5', lambda b: brotli.compress(b, quality=5, lgwin=24))):
        a, b2 = len(fn(base)), len(fn(nb))
        print(f'  {name:8} as is {a:>9,}  notes as refs {b2:>9,}  ({b2/a-1:+.1%})')
    for t in (8192, 65536, 262144):
        w = c(rewrap(json.loads(base), t))
        print(f'  $z at {t:>7,}: decoded working set {len(w):>10,}  zstd 9 {len(zstd.compress(w, 9)):>9,}  brotli 5 {len(brotli.compress(w, quality=5, lgwin=24)):>9,}')
    print(f'  $z off      : decoded working set {len(base):>10,}  zstd 9 {len(zstd.compress(base, 9)):>9,}  brotli 5 {len(brotli.compress(base, quality=5, lgwin=24)):>9,}')
