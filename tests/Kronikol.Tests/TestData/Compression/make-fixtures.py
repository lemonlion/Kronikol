"""Makes the compressed fixtures of the reader's cross-implementation facts (plans/REPORT_FORMAT_PLAN.md section 5, R3
item 10) from Reports/failing-with-steps.mergeable.json, each by an implementation other than Kronikol's own:

- failing-with-steps.json.zst: a bare zstd frame from libzstd (Python 3.14's compression.zstd), level 19, as the zstd
  tool writes one;
- failing-with-steps.krkl: Kronikol's container built from the plan's layout (section 4.1), the 24-byte skippable frame
  and then a libzstd frame at level 9 with its checksum;
- failing-with-steps.json.gz: gzip from Java's GZIPOutputStream, the class Kronikol4J writes with (Q12), through
  GzipFixture.java.

Run from anywhere with Python 3.14 or later and a JDK on PATH:
    python tests/Kronikol.Tests/TestData/Compression/make-fixtures.py
"""
import os
import struct
import subprocess
from compression import zstd

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, '..', 'Reports', 'failing-with-steps.mergeable.json')

data = open(SOURCE, 'rb').read()

level19 = {zstd.CompressionParameter.compression_level: 19, zstd.CompressionParameter.checksum_flag: 1}
with open(os.path.join(HERE, 'failing-with-steps.json.zst'), 'wb') as out:
    out.write(zstd.compress(data, options=level19))

level9 = {zstd.CompressionParameter.compression_level: 9, zstd.CompressionParameter.checksum_flag: 1}
marker = struct.pack('<II', 0x184D2A50, 16) + b'KRKL' + bytes([1, 0, 0, 0]) + struct.pack('<Q', len(data))
with open(os.path.join(HERE, 'failing-with-steps.krkl'), 'wb') as out:
    out.write(marker + zstd.compress(data, options=level9))

subprocess.run(['java', os.path.join(HERE, 'GzipFixture.java'), SOURCE,
                os.path.join(HERE, 'failing-with-steps.json.gz')], check=True)
print(f'made 3 fixtures from {len(data):,} bytes')
