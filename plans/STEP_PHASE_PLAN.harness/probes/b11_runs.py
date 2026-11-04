"""B11's fingerprint over repeated runs of one version: how many distinct sets it reads, and with how many calls."""
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
seen = {}
for path in sorted(root.glob('*.json')):
    version = path.stem.rsplit('-', 1)[0]
    doc = json.loads(path.read_text(encoding='utf-8'))
    names = doc['roster']['names']
    run = doc['run']
    for name, shape, calls in zip(names, run['shapeSet'], run['calls']):
        if 'B11' in name or 'b11' in name:
            seen.setdefault(version, []).append((calls, shape))
for version, rows in seen.items():
    counts = {}
    for calls, shape in rows:
        counts[(calls, str(shape)[:16])] = counts.get((calls, str(shape)[:16]), 0) + 1
    print(version, 'runs', len(rows), 'distinct (calls, set):', counts)
