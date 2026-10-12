# Compares the diagram sources two BreakfastProvider runs wrote, without printing the reports:
#   python compare_bp.py <reports dir A> <reports dir B>
# Sequence sources come from TestRunReport.html's puml-data script, the component diagram from ComponentDiagram.html.
import base64, difflib, gzip, hashlib, html, json, pathlib, re, sys
from collections import Counter


def decode(value):
    try:
        return gzip.decompress(base64.b64decode(value)).decode('utf-8', 'surrogatepass')
    except Exception:
        return value


def sequence_sources(d):
    t = (d / 'TestRunReport.html').read_text(encoding='utf-8', errors='surrogatepass')
    out = []
    for m in re.finditer(r'<script id="puml-data" type="application/json">(.*?)</script>', t, re.S):
        data = json.loads(m.group(1))
        out += [decode(v) for v in data.values() if isinstance(v, str)]
    return out


def component_sources(d):
    p = d / 'ComponentDiagram.html'
    if not p.exists():
        return []
    t = p.read_text(encoding='utf-8', errors='surrogatepass')
    zs = re.findall(r'data-plantuml-z="([^"]+)"', t)
    plain = re.findall(r'data-plantuml="([^"]+)"', t)
    return [decode(html.unescape(z)) for z in zs] + [html.unescape(s) for s in plain]


NOISE = [
    (re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', re.I), 'GUID'),
    (re.compile(r'(?<![0-9A-Za-z])[0-9a-f]{16,32}(?![0-9A-Za-z])', re.I), 'HEX'),
    (re.compile(r'\d{4}-\d{2}-\d{2}[T ][\d:.]+(Z|[+-]\d{2}:?\d{2})?'), 'TS'),
    (re.compile(r'[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} [\d:]+ GMT'), 'DATE'),
    (re.compile(r'\d+(\.\d+)?\s?ms(?![A-Za-z])'), 'Nms'),
]


def normalise(s):
    for rx, rep in NOISE:
        s = rx.sub(rep, s)
    return s


def short(line, n=160):
    return line if len(line) <= n else line[:n] + f'… ({len(line)} chars)'


def caps(sources):
    c = Counter()
    for s in sources:
        c['cut marker …'] += s.count('…')
        c['+N more'] += len(re.findall(r'…, \+\d+ more', s))
        c['lone surrogate'] += len(re.findall(r'[\ud800-\udbff](?![\udc00-\udfff])|(?<![\ud800-\udbff])[\udc00-\udfff]', s))
    return dict(c)


a, b = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
for kind, get in (('component', component_sources), ('sequence', sequence_sources)):
    sa, sb = [normalise(s) for s in get(a)], [normalise(s) for s in get(b)]
    ha = Counter(hashlib.sha1(s.encode('utf-8', 'surrogatepass')).hexdigest() for s in sa)
    hb = Counter(hashlib.sha1(s.encode('utf-8', 'surrogatepass')).hexdigest() for s in sb)
    same = sum((ha & hb).values())
    print(f'== {kind}: {len(sa)} vs {len(sb)} sources, {same} identical; caps A {caps(sa)} B {caps(sb)}')
    only_a = [s for s in sa if hashlib.sha1(s.encode('utf-8', 'surrogatepass')).hexdigest() not in hb]
    only_b = [s for s in sb if hashlib.sha1(s.encode('utf-8', 'surrogatepass')).hexdigest() not in ha]
    changed = Counter()
    shown = 0
    for x in only_a:
        best = max(only_b, key=lambda y: difflib.SequenceMatcher(None, x[:4000], y[:4000]).quick_ratio(), default=None)
        if best is None:
            print('  only in A:', short(x.splitlines()[0] if x else ''))
            continue
        diff = [l for l in difflib.unified_diff(x.splitlines(), best.splitlines(), lineterm='', n=0)
                if l[:1] in '+-' and not l.startswith(('+++', '---'))]
        key = tuple(re.sub(r'\d+', 'N', l[:40]) for l in diff[:2])
        changed[key] += 1
        if shown < 6:
            shown += 1
            print(f'  changed ({len(diff)} lines):')
            for l in diff[:6]:
                print('    ' + short(l))
    for k, n in changed.most_common(10):
        print(f'  {n} x {k}')
