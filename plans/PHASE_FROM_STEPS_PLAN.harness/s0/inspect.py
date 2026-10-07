import json, os, sys
out = sys.argv[1]
d = json.load(open(os.path.join(out, 'TestRunReport.json'), encoding='utf-8-sig'))
orig = {'orders-api': 'a', 'Database': 'b'}
for f in d['features']:
    for s in f['scenarios']:
        print(f'scenario id={s["id"]} name={s["name"]!r} result={s["result"]} steps={[st.get("text") for st in s["steps"]]}')
        hi = s.get('httpInteractions') or []
        print(f'httpInteractions: {len(hi)}')
        print('  {:<3} {:<10} {:<9} {:<8} | {:<25} {}'.format('in', 'rrId', 'type', 'phase', 'timestamp (extra)', 'stepPath (extra)'))
        for i in hi:
            print('  {:<3} {:<10} {:<9} {:<8} | {:<25} {}'.format(
                orig.get(i.get('serviceName'), '?'), str(i.get('requestResponseId'))[:8], str(i.get('type')), str(i.get('phase')),
                str(i.get('timestamp')), json.dumps(i.get('stepPath'))))
bg = d.get('background') or {}
print(f'background.interactions: {len(bg.get("interactions") or [])}')
diags = d.get('diagnostics') or []
print(f'top-level diagnostics: {len(diags)}')
for x in diags:
    print(f'  - kind={x.get("kind")} scenarioId={x.get("scenarioId")}: {x.get("message")}')
