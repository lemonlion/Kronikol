import re, sys
# usage: cmp3.py [-a] f9.txt 2720.txt pub.txt : each scenario by title, what its calls read on each build. -a shows the ones that agree too.
args = sys.argv[1:]
show_all = False
if args[0] == '-a':
    show_all = True
    args = args[1:]

def read(path):
    out = {}
    cur = None
    mode = None
    for line in open(path, encoding='utf-8', errors='replace'):
        line = line.rstrip('\r\n')
        m = re.match(r'### s\d+ steps', line)
        if m:
            mode = 'title'
            continue
        if mode == 'title':
            m = re.match(r's\d+\s+(.*?)\s+\[(\w+)\]', line)
            if m:
                cur = m[1]
                n = 0
                while cur in out:
                    n += 1
                    cur = f"{m[1]} #{n}"
                out[cur] = {'status': m[2], 'rows': [], 'calls': []}
                mode = 'rows'
            continue
        if line.startswith('--- s') and ' calls' in line:
            mode = 'calls'
            continue
        if mode == 'rows':
            m = re.match(r'^(.?)\s+(b?\d+)\s+(.*)$', line)
            if m and not line.startswith('  ' * 2 + ' '):
                text = re.sub(r'\s+\[i\d+.*$', '', m[3])
                text = re.sub(r'\s+[\d.]+s$', '', text)
                out[cur]['rows'].append(f"{m[2]}:{text}")
            continue
        if mode == 'calls':
            m = re.match(r'^(\S+)\s+(GET|POST|PUT|DELETE)\s+(\S+)\s+(\d+)', line)
            if m:
                out[cur]['calls'].append((m[3], m[1], int(m[4])))
    return out

def fmt(entry):
    if entry is None:
        return '(absent)'
    return ' '.join(f"{p}={s}" + (f"x{c}" if c != 1 else '') for p, s, c in sorted(entry['calls'], key=lambda c: c[0]))

files = [read(p) for p in args]
names = ['f9', '2720', 'pub'][:len(files)]
titles = []
for f in files:
    for t in f:
        if t not in titles:
            titles.append(t)
for t in sorted(titles):
    values = [fmt(f.get(t)) for f in files]
    rows = [f.get(t, {}).get('rows') for f in files]
    same = len(set(values)) == 1 and all(r == rows[0] for r in rows)
    if same and not show_all:
        continue
    print(f"--- {t}  [{' / '.join(f.get(t, {}).get('status', '?') for f in files)}]")
    seen = None
    for name, r in zip(names, rows):
        if r != seen:
            print(f"    rows {name}: {' | '.join(r or [])}")
            seen = r
    for name, v in zip(names, values):
        print(f"    {name:<5}{v}")
