import json, re, sys, os, hashlib
from collections import Counter
GUID=re.compile(r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')
HEX32=re.compile(r'^[0-9a-fA-F]{32}$'); NUM=re.compile(r'^\d+$')
ULID=re.compile(r'^[0-9A-HJKMNP-TV-Z]{26}$'); ISO=re.compile(r'^\d{4}-\d{2}-\d{2}([T_].*)?$')
def tmpl(u, pol):
    m=re.match(r'^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/]+(/.*)?$',u); tail=(m.group(1) or '/') if m else u
    path,_,q=tail.partition('?'); out=[]
    for s in [x for x in path.split('/') if x]:
        if pol=='none': out.append(s)
        elif pol=='guid': out.append('{guid}' if GUID.match(s) else s)
        else:
            out.append('{id}' if (GUID.match(s) or HEX32.match(s) or NUM.match(s) or ULID.match(s) or ISO.match(s)) else s)
    p='/'+'/'.join(out)
    if q: p += '?' + ('&'.join(sorted(k.split('=')[0] for k in q.split('&') if k)) if pol=='measured' else q)
    return p
def load(p): return json.load(open(p, encoding='utf-8-sig'))
def scen(d):
    for f in d.get('features',[]):
        for s in f.get('scenarios',[]): yield f.get('name'),s
files=sys.argv[1:]
allu=set(); ids={}
for p in files:
    d=load(p); proj=p.split('/')[1] if '/' in p else p
    n=0
    for fn,s in scen(d):
        n+=1
        ids.setdefault(s.get('stableId'),set()).add(proj)
        for i in s.get('httpInteractions',[]) or []:
            if i.get('uri'): allu.add(i['uri'])
    print('%-52s scenarios=%d' % (proj, n))
print()
for pol in ('none','guid','measured'):
    print('distinct templated URIs [%-8s] = %d' % (pol, len({tmpl(u,pol) for u in allu})))
shared=[k for k,v in ids.items() if len(v)>1]
print()
print('distinct stableIds across %d projects: %d' % (len(files), len(ids)))
print('stableIds appearing in MORE THAN ONE project: %d (%.1f%%)' % (len(shared), 100*len(shared)/max(len(ids),1)))
if shared: print('  e.g.', shared[:3])
