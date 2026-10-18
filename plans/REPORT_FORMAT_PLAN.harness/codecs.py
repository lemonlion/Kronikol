# Sizes and timings only. Content: the report inflated, compact, nulls/empties dropped (what a .krkl would hold).
import sys, json, gzip, base64, time, lzma
from compression import zstd
import brotli
def inflate(z): return gzip.decompress(base64.b64decode(z)).decode('utf-8')
def unwrap(o):
    if isinstance(o, dict):
        if '$z' in o: return inflate(o['$z'])
        return {k: unwrap(v) for k, v in o.items() if v is not None and v != []}
    if isinstance(o, list): return [unwrap(x) for x in o]
    return o
def best(f, n=3):
    r = None
    for _ in range(n):
        t = time.perf_counter(); out = f(); dt = time.perf_counter() - t
        r = dt if r is None or dt < r else r
    return out, r
b = json.dumps(unwrap(json.load(open(sys.argv[1], encoding='utf-8'))), ensure_ascii=False, separators=(',', ':')).encode()
print(f'raw {len(b):,} bytes')
cases = [('gzip 6', lambda: gzip.compress(b, 6), gzip.decompress), ('gzip 9', lambda: gzip.compress(b, 9), gzip.decompress)]
for lv in (1, 3, 9, 12, 15, 19):
    cases.append((f'zstd {lv}', lambda lv=lv: zstd.compress(b, lv), zstd.decompress))
for q in (5, 9, 11):
    cases.append((f'brotli {q}', lambda q=q: brotli.compress(b, quality=q, lgwin=24), brotli.decompress))
cases.append(('xz 6', lambda: lzma.compress(b, preset=6), lzma.decompress))
for name, comp, dec in cases:
    c, tc = best(comp, 1 if name in ('brotli 11', 'zstd 19') else 3)
    _, td = best(lambda: dec(c))
    print(f'{name:10} {len(c):>9,} ({len(c)/len(b):5.1%})  write {tc*1000:7.0f} ms ({len(b)/tc/1e6:6.1f} MB/s)  read {td*1000:5.1f} ms')
