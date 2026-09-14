"""Is the residual fingerprint disagreement HARNESS DIFFERENCE or DRIFT? Plan §2.4 / §2.10.

Drift is unstructured: it would scatter disagreement evenly across every pair of runs. Harness
difference is structured: specific frameworks disagree with everyone, the rest agree perfectly.
Computes the pairwise agreement matrix over scenarios shared by each pair, plus order-insensitive
(shapeSet) and ordered (shapeOrdered) variants. Usage: python pairwise.py <report>...
"""
import json, re, sys, hashlib, itertools, os
from collections import defaultdict

SUBS = [(re.compile(r'[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'), '{id}'),
        (re.compile(r'\b[0-9a-fA-F]{32}\b'), '{id}'),
        (re.compile(r'\b[0-9a-fA-F]{16,}\b'), '{id}'),
        (re.compile(r'\b[0-9A-HJKMNP-TV-Z]{26}\b'), '{id}'),
        (re.compile(r'\d{4}-\d{2}-\d{2}T[\d:.]+Z?'), '{ts}'),
        (re.compile(r'(?<![A-Za-z])\d{1,}(?![A-Za-z])'), '{n}')]

def tmpl(u):
    m = re.match(r'^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/]+(/.*)?$', u)
    tail = (m.group(1) or '/') if m else u
    path, _, q = tail.partition('?')
    for rx, rep in SUBS: path = rx.sub(rep, path)
    if q: path += '?' + '&'.join(sorted(k.split('=')[0] for k in q.split('&') if k))
    return path

def calls(s):
    return ['%s|%s|%s|%s|%s' % (i.get('callerName') or '', i.get('serviceName') or '', i.get('method') or '',
            tmpl(i.get('uri') or ''), i.get('statusCode') or '') for i in (s.get('httpInteractions') or [])]

def h(parts): return hashlib.sha256('\n'.join(parts).encode()).hexdigest()[:8]

runs = {}
for p in sys.argv[1:]:
    name = next((seg for seg in re.split(r'[/\\]', p) if 'Tests' in seg), os.path.basename(p))
    name = name.split('.')[-1]
    d = json.load(open(p, encoding='utf-8-sig'))
    m = {}
    for f in d.get('features', []):
        for s in f.get('scenarios', []):
            c = calls(s)
            m[s.get('stableId')] = (h(sorted(c)), h(c), len(c))
    runs[name] = m

names = sorted(runs)
print("runs: %s\n" % ", ".join(names))
for label, idx in (("shapeSet (order-insensitive, PRIMARY)", 0), ("shapeOrdered (secondary)", 1)):
    print("=== %s ===" % label)
    print("%-12s" % "", "".join("%10s" % n[:9] for n in names))
    per_run_disagreements = defaultdict(int)
    for a in names:
        row = ""
        for b in names:
            if a == b: row += "%10s" % "-"; continue
            shared = set(runs[a]) & set(runs[b])
            if not shared: row += "%10s" % "n/a"; continue
            ok = sum(1 for k in shared if runs[a][k][idx] == runs[b][k][idx])
            row += "%9.1f%%" % (100.0 * ok / len(shared))
            per_run_disagreements[a] += len(shared) - ok
        print("%-12s%s" % (a[:12], row))
    print("  disagreements contributed per run: " +
          ", ".join("%s=%d" % (n, per_run_disagreements[n]) for n in names))
    print()

# --- the decisive test: are the disagreeing SCENARIOS the same across pairs? ---
# Drift would disagree on different scenarios each time (it is noise). Harness difference disagrees
# on the SAME scenarios every time (those scenarios call a different number of things).
print("=== which scenarios disagree, and do the pairs agree about that? ===")
for label, idx in (("shapeSet", 0), ("shapeOrdered", 1)):
    sets = {}
    for a, b in itertools.combinations(names, 2):
        shared = set(runs[a]) & set(runs[b])
        if not shared: continue
        sets[(a, b)] = {k for k in shared if runs[a][k][idx] != runs[b][k][idx]}
    if not sets: continue
    union = set().union(*sets.values()); inter = set.intersection(*sets.values())
    print("  %-13s %d pairs; %d distinct scenarios disagree somewhere; %d disagree in EVERY pair (%.0f%%)"
          % (label, len(sets), len(union), len(inter), 100.0 * len(inter) / max(len(union), 1)))
    for (a, b), d in sets.items():
        print("      %-9s vs %-9s %2d disagreements" % (a, b, len(d)))
    # call-count deltas on the disagreeing scenarios: a harness difference shows up as a count gap
    gaps = []
    for sid in union:
        cnts = {n: runs[n][sid][2] for n in names if sid in runs[n]}
        if len(set(cnts.values())) > 1: gaps.append((sid, cnts))
    print("      of the %d, %d differ in CALL COUNT (a harness making more/fewer calls, not drift)"
          % (len(union), len(gaps)))
    for sid, c in gaps[:4]: print("        %s  %s" % (sid, c))
    print()
