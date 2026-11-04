#!/usr/bin/env python
"""Side-by-side of two Dump2 files, per test; only tests that differ unless --all."""
import sys,re,difflib
def load(p):
    d={};cur=None
    for line in open(p,encoding='utf-8'):
        line=line.rstrip('\n')
        if line.startswith('## '):
            cur=line[3:];d[cur]=[]
        elif cur is not None and line.strip():
            d[cur].append(line)
    return d
a=load(sys.argv[1]);b=load(sys.argv[2])
flt=sys.argv[3] if len(sys.argv)>3 else None
for k in sorted(set(a)|set(b)):
    if flt and not re.search(flt,k): continue
    x=a.get(k,[]);y=b.get(k,[])
    if x==y and not flt:
        print('== same:',k);continue
    print('#####',k)
    for l in difflib.unified_diff(x,y,lineterm='',n=99):
        if l.startswith(('---','+++','@@')): continue
        print(l[:150])
    print()
