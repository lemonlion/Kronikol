# Sizes only: one gzip stream vs one gzip member per scenario (a valid multi-member gzip of the same bytes).
import sys, json, gzip, base64
def inflate(z): return gzip.decompress(base64.b64decode(z)).decode('utf-8')
def unwrap(o):
    if isinstance(o, dict):
        if '$z' in o: return inflate(o['$z'])
        return {k: unwrap(v) for k, v in o.items() if v is not None and v != []}
    if isinstance(o, list): return [unwrap(x) for x in o]
    return o
c = lambda o: json.dumps(o, ensure_ascii=False, separators=(',', ':')).encode()
for p in sys.argv[1:]:
    d = unwrap(json.load(open(p, encoding='utf-8')))
    whole = c(d)
    pieces, n = [], 0
    for f in d['features']:
        for s in f.get('scenarios', []):
            pieces.append(c(s)); n += 1
    rest = len(whole) - sum(len(x) for x in pieces)
    members = sum(len(gzip.compress(x, 6)) for x in pieces) + len(gzip.compress(b'x' * 0, 6)) + rest // 8
    single = len(gzip.compress(whole, 6))
    assert gzip.decompress(gzip.compress(b'{"a":') + gzip.compress(b'1}')) == b'{"a":1}'
    print(f'{p[-40:]}: scenarios {n}, raw {len(whole):,}, one stream {single:,}, member per scenario ~{members:,} (+{members/single-1:.1%})')
