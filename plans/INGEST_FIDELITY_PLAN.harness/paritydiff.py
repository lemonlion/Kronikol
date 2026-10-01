import sys, os, re, gzip, base64, json, difflib

GZ = re.compile(r'H4sI[A-Za-z0-9+/=]{8,}')
HEX32 = re.compile(r'\b[0-9a-f]{32}\b')
HEX16 = re.compile(r'\b[0-9a-f]{16}\b')
GUID = re.compile(r'\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b')
ISO = re.compile(r'\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|[+-]\d\d:\d\d)?')
HMS = re.compile(r'\b\d\d:\d\d:\d\d(?:\.\d+)?\b')
COMPACT = re.compile(r'\b\d{8}T\d{6}Z\b')
EPOCH = re.compile(r'\b1\d{12}(?:\.\d+)?\b')
DATE = re.compile(r'\b\d{4}-\d\d-\d\d\b|\b\d{1,2} [A-Z][a-z]+ \d{4}\b')

def inflate(text, depth=0):
    def rep(m):
        try:
            raw = gzip.decompress(base64.b64decode(m.group(0)))
            inner = raw.decode('utf-8')
            return '<<GZ:' + inflate(inner, depth + 1) + ':GZ>>'
        except Exception:
            return m.group(0)
    return GZ.sub(rep, text) if depth < 4 else text

def normalise(text):
    maps = {}
    def by_order(kind):
        table = maps.setdefault(kind, {})
        def rep(m):
            v = m.group(0)
            if v not in table:
                table[v] = f'<{kind}{len(table)}>'
            return table[v]
        return rep
    text = GUID.sub(by_order('G'), text)
    text = HEX32.sub(by_order('T'), text)
    text = HEX16.sub(by_order('S'), text)
    text = ISO.sub(by_order('ISO'), text)
    text = COMPACT.sub(by_order('C'), text)
    text = HMS.sub(by_order('HMS'), text)
    text = EPOCH.sub(by_order('EP'), text)
    text = DATE.sub(by_order('D'), text)
    return text

def load(path):
    with open(path, encoding='utf-8', errors='replace') as f:
        t = f.read()
    t = t.replace('\\u002B', '+').replace('\\u002b', '+').replace('\\/', '/')
    t = normalise(inflate(t))
    # The scenario search index is binary and holds run-dependent ids; it is not internal flow.
    t = re.sub(r'<<GZ:KSI1.*?:GZ>>', '<<KSI1>>', t, flags=re.S)
    return t

a, b = sys.argv[1], sys.argv[2]
skip = {'TestRunReport.schema.json', 'query.cs', 'AGENTS.md', 'CLAUDE.md'}
total = 0
for root, _, files in os.walk(a):
    for name in sorted(files):
        pa = os.path.join(root, name)
        rel = os.path.relpath(pa, a)
        pb = os.path.join(b, rel)
        if not os.path.exists(pb):
            print('MISSING in b:', rel); total += 1; continue
        ta, tb = load(pa), load(pb)
        if ta == tb:
            print('same  ', rel, len(ta)); continue
        total += 1
        # Split at punctuation so the diff is local.
        sa = re.split(r'(?<=[,;>{}\]\n])', ta)
        sb = re.split(r'(?<=[,;>{}\]\n])', tb)
        d = [l for l in difflib.unified_diff(sa, sb, lineterm='', n=1) if not l.startswith(('---', '+++'))]
        print('DIFF  ', rel, f'({len(d)} diff lines)')
        for l in d[:int(os.environ.get('SHOW', '12'))]:
            print('   ', l[:300].replace('\n', '\\n'))
print('files differing:', total)
