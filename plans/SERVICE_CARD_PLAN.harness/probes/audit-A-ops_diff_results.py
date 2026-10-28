import collections, json, sys
# For the run pairs whose distinct operations differ: do the two runs' scenario results differ? Counts only.
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
def ops(run):
    c = tables.get(run.get('shapes'))
    return None if c is None else {x.rsplit(' ', 1)[0] for x in c}
out = collections.Counter()
for key, rs in groups.items():
    rs = sorted(rs, key=lambda r: r['at'])
    for a, b in zip(rs, rs[1:]):
        if a.get('partial') or b.get('partial'): continue
        oa, ob = ops(a), ops(b)
        if oa is None or ob is None or oa == ob: continue
        diff = oa ^ ob
        mongo = any(' MongoDB ' in ' ' + d.split('>',1)[-1] or 'MongoDB' in d.split(' ',2)[0] for d in diff)
        res_diff = sum(1 for x, y in zip(a['results'], b['results']) if x != y)
        kinds = collections.Counter((x, y) for x, y in zip(a['results'], b['results']) if x != y)
        label = 'mongo-block' if any('MongoDB' in d for d in diff) else ('cosmos' if any('CosmosDB' in d for d in diff) else 'other')
        out[(label, res_diff, tuple(sorted(kinds.items())))] += 1
for k, v in sorted(out.items()): print(v, k)
