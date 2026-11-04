"""Which tests of two dumps differ, ignoring record order and attribution source. Usage: cmp_suites.py a b."""
import re
import sys


def load(p):
    d = {}
    cur = None
    try:
        lines = open(p, encoding='utf-8', errors='replace').read().split('\n')
    except FileNotFoundError:
        print('missing', p)
        return {}
    for l in lines:
        if l.startswith('## '):
            cur = l[3:]
            d[cur] = []
        elif cur is not None and l.strip():
            d[cur].append(re.sub(r'src=\S+', '', l).rstrip())
    return d


a, b = load(sys.argv[1]), load(sys.argv[2])
diff = [k for k in sorted(set(a) | set(b)) if sorted(a.get(k, [])) != sorted(b.get(k, []))]
print(f'{sys.argv[2]}: {len(b)} tests, {len(diff)} differ from {sys.argv[1]}')
for k in diff:
    x, y = a.get(k, []), b.get(k, [])
    print('  ', k[:70])
    only_a = [l for l in x if l not in y]
    only_b = [l for l in y if l not in x]
    for l in only_a[:6]:
        print('      -', l.strip()[:110])
    for l in only_b[:6]:
        print('      +', l.strip()[:110])
