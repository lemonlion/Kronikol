import json,re,sys,hashlib
from collections import defaultdict, Counter
# intra-segment substitution, ordered most-specific first
SUBS=[(re.compile(r'[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'),'{id}'),
      (re.compile(r'\b[0-9a-fA-F]{32}\b'),'{id}'),
      (re.compile(r'\b[0-9a-fA-F]{16,}\b'),'{id}'),
      (re.compile(r'\b[0-9A-HJKMNP-TV-Z]{26}\b'),'{id}'),
      (re.compile(r'\d{4}-\d{2}-\d{2}T[\d:.]+Z?'),'{ts}'),
      (re.compile(r'(?<![A-Za-z])\d{1,}(?![A-Za-z])'),'{n}')]
def tmpl(u,pol):
    m=re.match(r'^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/]+(/.*)?$',u); tail=(m.group(1) or '/') if m else u
    path,_,q=tail.partition('?')
    if pol=='intra':
        for rx,rep in SUBS: path=rx.sub(rep,path)
        p=path
    else:
        p=path
    if q: p+='?'+'&'.join(sorted(k.split('=')[0] for k in q.split('&') if k))
    return p
def shape(s,pol):
    return '\n'.join('%s|%s|%s|%s|%s'%(i.get('callerName') or '',i.get('serviceName') or '',i.get('method') or '',
        tmpl(i.get('uri') or '',pol),i.get('statusCode') or '') for i in (s.get('httpInteractions') or []))
by=defaultdict(dict); allu=set()
for p in sys.argv[1:]:
    proj=p.split('/')[1]; d=json.load(open(p,encoding='utf-8-sig'))
    for f in d.get('features',[]):
        for s in f.get('scenarios',[]):
            by[s.get('stableId')][proj]=s
            for i in s.get('httpInteractions') or []:
                if i.get('uri'): allu.add(i['uri'])
shared={k:v for k,v in by.items() if len(v)>1}
agree=0; diverge=[]
for sid,projs in shared.items():
    hs={hashlib.sha256(shape(s,'intra').encode()).hexdigest()[:8] for s in projs.values()}
    if len(hs)==1: agree+=1
    else: diverge.append((sid,projs))
print('[intra-segment] identical across frameworks: %d/%d (%.1f%%)' % (agree,len(shared),100*agree/len(shared)))
print('distinct templated URIs [intra] = %d  (raw %d)' % (len({tmpl(u,'intra') for u in allu}), len(allu)))
print()
print('remaining divergences: %d' % len(diverge))
causes=Counter()
for sid,projs in diverge[:40]:
    ks=list(projs); a=shape(projs[ks[0]],'intra').split('\n'); b=shape(projs[ks[1]],'intra').split('\n')
    if len(a)!=len(b): causes['different call COUNT (genuine behaviour difference)']+=1; continue
    for i in range(len(a)):
        if a[i]!=b[i]:
            causes['different call content']+=1
            if causes['different call content']<=3:
                print('  %s\n    - %s\n    + %s'%(sid,a[i][:105],b[i][:105]))
            break
for k,v in causes.most_common(): print('  cause: %-46s %d' % (k,v))
