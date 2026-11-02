# Every key of every scenario, and of every call of it, in a real report's JSON against the same in its merged JSON: which
# keys a merge moves at all, by count, with no value printed. args: <before.json> <after.json>
import json, collections, sys

before, after = (json.load(open(p, encoding='utf-8')) for p in sys.argv[1:3])


def scenarios(doc):
    out = {}
    for feature in doc['features']:
        for scenario in feature['scenarios']:
            out.setdefault(scenario['id'], []).append((feature['name'], scenario))
    return out


a, b = scenarios(before), scenarios(after)
print('scenarios', sum(map(len, a.values())), sum(map(len, b.values())), 'ids only before', len(set(a) - set(b)), 'ids only after', len(set(b) - set(a)))
scenario_keys, call_keys = collections.Counter(), collections.Counter()
calls = 0
for sid in set(a) & set(b):
    for (fa, sa), (fb, sb) in zip(a[sid], b[sid]):
        if fa != fb:
            scenario_keys['(feature name)'] += 1
        for key in set(sa) | set(sb):
            if key in ('httpInteractions',):
                continue
            if sa.get(key) != sb.get(key):
                scenario_keys[key] += 1
        ia, ib = sa.get('httpInteractions') or [], sb.get('httpInteractions') or []
        if len(ia) != len(ib):
            call_keys['(count of calls)'] += 1
        for ca, cb in zip(ia, ib):
            calls += 1
            for key in set(ca) | set(cb):
                if ca.get(key) != cb.get(key):
                    call_keys[key] += 1
print('calls compared', calls)
print('scenario keys that moved:', dict(scenario_keys))
print('call keys that moved:', dict(call_keys))
top = [k for k in set(before) | set(after) if k not in ('features',) and before.get(k) != after.get(k)]
print('top-level keys that differ:', sorted(top))
bg_a = (before.get('background') or {}).get('interactions') or []
bg_b = (after.get('background') or {}).get('interactions') or []
print('background calls', len(bg_a), len(bg_b))
