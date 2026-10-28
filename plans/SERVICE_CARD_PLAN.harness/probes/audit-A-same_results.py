import collections, json, sys
# Run-level stability restricted to pairs whose scenario results are identical. Counts only.
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
tot = collections.Counter(); same = collections.Counter()
for key, rs in groups.items():
    rs = sorted(rs, key=lambda r: r['at'])
    for a, b in zip(rs, rs[1:]):
        if a.get('partial') or b.get('partial'): continue
        if a.get('shapeVersion') is None: continue
        identical = a['results'] == b['results']
        depsd = sorted(a.get('deps') or []) != sorted(b.get('deps') or [])
        tot['pairs'] += 1; tot['deps'] += depsd
        if identical:
            same['pairs'] += 1; same['deps'] += depsd
        ca, cb = tables.get(a.get('shapes')), tables.get(b.get('shapes'))
        if ca is None or cb is None: continue
        oa = {x.rsplit(' ',1)[0] for x in ca}; ob = {x.rsplit(' ',1)[0] for x in cb}
        tot['tpairs'] += 1; tot['ops'] += (oa != ob); tot['calls'] += (set(ca) != set(cb))
        if identical:
            same['tpairs'] += 1; same['ops'] += (oa != ob); same['calls'] += (set(ca) != set(cb))
print('all pairs:', dict(tot))
print('pairs with identical results:', dict(same))
