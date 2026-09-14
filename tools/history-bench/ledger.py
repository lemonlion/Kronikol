import json, os, sys, random, gzip, hashlib, io
random.seed(7)
p=sys.argv[1]; d=json.load(open(p,encoding='utf-8-sig'))
sc=[(f.get('name'),s) for f in d.get('features',[]) for s in f.get('scenarios',[])]
ids=[s.get('stableId') for _,s in sc]; names=[s.get('name') for _,s in sc]; feats=[f for f,_ in sc]
N=len(sc)
def build(runs, durations=True, shapes=True, dwindow=None, swindow=None):
    out=io.StringIO()
    out.write(json.dumps({"t":"header","historyFormatVersion":1,"generator":"3.1.0","window":runs},separators=(',',':'))+'\n')
    out.write(json.dumps({"t":"roster","hash":"r1","ids":ids,"names":names,"features":feats,"slots":[0]*N},separators=(',',':'))+'\n')
    for r in range(runs):
        line={"t":"run","id":"gh:1827364%d:1"%r,"at":"2026-09-%02dT10:04:11Z"%((r%28)+1),"branch":"main",
              "commit":hashlib.sha1(str(r).encode()).hexdigest()[:7],
              "url":"https://github.com/acme/breakfast/actions/runs/1827364%d"%r,
              "provider":"GitHubActions","shards":8,"roster":"r1",
              "results":''.join(random.choice('PPPPPPPPPPPPPPPPPPPF') for _ in range(N)),
              "attempts":'1'*N}
        recent = (runs-r) <= (dwindow or runs)
        if durations and recent: line["durations"]=[random.randint(80,9000) for _ in range(N)]
        recent_s = (runs-r) <= (swindow or runs)
        if shapes and recent_s: line["shapes"]=[hashlib.sha1(str(i).encode()).hexdigest()[:4] for i in range(N)]
        line["errors"]=[None]*N
        out.write(json.dumps(line,separators=(',',':'))+'\n')
    return out.getvalue()
def rep(label, txt):
    b=txt.encode(); g=len(gzip.compress(b,9))
    print('  %-46s %8.1f KB   gz %6.1f KB' % (label, len(b)/1024, g/1024))
print('suite: %s scenarios (%s)' % (N, os.path.basename(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(p)))))))
for w in (20,30,50):
    print(' window %d:' % w)
    rep('full (durations + shapes, every run)', build(w))
    rep('durations+shapes only last 10 runs',  build(w, dwindow=10, swindow=10))
    rep('no durations, no shapes',             build(w, durations=False, shapes=False))
print()
print('  six suites in one repo, window 50, recent-10 policy: %.1f KB' % (6*len(build(50,dwindow=10,swindow=10).encode())/1024))
