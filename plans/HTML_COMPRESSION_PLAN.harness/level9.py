"""Level 9 only: window variants within the 8 MB cap, and the cost of chunking the diagram sources."""
import sys
import time
from compression import zstd

P = zstd.CompressionParameter
split = sys.argv[1]

def z(data, wlog=None, ldm=False, level=9):
    opts = {P.compression_level: level, P.checksum_flag: 1}
    if wlog: opts[P.window_log] = wlog
    if ldm: opts[P.enable_long_distance_matching] = 1
    t = time.perf_counter()
    out = zstd.compress(data, options=opts)
    return len(out), time.perf_counter() - t

classes = ['markup.html', 'puml.txt', 'steps.txt', 'iflow.txt', 'flame.txt', 'search.bin']
data = {c: open(f"{split}/{c}", 'rb').read() for c in classes}
print(f"{'variant':<24}" + ''.join(f"{c.split('.')[0]:>12}" for c in classes) + f"{'sum':>12}{'secs':>8}")
for label, wlog, ldm in (('-9 (w22 default)', None, False), ('-9 w23', 23, False), ('-9 w23 ldm', 23, True),
                         ('-9 w27 ldm', 27, True)):
    sizes, secs = [], 0.0
    for c in classes:
        s, t = z(data[c], wlog, ldm); sizes.append(s); secs += t
    print(f"{label:<24}" + ''.join(f"{s:>12,}" for s in sizes) + f"{sum(sizes):>12,}{secs:>8.2f}")

# Everything in ONE frame (page first, then diagrams), versus separate frames
whole = b''.join(data[c] for c in classes)
for label, wlog, ldm in (('one frame -9 w23', 23, False), ('one frame -9 w23 ldm', 23, True)):
    s, t = z(whole, wlog, ldm)
    print(f"{label:<24}{s:>12,}  {t:.2f}s")

# Chunked diagram sources: independent frames of N MB, cut at diagram boundaries
SEP = b'\n\x1e\n'
items = data['puml.txt'].split(SEP)
for mb in (1, 2, 4, 8, 16):
    chunks, cur, curlen = [], [], 0
    for it in items:
        cur.append(it); curlen += len(it)
        if curlen >= mb * 1_000_000:
            chunks.append(SEP.join(cur)); cur, curlen = [], 0
    if cur: chunks.append(SEP.join(cur))
    total = sum(z(c, 23)[0] for c in chunks)
    print(f"diagram chunks of {mb:>2} MB: {len(chunks):>4} frames, {total:>12,} bytes")

# Per-diagram with a trained dictionary (random access per item)
for dsize in (112_640, 1_000_000):
    t = time.perf_counter()
    d = zstd.train_dict(items[::3], dsize)
    tt = time.perf_counter() - t
    total = sum(len(zstd.compress(it, options={P.compression_level: 9}, zstd_dict=d)) for it in items)
    print(f"per diagram + {dsize:>9,} B trained dict: {total + len(d.dict_content):>12,} bytes (dict trained in {tt:.1f}s)")
