"""review5/B05 over repeated runs: the keywords that get a bar, in order, and how many runs read each."""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
for version in sys.argv[2:]:
    counts = {}
    folder = root / version
    runs = sorted((p for p in folder.iterdir() if p.is_dir()), key=lambda p: p.name) if folder.exists() else []
    for run in runs:
        dump = run / 'rev5-bddfy.txt'
        if not dump.exists():
            continue
        on, bars = False, []
        for line in dump.read_text(encoding='utf-8', errors='replace').splitlines():
            if line.startswith('## '):
                on = 'B05' in line
            elif on:
                if line.strip() == 'steps:':
                    break
                m = re.search(r'\[Step\] <color:white>(\w+)', line)
                if m:
                    bars.append(m[1][0])
        key = ' '.join(bars)
        counts[key] = counts.get(key, 0) + 1
    print(version, counts)
