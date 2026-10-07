"""Prints, per test case, every test step's status and message and where each attachment landed."""
import json, sys
env = [json.loads(l) for l in open(sys.argv[1], encoding='utf-8') if l.strip()]
pickles = {e['pickle']['id']: e['pickle'] for e in env if 'pickle' in e}
cases = {e['testCase']['id']: e['testCase'] for e in env if 'testCase' in e}
started = {e['testCaseStarted']['id']: e['testCaseStarted'] for e in env if 'testCaseStarted' in e}
fin = {(e['testStepFinished']['testCaseStartedId'], e['testStepFinished']['testStepId']): e['testStepFinished']['testStepResult'] for e in env if 'testStepFinished' in e}
att = [e['attachment'] for e in env if 'attachment' in e]
for sid, s in started.items():
    tc = cases[s['testCaseId']]; p = pickles[tc['pickleId']]
    texts = {st['id']: st['text'] for st in p['steps']}
    print(f"== {p['name']} (attempt {s['attempt']})")
    for ts in tc['testSteps']:
        label = texts.get(ts.get('pickleStepId')) or ('hook ' + ts['id'].split('-', 2)[-1][-24:])
        r = fin.get((sid, ts['id']), {})
        print(f"   {r.get('status','(none)'):8} {label}" + (f"   message={r.get('message')!r}"[:140] if r.get('message') else ''))
        for a in att:
            if a.get('testCaseStartedId') == sid and a.get('testStepId') == ts['id']:
                print(f"            attachment {a.get('fileName')} ({a.get('mediaType')}) body={a.get('body','')[:60]!r}")
