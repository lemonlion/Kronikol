import csv, sys, collections
rows = list(csv.DictReader(open(sys.argv[1], encoding='utf-8'), delimiter='\t'))
c = collections.Counter()
for r in rows:
    k = r['corpus']; c[k, 'n'] += 1
    for col in ('C', 'REC', 'RECSEG'):
        if r[col] != r['today']:
            c[k, col] += 1
            if r[col].lower() == r['today'].lower(): c[k, col + '_caseonly'] += 1
    if r['REC'] != r['C']: c[k, 'REC_vs_C'] += 1
for k in sorted({k for k, _ in c}):
    print(k, {m: c[k, m] for m in ('n', 'C', 'C_caseonly', 'REC', 'REC_caseonly', 'REC_vs_C', 'RECSEG')})
shown = 0
for r in rows:
    if r['REC'] != r['C'] and shown < 8:
        print('  ', r['method'][:45], '|', r['C'][:45], '|', r['REC'][:45]); shown += 1
