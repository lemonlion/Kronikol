import json,sys,io
sys.stdout=io.TextIOWrapper(sys.stdout.buffer,encoding='utf-8')
d=json.load(sys.stdin)
for it in d['items']:
    for f in it['failingSteps']:
        print(' ',f['path'],f['text'],'callsTotal=',f['callsTotal'],[c['summary'] for c in f['calls']])
