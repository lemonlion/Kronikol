import sys, re
def parse(p):
    cases, cur = {}, None
    for line in open(p, encoding='utf-8', errors='replace').read().replace('\r\n', '\n').split('\n'):
        m = re.match(r'^== (\S+): (.*)$', line)
        if m:
            cur = m.group(1); cases[cur] = [m.group(2)]; continue
        if cur and re.match(r'^\s+[\u2713\u2717] ', line):
            cases[cur].append(line.strip())
    return cases
a, b = parse(sys.argv[1]), parse(sys.argv[2])
same = 0
for k in a:
    if a[k] == b.get(k):
        same += 1; continue
    print(f'{k}:')
    for l in a[k][1:]: print(f'   old  {l}')
    for l in b.get(k, ['MISSING'])[1:]: print(f'   new  {l}')
    if a[k][0] != b.get(k, [''])[0]: print(f'   outcome {a[k][0]} -> {b.get(k,["?"])[0]}')
print(f'unchanged cases: {same} of {len(a)}')
