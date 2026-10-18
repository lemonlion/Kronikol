# Sizes and timings only, on the 150 MB synthetic query-bench report converted to .krkl content.
import sys, json, gzip, time, base64
from compression import zstd
def inflate(z): return gzip.decompress(base64.b64decode(z)).decode('utf-8')
def unwrap(o):
    if isinstance(o, dict):
        if '$z' in o: return inflate(o['$z'])
        return {k: unwrap(v) for k, v in o.items() if v is not None and v != []}
    if isinstance(o, list): return [unwrap(x) for x in o]
    return o
raw = open(sys.argv[1], 'rb').read()
t = time.perf_counter(); d = json.loads(raw); del raw
b = json.dumps(unwrap(d), ensure_ascii=False, separators=(',', ':')).encode(); del d
open(sys.argv[2], 'wb').write(b)
print(f'content {len(b):,} bytes (converted in {time.perf_counter()-t:.1f} s)')
def run(name, comp, dec):
    t = time.perf_counter(); c = comp(); tc = time.perf_counter() - t
    t = time.perf_counter(); out = dec(c); td = time.perf_counter() - t
    assert len(out) == len(b)
    print(f'{name:22} {len(c):>12,} ({len(c)/len(b):6.2%})  write {tc:6.2f} s  read {td:5.2f} s')
run('gzip 6', lambda: gzip.compress(b, 6), gzip.decompress)
run('zstd 3', lambda: zstd.compress(b, 3), zstd.decompress)
run('zstd 9', lambda: zstd.compress(b, 9), zstd.decompress)
P = zstd.CompressionParameter
run('zstd 9 + long 27', lambda: zstd.compress(b, options={P.compression_level: 9, P.enable_long_distance_matching: 1, P.window_log: 27}), zstd.decompress)
