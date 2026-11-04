"""The reference consumer's diagrams, structure only: arrows, partitions, step bars, with note bodies, ids and durations out. Usage: compare_consumer_structure.py <results/consumer>"""
import pathlib, re, sys, difflib, collections
root = pathlib.Path(sys.argv[1])
GUID = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')
def structure(path):
    out, in_note = [], False
    for line in path.read_text(encoding='utf-8', errors='replace').splitlines():
        s = line.strip()
        if in_note:
            if s in ('end note', 'end hnote', 'endhnote', 'endnote'): in_note = False
            continue
        if re.match(r'^(note|hnote|rnote)\b', s):
            if ':' not in s.split('note', 1)[1]: in_note = True
            if s.startswith('hnote across') or '<<stepDelimiter>>' in s: out.append(GUID.sub('{g}', s)[:120])
            continue
        if s.startswith(('partition', 'end', 'group', 'loop', 'alt', 'else')) or '->' in s or '-[' in s:
            s = re.sub(r'[0-9a-f]{32}', '{hex}', GUID.sub('{g}', re.sub(r'#iflow-[^\s\]]+', '#iflow', s)))
            out.append(re.sub(r'\d{6,}', '{n}', re.sub(r'\b\d+(\.\d+)?\s?(ms|s)\b', '{t}', s))[:150])
    return out
for lane in sorted(x.name for x in (root/'base').iterdir()):
    a, b = root/'base'/lane/'diagrams', root/'final'/lane/'diagrams'
    same = moved = 0
    kinds = collections.Counter(); examples = []
    for p in sorted(a.glob('*.puml')):
        q = b/p.name
        if not q.exists(): kinds['only base'] += 1; continue
        x, y = structure(p), structure(q)
        if x == y: same += 1; continue
        moved += 1
        hasA, hasB = any(l.startswith('partition') for l in x), any(l.startswith('partition') for l in y)
        kind = 'partition new' if hasB and not hasA else 'partition gone' if hasA and not hasB else 'partition both' if hasA else 'no partition'
        # A move of the same lines is the race or the boundary; a changed set is more.
        kinds[kind + (', same lines' if sorted(x) == sorted(y) else ', lines differ')] += 1
        if len(examples) < 5: examples.append((p.name, [l[:120] for l in difflib.unified_diff(x, y, lineterm='', n=0) if not l.startswith(('---', '+++', '@@'))][:6]))
    print(f'== {lane}: {same} same, {moved} differ in structure: {dict(kinds)}')
    for n, d in examples:
        print('  ', n)
        for l in d: print('     ', l)
