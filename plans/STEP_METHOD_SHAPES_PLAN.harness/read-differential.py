# What the differential found: outcomes overall, then the runs that differ, grouped by statement form and by condition, with a sample of each.
import collections
import csv
import sys

rows = list(csv.DictReader(open(sys.argv[1], encoding='utf-8', errors='replace'), delimiter='\t'))
print('runs', len(rows), collections.Counter(r['outcome'] for r in rows).most_common())
bad = [r for r in rows if r['outcome'] not in ('OK', 'ASSERTION-LOST')]
for kind in ('form', 'condition', 'kind', 'config'):
    tally = collections.Counter()
    for r in bad:
        _, cond, form, k = r['id'].split('-')
        tally[{'form': form, 'condition': cond, 'kind': k, 'config': r['config']}[kind]] += 1
    print(kind, tally.most_common(40))
seen = set()
for r in bad:
    _, cond, form, k = r['id'].split('-')
    key = (form, r['outcome'], r['config'])
    if key in seen:
        continue
    seen.add(key)
    print(r['id'], r['config'], r['outcome'], '|', (r['value'] or '')[:90], '|', (r['detail'] or '')[:150].replace('\n', ' '))
lost = [r for r in rows if r['outcome'] == 'ASSERTION-LOST']
tally = collections.Counter(r['id'].split('-')[2] for r in lost)
print('assertion lost with no word, by form', tally.most_common(40))
