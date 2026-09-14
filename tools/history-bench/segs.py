import json, re, sys, os
from collections import Counter
GUID = re.compile(r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')
NUM  = re.compile(r'^\d+$'); HEX = re.compile(r'^[0-9a-fA-F]{16,}$')
ULID = re.compile(r'^[0-9A-HJKMNP-TV-Z]{26}$'); ISO = re.compile(r'^\d{4}-\d{2}-\d{2}([T_].*)?$')
B64  = re.compile(r'^[A-Za-z0-9_\-]{22,}={0,2}$')
def cls(s):
    if GUID.match(s): return 'guid'
    if NUM.match(s): return 'numeric'
    if ULID.match(s): return 'ulid'
    if ISO.match(s): return 'iso'
    if HEX.match(s): return 'longhex'
    if B64.match(s): return 'base64ish'
    return 'literal'
ex = {}
uris = set()
for path in sys.argv[1:]:
    doc = json.load(open(path, encoding='utf-8-sig'))
    for f in doc.get('features', []):
        for s in f.get('scenarios', []):
            for i in s.get('httpInteractions', []) or []:
                u = i.get('uri') or ''
                if not u: continue
                uris.add(u)
                m = re.match(r'^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/]+(/.*)?$', u)
                tail = (m.group(1) or '/') if m else u
                for seg in [x for x in tail.partition('?')[0].split('/') if x]:
                    c = cls(seg)
                    if c != 'literal':
                        ex.setdefault(c, Counter())[seg] += 1
for c, ctr in ex.items():
    print('--- %s (%d distinct)' % (c, len(ctr)))
    for seg, n in ctr.most_common(6):
        print('    %4d  %s' % (n, seg[:90]))
print('--- sample full URIs')
for u in sorted(uris)[:8]:
    print('    ' + u[:130])
