"""Compression matrix: zstd (levels, window, long-distance matching), gzip, brotli, xz. Prints size and time."""
import os
import subprocess
import sys
import time
from compression import zstd

P = zstd.CompressionParameter

def zstd_size(data, level, wlog=None, ldm=False):
    opts = {P.compression_level: level, P.checksum_flag: 1}
    if wlog:
        opts[P.window_log] = wlog
    if ldm:
        opts[P.enable_long_distance_matching] = 1
    t = time.perf_counter()
    out = zstd.compress(data, options=opts)
    ct = time.perf_counter() - t
    t = time.perf_counter()
    back = zstd.decompress(out)
    dt = time.perf_counter() - t
    assert back == data
    return len(out), ct, dt

def cli_size(cmd, data):
    t = time.perf_counter()
    r = subprocess.run(cmd, input=data, stdout=subprocess.PIPE, check=True)
    return len(r.stdout), time.perf_counter() - t

files = sys.argv[1:]
for f in files:
    data = open(f, 'rb').read()
    name = os.path.basename(f)
    print(f"\n### {name}: {len(data):,} bytes", flush=True)
    rows = []
    for level, wlog, ldm in ((3, None, False), (3, 27, True), (9, None, False), (9, 27, True), (12, 27, True),
                             (15, 27, True), (19, None, False), (19, 27, True), (22, 27, True)):
        if level >= 19 and len(data) > 60_000_000 and os.environ.get('SKIP_SLOW'):
            continue
        size, ct, dt = zstd_size(data, level, wlog, ldm)
        label = f"zstd -{level}" + (f" w{wlog}" if wlog else "") + (" ldm" if ldm else "")
        print(f"{label:<22}{size:>14,}  {size / len(data) * 100:6.2f}%  compress {ct:7.2f}s  decompress {dt:6.3f}s", flush=True)
    for label, cmd in (("gzip -9", ["gzip", "-9", "-c"]),
                       ("brotli -q 11 -w 24", ["brotli", "-q", "11", "-w", "24", "-c"]),
                       ("xz -9e", ["xz", "-9e", "-c", "-T1"])):
        size, ct = cli_size(cmd, data)
        print(f"{label:<22}{size:>14,}  {size / len(data) * 100:6.2f}%  compress {ct:7.2f}s", flush=True)
