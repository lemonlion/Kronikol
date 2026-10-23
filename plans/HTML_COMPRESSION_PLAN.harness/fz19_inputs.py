"""Inputs for fzstd issue #19: "hello world " repeated N times, compressed as the zstd CLI does by default (level 3,
content size and checksum on), plus the same at level 9 with a 2^23 window. Usage: py -I fz19_inputs.py <outdir>"""
import os, sys
from compression import zstd
P = zstd.CompressionParameter
out = sys.argv[1]
for n in (10000, 11000, 12000, 50000, 1000000):
    raw = b'hello world ' * n
    open(os.path.join(out, f'h{n}.raw'), 'wb').write(raw)
    for name, opts in (('l3', {P.compression_level: 3, P.checksum_flag: 1}),
                       ('l9w23', {P.compression_level: 9, P.window_log: 23, P.checksum_flag: 1})):
        open(os.path.join(out, f'h{n}.{name}.zst'), 'wb').write(zstd.compress(raw, options=opts))
print('ok')
