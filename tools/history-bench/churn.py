import json,re,sys,hashlib
from collections import defaultdict
GUID=re.compile(r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')
HEX32=re.compile(r'^[0-9a-fA-F]{32}$'); NUM=re.compile(r'^\d+$')
ULID=re.compile(r'^[0-9A-HJKMNP-TV-Z]{26}$'); ISO=re.compile(r'^\d{4}-\d{2}-\d{2}([T_].*)?$')
def tmpl(u,pol):
    m=re.match(r'^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/]+(/.*)?$',u); tail=(m.group(1) or '/') if m else u
    path,_,q=tail.partition('?'); out=[]
    for s in [x for x in path.split('/') if x]:
        if pol=='none': out.append(s)
        elif pol=='guid': out.append('{guid}' if GUID.match(s) else s)
        else: out.append('{id}' if (GUID.match(s) or HEX32.match(s) or NUM.match(s) or ULID.match(s) or ISO.match(s)) else s)
    p='/'+'/'.join(out)
    if q: p+='?'+('&'.join(sorted(k.split('=')[0] for k in q.split('&') if k)) if pol=='measured' else q)
    return p
def shape(s,pol):
    return '\n'.join('%s|%s|%s|%s|%s'%(i.get('callerName') or '',i.get('serviceName') or '',i.get('method') or '',
        tmpl(i.get('uri') or '',pol),i.get('statusCode') or '') for i in (s.get('httpInteractions') or []))
by=defaultdict(dict)
for p in sys.argv[1:]:
    proj=p.split('/')[1]
    d=json.load(open(p,encoding='utf-8-sig'))
    for f in d.get('features',[]):
        for s in f.get('scenarios',[]):
            by[s.get('stableId')][proj]=s
shared={k:v for k,v in by.items() if len(v)>1}
print('scenarios present in >1 framework: %d' % len(shared))
for pol in ('none','guid','measured'):
    agree=0; disagree=[]
    for sid,projs in shared.items():
        hs={hashlib.sha256(shape(s,pol).encode()).hexdigest()[:8] for s in projs.values()}
        if len(hs)==1: agree+=1
        else: disagree.append(sid)
    print('  [%-8s] identical shape across frameworks: %3d/%3d (%.1f%%)' % (pol,agree,len(shared),100*agree/len(shared)))
    if pol=='measured' and disagree:
        sid=disagree[0]; projs=shared[sid]
        ks=list(projs)[:2]
        a=shape(projs[ks[0]],'measured').split('\n'); b=shape(projs[ks[1]],'measured').split('\n')
        print('    example divergence %s (%s vs %s): %d vs %d calls' % (sid,ks[0].split('.')[-1],ks[1].split('.')[-1],len(a),len(b)))
        for i in range(min(len(a),len(b))):
            if a[i]!=b[i]:
                print('      first differing call #%d:\n        - %s\n        + %s'%(i,a[i][:110],b[i][:110])); break
