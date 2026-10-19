# What the $z payloads cost under `zstd -3` of the JSON as written (the zstd command's default), against what they
# cost inside a .krkl (zstd 9 over the decoded, compact, no-nulls content), and what everything else costs at each.
# Usage: python -E payload_split.py <TestRunReport.json>. Prints sizes only.
import sys, json, gzip, base64
from compression import zstd

raw = open(sys.argv[1], 'rb').read()
doc = json.loads(raw)
stored = [0]
count = [0]

def walk(o, mode):
    if isinstance(o, dict):
        if '$z' in o:
            if mode == 'kept':
                stored[0] += len(o['$z'])
                count[0] += 1
                return o
            if mode == 'stripped':
                return ''
            return gzip.decompress(base64.b64decode(o['$z'])).decode('utf-8')
        return {k: walk(v, mode) for k, v in o.items() if v is not None and v != []}
    if isinstance(o, list):
        return [walk(x, mode) for x in o]
    return o

def dump(o):
    return json.dumps(o, ensure_ascii=False, separators=(',', ':')).encode()

forms = {m: dump(walk(doc, m)) for m in ('kept', 'decoded', 'stripped')}
z = lambda b, level: len(zstd.compress(b, level))
asis3 = z(raw, 3)
print(f'file {len(raw):,}  $z payloads {count[0]:,} holding {stored[0]:,} base64 chars')
print(f'zstd -3 of the file as written {asis3:,}')
for m, b in forms.items():
    print(f'  {m:9} raw {len(b):>11,}  zstd3 {z(b, 3):>9,}  zstd9 {z(b, 9):>9,}')
k3, s3, d9, s9 = z(forms['kept'], 3), z(forms['stripped'], 3), z(forms['decoded'], 9), z(forms['stripped'], 9)
print(f'payloads under zstd -3, kept as $z: {k3 - s3:,}; inside a .krkl (decoded, zstd 9): {d9 - s9:,} ({(d9 - s9) / (k3 - s3):.0%})')
print(f'the rest under zstd -3: {s3:,}; at zstd 9: {s9:,} ({s9 / s3:.0%})')
print(f'.krkl content at zstd 9 against zstd -3 of the file: {d9:,} / {asis3:,} = {d9 / asis3:.0%}')
