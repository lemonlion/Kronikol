"""The reference consumer, lane by lane: phase totals, phase buckets, diagrams with ids and times masked, fingerprints. Usage: compare_consumer.py <results/consumer>"""
import json, pathlib, re, sys
root = pathlib.Path(sys.argv[1])
MASK = [(re.compile(r'-[0-9a-f]{16}-0[01]'), '-{span}-00'), (re.compile(r'Date=[^]]+'), 'Date={d}'),
        (re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'), '{guid}'), (re.compile(r'[0-9a-f]{32}'), '{hex}'),
        (re.compile(r'\b\d+(\.\d+)? ?(ms|s)\b'), '{t}'), (re.compile(r'\d{4}-\d\d-\d\dT[\d:.]+Z?'), '{ts}')]
def mask(t):
    for rx, s in MASK: t = rx.sub(s, t)
    return t
def fp(p):
    d = json.loads(p.read_text(encoding='utf-8')); ro = d['roster']; r = d['run']
    return d['run'].get('shapeVersion'), {n: (s, o, c) for n, s, o, c in zip(ro['names'], r['shapeSet'], r['shapeOrdered'], r['calls'])}
for lane in sorted(x.name for x in (root/'base').iterdir()):
    a, b = root/'base'/lane, root/'final'/lane
    print('==', lane)
    for f in ('phase-totals.txt', 'phases.txt'):
        ta = (a/f).read_text(encoding='utf-8', errors='replace').splitlines() if (a/f).exists() else []
        tb = (b/f).read_text(encoding='utf-8', errors='replace').splitlines() if (b/f).exists() else []
        def table(lines):
            out = {}
            for l in lines[1:]:
                c = l.split()
                if len(c) >= 3 and not l.endswith('buckets'):
                    m = re.match(r'^(.*?)\s+(\d+)\s+(\d+)\s', l)
                    if m: out[re.sub(r'\s+', ' ', m[1])] = (int(m[2]), int(m[3]))
            return out
        x, y = table(ta), table(tb)
        if f == 'phase-totals.txt':
            print('  totals base :', {k: v[0] for k, v in x.items()})
            print('  totals final:', {k: v[0] for k, v in y.items()})
        else:
            moved = [(k, x.get(k, (0, 0))[0], y.get(k, (0, 0))[0]) for k in sorted(set(x) | set(y)) if x.get(k, (0, 0))[0] != y.get(k, (0, 0))[0]]
            print('  buckets moved:', len(moved), 'of', len(set(x) | set(y)))
            for m in moved[:40]: print('    ', m)
    da, db = {p.name: p for p in (a/'diagrams').glob('*')}, {p.name: p for p in (b/'diagrams').glob('*')}
    same = sum(1 for k in da if k in db and mask(da[k].read_text(encoding='utf-8', errors='replace')) == mask(db[k].read_text(encoding='utf-8', errors='replace')))
    print(f'  diagrams: {len(da)} base, {len(db)} final, {same} same once masked')
    if (a/'History.run.json').exists() and (b/'History.run.json').exists():
        va, x = fp(a/'History.run.json'); vb, y = fp(b/'History.run.json')
        setmoved = [k for k in x if k in y and x[k][0] != y[k][0]]
        ordmoved = [k for k in x if k in y and x[k][0] == y[k][0] and x[k][1] != y[k][1]]
        print(f'  fingerprints: version {va}->{vb}; {len(x)} scenarios; set moved {len(setmoved)}; order only {len(ordmoved)}; only-one-side {len(set(x)^set(y))}')
        for k in setmoved[:12]: print('    SET', str(k)[:70], 'calls', x[k][2], '->', y[k][2])
