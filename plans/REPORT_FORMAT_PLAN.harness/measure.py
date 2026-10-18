# Sizes only: never prints report content.
import sys, json, gzip, zlib, base64, re, time, lzma
from compression import zstd
import brotli

def inflate(z):
    return gzip.decompress(base64.b64decode(z)).decode('utf-8')

def is_wrap(v):
    return isinstance(v, dict) and '$z' in v

def unwrap_all(o):
    if isinstance(o, dict):
        if is_wrap(o):
            return inflate(o['$z'])
        return {k: unwrap_all(v) for k, v in o.items()}
    if isinstance(o, list):
        return [unwrap_all(x) for x in o]
    return o

def drop_empty(o):
    if isinstance(o, dict):
        return {k: drop_empty(v) for k, v in o.items() if v is not None and v != [] and v != {}}
    if isinstance(o, list):
        return [drop_empty(x) for x in o]
    return o

def compact(o):
    return json.dumps(o, ensure_ascii=False, separators=(',', ':')).encode('utf-8')

def indented(o):
    return json.dumps(o, ensure_ascii=False, indent=2).encode('utf-8')

cat = {}
def add(k, n):
    cat[k] = cat.get(k, 0) + n

depth_hist = {}
def walk_steps(steps, depth):
    for s in steps:
        depth_hist[depth] = depth_hist.get(depth, 0) + 1
        subs = s.get('subSteps') or []
        rest = {k: v for k, v in s.items() if k != 'subSteps'}
        add('steps', len(compact(rest)))
        add('steps.nullOrEmptyKeys', sum(len(compact({k: v})) - 1 for k, v in s.items() if v is None or v == [] ))
        walk_steps(subs, depth + 1)

def measure(path):
    global cat, depth_hist
    cat, depth_hist = {}, {}
    raw = open(path, 'rb').read()
    d = json.loads(raw)
    outside = re.sub(rb'"(?:[^"\\]|\\.)*"', b'""', raw)
    ws = sum(outside.count(c) for c in (b' ', b'\n', b'\r', b'\t'))
    for f in d.get('features', []):
        for s in f.get('scenarios', []):
            walk_steps((s.get('backgroundSteps') or []) + (s.get('steps') or []), 0)
            for dg in s.get('diagrams') or []:
                add('diagrams.stored', len(compact(dg)))
                add('diagrams.decoded', len((inflate(dg['$z']) if is_wrap(dg) else dg).encode()))
            for it in s.get('httpInteractions') or []:
                c = it.get('content')
                if c is not None:
                    add('content.stored', len(compact(c)))
                    add('content.decoded', len((inflate(c['$z']) if is_wrap(c) else c).encode()))
                add('interactions.meta', len(compact({k: v for k, v in it.items() if k != 'content'})))
    full = unwrap_all(d)
    variants = {
        'asis file': raw,
        'compact (wrappers kept)': compact(d),
        'compact, nulls/empties dropped': compact(drop_empty(d)),
        'inflated, indented': indented(full),
        'inflated, compact': compact(full),
        'inflated, compact, no nulls': compact(drop_empty(full)),
    }
    print(f'== {path[-90:]}')
    print(f'file {len(raw):,}  formatVersion {d.get("formatVersion")}  ws-outside-strings {ws:,} ({ws/len(raw):.1%})')
    for k in sorted(cat):
        print(f'  {k:32} {cat[k]:>12,}')
    print('  step depth histogram', dict(sorted(depth_hist.items())))
    for name, b in variants.items():
        t = time.perf_counter(); g6 = gzip.compress(b, 6); tg = time.perf_counter() - t
        t = time.perf_counter(); gzip.decompress(g6); tgd = time.perf_counter() - t
        z3 = zstd.compress(b, 3); z19 = zstd.compress(b, 19)
        br = brotli.compress(b, quality=9, lgwin=24)
        xz = lzma.compress(b, preset=6)
        print(f'  {name:32} raw {len(b):>11,} gz6 {len(g6):>10,} (inflate {tgd*1000:.0f} ms) zstd3 {len(z3):>10,} zstd19 {len(z19):>10,} br9 {len(br):>10,} xz6 {len(xz):>10,}')

for p in sys.argv[1:]:
    measure(p)
