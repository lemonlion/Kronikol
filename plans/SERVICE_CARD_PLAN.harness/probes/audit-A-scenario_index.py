import collections, json, sys
# How often a card's scenario index (per scenario, the set of operations it touched, status dropped) would change between
# two consecutive same-commit runs of one suite (same roster and shape version, neither partial). Counts only.
path = sys.argv[1]
runs, tables = [], {}
with open(path, encoding='utf-8') as f:
    for line in f:
        o = json.loads(line)
        if o.get('t') == 'run': runs.append(o)
        elif o.get('t') == 'shapes': tables[o['hash']] = o['calls']
groups = collections.defaultdict(list)
for r in runs:
    groups[(r.get('suite'), r.get('commit'), r.get('roster'), r.get('shapeVersion'))].append(r)
def index(run):
    calls = tables.get(run.get('shapes'))
    cs = run.get('callSets')
    if calls is None or cs is None: return None
    out = []
    for s in cs:
        out.append(None if s is None else frozenset(calls[i].rsplit(' ', 1)[0] for i in s))
    return out
c = collections.Counter()
for key, rs in groups.items():
    rs = sorted(rs, key=lambda r: r['at'])
    for a, b in zip(rs, rs[1:]):
        if a.get('partial') or b.get('partial'): continue
        ia, ib = index(a), index(b)
        if ia is None or ib is None: continue
        same = a['results'] == b['results']
        diff = any(x != y for x, y in zip(ia, ib)) or len(ia) != len(ib)
        c['pairs'] += 1; c['index differs'] += diff
        if same:
            c['pairs, identical results'] += 1; c['index differs, identical results'] += diff
print(dict(c))
