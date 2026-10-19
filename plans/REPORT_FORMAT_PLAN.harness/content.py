# Writes the content a .krkl would hold (every $z decoded, compact, no null or empty keys) for codecs.cs.
# Usage: python -E content.py <TestRunReport.json> <out file>. Prints the byte count only.
import sys, json, gzip, base64
def inflate(z): return gzip.decompress(base64.b64decode(z)).decode('utf-8')
def unwrap(o):
    if isinstance(o, dict):
        if '$z' in o: return inflate(o['$z'])
        return {k: unwrap(v) for k, v in o.items() if v is not None and v != []}
    if isinstance(o, list): return [unwrap(x) for x in o]
    return o
b = json.dumps(unwrap(json.load(open(sys.argv[1], encoding='utf-8'))), ensure_ascii=False, separators=(',', ':')).encode()
open(sys.argv[2], 'wb').write(b)
print(len(b))
