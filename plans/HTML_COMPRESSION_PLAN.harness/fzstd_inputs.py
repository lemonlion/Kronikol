"""Inputs for fzcheck.js and fzstream.js, which test fzstd's documented 2^25 back-reference limit.

Writes three zstd frames of one input into the output folder: w23.zst (level 9, window 2^23, the plan's setting),
w27ldm.zst (level 9, window 2^27, long-distance matching) and l22.zst (level 22, whose default window is 2^27), with
expected.sha, the input's SHA-256.

The input is the given file (the split's puml.txt, for real diagram sources), or with no file 40 MiB of random bytes
followed by a copy of their first 4 MiB: that tail compresses only as a match 40 MiB back, beyond 2^25, so a frame
whose size falls by about 4 MB used such a match.

Usage: py -I fzstd_inputs.py <outdir> [file]
"""
import hashlib
import os
import sys
from compression import zstd

P = zstd.CompressionParameter
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
if len(sys.argv) > 2:
    data = open(sys.argv[2], 'rb').read()
else:
    head = os.urandom(40 * 1024 * 1024)
    data = head + head[:4 * 1024 * 1024]
open(os.path.join(out, 'expected.sha'), 'w').write(hashlib.sha256(data).hexdigest())
print('input', len(data))
for name, opts in (('w23', {P.compression_level: 9, P.window_log: 23, P.checksum_flag: 1}),
                   ('w27ldm', {P.compression_level: 9, P.window_log: 27, P.enable_long_distance_matching: 1,
                               P.checksum_flag: 1}),
                   ('l22', {P.compression_level: 22, P.checksum_flag: 1})):
    z = zstd.compress(data, options=opts)
    open(os.path.join(out, name + '.zst'), 'wb').write(z)
    print(f"{name:<7} {len(z):>12,}")
