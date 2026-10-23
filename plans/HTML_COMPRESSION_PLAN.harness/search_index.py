"""The deep-search index inside a zstd stream: as the page stores it (a JSON string of base64 gzip) against the raw
binary index (split.py's search.bin), each at level 9 with a 2^23 window and the checksum, as level9.py writes.
Usage: py -I search_index.py <TestRunReport.html> <search.bin>
"""
import re
import sys
from compression import zstd

P = zstd.CompressionParameter
opts = {P.compression_level: 9, P.window_log: 23, P.checksum_flag: 1}
page = open(sys.argv[1], 'rb').read().decode('utf-8')
m = re.search(r'<script id="kron-search-index" type="application/json">(.*?)</script>', page, re.S)
stored = m.group(1).encode()
raw = open(sys.argv[2], 'rb').read()
print('stored as in the page:', len(stored), '-> zstd -9 w23', len(zstd.compress(stored, options=opts)))
print('raw binary index:     ', len(raw), '-> zstd -9 w23', len(zstd.compress(raw, options=opts)))
