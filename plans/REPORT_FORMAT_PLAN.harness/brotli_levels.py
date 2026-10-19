# Sizes and timings only: brotli quality and window on the content a .krkl would hold.
import sys, time, brotli
b = open(sys.argv[1], 'rb').read()
print(f'raw {len(b):,}')
for q in (4, 5, 6, 7):
    for w in (22, 24):
        t = time.perf_counter(); c = brotli.compress(b, quality=q, lgwin=w); tc = time.perf_counter() - t
        t = time.perf_counter(); brotli.decompress(c); td = time.perf_counter() - t
        print(f'brotli q{q} w{w}: {len(c):>9,}  write {tc*1000:5.0f} ms  read {td*1000:4.1f} ms')
