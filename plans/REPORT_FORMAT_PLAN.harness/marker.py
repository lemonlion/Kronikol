# Usage: python -E marker.py <content file> <out .krkl>. Does a zstd skippable frame in front of the data frame decode transparently? Sizes and booleans only.
import struct, sys
from compression import zstd
data = open(sys.argv[1], 'rb').read()
marker = struct.pack('<II', 0x184D2A50, 16) + b'KRKL' + bytes([1, 0, 0, 0]) + struct.pack('<Q', len(data))
opts = {zstd.CompressionParameter.compression_level: 9, zstd.CompressionParameter.checksum_flag: 1}
frame = zstd.compress(data, options=opts)
blob = marker + frame
open(sys.argv[2], 'wb').write(blob)
print('marker bytes', len(marker), 'frame', len(frame), 'first bytes', blob[:4].hex(' '))
print('python one-shot decompress equals:', zstd.decompress(blob) == data)
d = zstd.ZstdDecompressor(); out = d.decompress(blob)
print('python ZstdDecompressor equals:', out == data, 'eof', d.eof)
print('frame info of data frame: content size', zstd.get_frame_info(frame).decompressed_size)
broken = bytearray(blob); broken[len(blob)//2] ^= 0xFF
try:
    zstd.decompress(bytes(broken)); print('corrupted: NOT detected')
except zstd.ZstdError as e:
    print('corrupted: detected', type(e).__name__)
try:
    zstd.decompress(blob[:-1000]); print('truncated: NOT detected')
except zstd.ZstdError as e:
    print('truncated: detected', type(e).__name__)
