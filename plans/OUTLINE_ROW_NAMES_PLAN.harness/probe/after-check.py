"""The proof of plans/OUTLINE_ROW_NAMES_PLAN.md 4.6 on the probe: the run of the fix (results/after/) against 5.4.0's
(results/before-5.4.0/) and the Cucumber lane of 5.4.0's run (results/cucumber-lane-5.4.0/), each read from its
query-scenarios-all.json (kronikol query scenarios --json --max-bytes 0). Every row must keep its address, feature,
values and stable id, and take the name the Cucumber lane gives the same row. Run from this folder:
python -I after-check.py > results/after-check.txt"""
import json
from collections import defaultdict


def items(lane):
    with open(f'results/{lane}/query-scenarios-all.json', encoding='utf-8') as f:
        return json.load(f)['items']


def values(item):
    return json.dumps(item.get('exampleValues'), ensure_ascii=False, sort_keys=True)


before, after, cucumber = items('before-5.4.0'), items('after'), items('cucumber-lane-5.4.0')
print(f'rows: before {len(before)}, after {len(after)}, cucumber lane {len(cucumber)}')

# ids, addresses and order: the same row at the same address, with the same id
by_address = {i['address']: i for i in before}
kept = [a for a in after if a['address'] in by_address
        and (by_address[a['address']]['feature'], values(by_address[a['address']]), by_address[a['address']]['stableId'])
        == (a['feature'], values(a), a['stableId'])]
print(f'address, feature, values and stableId as 5.4.0 wrote them: {len(kept)} of {len(after)}')
for a in after:
    if a not in kept:
        print(f'  MOVED {a["address"]} {a["feature"]} {values(a)} {a["stableId"]} {a["scenario"]!r}')

# names: per feature and values, the Reqnroll lane's names are the Cucumber lane's
def names(rows):
    grouped = defaultdict(list)
    for r in rows:
        grouped[(r['feature'], values(r))].append(r['scenario'])
    return {k: sorted(v) for k, v in grouped.items()}

reqnroll, gherkin = names(after), names(cucumber)
same = sum(len(v) for k, v in reqnroll.items() if gherkin.get(k) == v)
print(f'names the Cucumber lane gives the same rows: {same} of {len(after)}')
for k, v in sorted(reqnroll.items()):
    if gherkin.get(k) != v:
        print(f'  DIFFERS {k}: {v!r} vs {gherkin.get(k)!r}')

placeholders = [a for a in after if '<case>' in a['scenario']]
print(f'rows still named (<case>): {len(placeholders)}')
changed = sum(1 for a in after if a['address'] in by_address and by_address[a['address']]['scenario'] != a['scenario'])
print(f'rows whose name changed: {changed} of {len(after)}')
