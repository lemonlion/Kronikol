import json,sys,re
verbs={'GET','POST','PUT','DELETE','PATCH','HEAD','OPTIONS','TRACE','CONNECT'}
for path in sys.argv[1:]:
    d=json.load(open(path,encoding='utf8'))
    keys=list(d.keys())
    shapes=d.get('shapes') or {}
    calls=shapes.get('calls') if isinstance(shapes,dict) else None
    run=d.get('run') or {}
    cs=run.get('callSets')
    roster=d.get('roster') or {}
    hit=[]
    for i,c in enumerate(calls or []):
        m=re.match(r'^(.*?>.*?) (\S+) (.*)$',c)
        parts=c.split(' ')
        # Caller>Service METHOD target status: the service may hold spaces, so test every token up to the first path
        for tok in parts:
            if tok.upper() in verbs and tok!=tok.upper():
                hit.append((i,c)); break
    n=0
    if cs:
        hitset={h[0] for h in hit}
        for row in cs:
            if row and any(x in hitset for x in row): n+=1
    print(path,'keys',keys,'distinct calls',len(calls or []),'verb-cased',len(hit),'scenarios with one',n,'of',len(cs or []))
    for i,c in hit[:15]:
        print('   ',c[:120])
