import collections, json, sys
# For run pairs (same suite, commit, roster, shapeVersion, neither partial, consecutive) whose distinct operations differ,
# classify the differing operations by service and method only. Prints counts, never a path or body.
path = sys.argv[1]
runs, tables = [], {}
with open(path, encoding='utf-8') as f:
    for line in f:
        o = json.loads(line)
        t = o.get('t')
        if t == 'run': runs.append(o)
        elif t == 'shapes': tables[o['hash']] = o['calls']
groups = collections.defaultdict(list)
for r in runs:
    groups[(r.get('suite'), r.get('commit'), r.get('roster'), r.get('shapeVersion'))].append(r)
def ops(run):
    calls = tables.get(run.get('shapes'))
    if calls is None: return None
    return {c.rsplit(' ', 1)[0] for c in calls}
def classify(op, deps):
    # find which dep prefix the op starts with (party names hold spaces)
    for d in sorted(deps, key=len, reverse=True):
        if op.startswith(d + ' '):
            rest = op[len(d)+1:]
            method = rest.split(' ', 1)[0]
            return d.split('>',1)[1] + ' ' + method
    return '?'
pairs = 0; changed = 0
by_pair = collections.Counter(); by_op = collections.Counter()
for key, rs in groups.items():
    rs = sorted(rs, key=lambda r: r['at'])
    for a, b in zip(rs, rs[1:]):
        if a.get('partial') or b.get('partial'): continue
        oa, ob = ops(a), ops(b)
        if oa is None or ob is None: continue
        pairs += 1
        if oa != ob:
            changed += 1
            deps = set(a.get('deps') or []) | set(b.get('deps') or [])
            kinds = {classify(x, deps) for x in (oa ^ ob)}
            by_pair[tuple(sorted(kinds))] += 1
            for k in kinds: by_op[k] += 1
print(f'pairs {pairs}; operations differ {changed}')
print('pairs by the set of (service method) kinds that differ:')
for k, v in by_pair.most_common(): print(f'  {v:3}  {list(k)}')
print('pairs touching each (service method):')
for k, v in by_op.most_common(): print(f'  {v:3}  {k}')
