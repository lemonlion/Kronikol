"""Prints only what issue #105 is about, from one ingest output directory (never the whole report)."""
import json, sys, os
d = sys.argv[1]
rep = json.load(open(os.path.join(d, 'TestRunReport.json'), encoding='utf-8-sig'))
hist = json.load(open(os.path.join(d, 'History.run.json'), encoding='utf-8-sig'))
for f in rep.get('features') or []:
    for sc in f.get('scenarios') or []:
        print(f"scenario result={sc.get('result')} history={hist['run']['results']}")
        for st in (sc.get('steps') or []):
            print(f"  {(st.get('keyword') or ''):5} {st.get('status')!s:9} bypassReason={st.get('bypassReason')!r} comments={st.get('comments')!r} :: {st.get('text')}")
