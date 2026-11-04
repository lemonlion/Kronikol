#!/usr/bin/env python3
"""show.py <dump.txt> <name fragment or sN>...: print one scenario's block"""
import sys,io
sys.stdout=io.TextIOWrapper(sys.stdout.buffer,encoding='utf-8')
t=open(sys.argv[1],encoding='utf-8').read()
for want in sys.argv[2:]:
    for blk in t.split('=== ')[1:]:
        head=blk.splitlines()[0]
        if head.startswith(want+' ') or want in head:
            print('=== '+head[:100])
            for l in blk.splitlines()[1:]:
                if 'open: TestRun' in l or l.strip().startswith('at ') or l.strip().startswith('· '): continue
                print(l[:112])
