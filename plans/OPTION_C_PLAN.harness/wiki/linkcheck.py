import sys, re, os, glob
def slug(h):
    h = h.strip().lower()
    h = re.sub(r'[^\w\s-]', '', h, flags=re.UNICODE)
    return re.sub(r'\s', '-', h)
anchors = {}
for p in glob.glob('*.md'):
    seen, out = {}, set()
    in_code = False
    for l in open(p, encoding='utf-8').read().splitlines():
        if l.startswith('```'): in_code = not in_code
        if in_code: continue
        m = re.match(r'^#{1,6}\s+(.*?)\s*#*\s*$', l)
        if m:
            s = slug(re.sub(r'\[\[([^|\]]*\|)?([^\]]*)\]\]', r'\2', m.group(1)).replace('`', ''))
            n = seen.get(s, 0); seen[s] = n + 1
            out.add(s if n == 0 else f'{s}-{n}')
    anchors[p[:-3]] = out
bad = 0
for p in sys.argv[1:]:
    t = open(p, encoding='utf-8').read()
    t = re.sub(r'```.*?```', '', t, flags=re.S)
    links = [(m.group(2) or m.group(1)) for m in re.finditer(r'\[\[([^|\]]+)(?:\|([^\]]+))?\]\]', t)]
    links += [m.group(1) for m in re.finditer(r'\]\(((?!https?:|mailto:|#)[^)\s]+)\)', t)]
    links += [p[:-3] + m.group(1) for m in re.finditer(r'\]\((#[^)\s]+)\)', t)]
    for l in links:
        page, _, frag = l.partition('#')
        page = page.strip().replace(' ', '-')
        if page.endswith('.md'): page = page[:-3]
        if page not in anchors:
            if '/' in page or '.' in page: continue
            print(p, 'NO PAGE', l); bad += 1; continue
        if frag and frag not in anchors[page]:
            print(p, 'NO ANCHOR', l); bad += 1
print('bad', bad)
