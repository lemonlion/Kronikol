#!/usr/bin/env python3
"""lanecmp.py a.txt b.txt: compare two dumps by scenario name, ignoring ordinals and call ranges; prints only a summary and short diffs."""
import re,sys,io,difflib
sys.stdout=io.TextIOWrapper(sys.stdout.buffer,encoding='utf-8')
def blocks(p):
    out={}
    t=open(p,encoding='utf-8').read()
    for b in t.split('=== ')[1:]:
        lines=b.splitlines()
        name=' '.join(lines[0].split()[1:])
        body=[re.sub(r's\d+/i\d+','iN',re.sub(r'\[i\d+(-i\d+)?\]( \d+ calls)?','',l)).rstrip() for l in lines[1:] if 'open: TestRun' not in l and not l.strip().startswith('at ') and 'attachment' not in l and 'stableId' not in l]
        out.setdefault(name,[]).append(sorted(body))
    return out
a=blocks(sys.argv[1]); b=blocks(sys.argv[2])
print('scenarios',len(a),len(b))
same=0; shown=0
for k in sorted(set(a)|set(b)):
    if a.get(k)==b.get(k): same+=1; continue
    shown+=1
    if shown>int(sys.argv[3]) if len(sys.argv)>3 else shown>12: continue
    print('DIFF',k[:70])
    x=(a.get(k) or [[]])[0]; y=(b.get(k) or [[]])[0]
    for l in list(difflib.unified_diff(x,y,lineterm='',n=0))[2:8]: print('   ',l[:100])
print('same',same,'different',shown)
