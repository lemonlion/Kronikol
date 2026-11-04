"""Every scenario of the parallel Reqnroll suite against the rule: exactly one marker, each call on the right side of it with the right phase. Usage: check_par.py dump"""
import re, sys
cur = None; tests = {}
for l in open(sys.argv[1], encoding='utf-8', errors='replace'):
    l = l.rstrip()
    if l.startswith('## '):
        cur = l[3:]; tests[cur] = []
    elif cur and l.strip().startswith('steps:'):
        cur = None
    elif cur and l.strip():
        tests[cur].append(l.strip())
bad = 0
for name, rows in sorted(tests.items()):
    markers = [i for i, r in enumerate(rows) if 'MARKER' in r]
    problems = []
    if len(markers) != 1:
        problems.append(f'{len(markers)} markers')
    m = markers[0] if markers else len(rows)
    for i, r in enumerate(rows):
        g = re.match(r'(Request|Response)\s+(\S+)\s+phase=(\S+)', r)
        if not g:
            continue
        label, phase = g[2], g[3]
        want = 'Setup' if ('seed' in label) else 'Action'
        side = 'Setup' if i < m else 'Action'
        if phase != want:
            problems.append(f'{label} reads {phase}')
        if g[1] == 'Request' and side != want:
            problems.append(f'{label} drawn in {side}')
    if problems:
        bad += 1
        print(name[:40], '; '.join(problems[:5]))
print(f'{len(tests)} scenarios, {bad} wrong')
