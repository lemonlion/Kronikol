"""review5/B05, two BDDfy stories in parallel in one test, over repeated runs: bars drawn and steps nested under another, and whether a call changed phase."""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
for version in sys.argv[2:]:
    rows, phases = [], set()
    folder = root / version
    runs = sorted((p for p in folder.iterdir() if p.is_dir()), key=lambda p: p.name) if folder.exists() else []
    for run in runs:
        dump = run / 'rev5-bddfy.txt'
        if not dump.exists():
            continue
        block, on = [], False
        for line in dump.read_text(encoding='utf-8', errors='replace').splitlines():
            if line.startswith('## '):
                on = 'B05' in line
            elif on:
                block.append(line)
        if not block:
            continue
        cut = next((i for i, l in enumerate(block) if l.strip() == 'steps:'), len(block))
        bars = sum('[Step]' in l for l in block[:cut])
        after = next((i for i, l in enumerate(block) if l.strip().startswith('diagram')), len(block))
        steps = [l for l in block[cut + 1:after] if l.strip()]
        nested = sum(len(l) - len(l.lstrip()) > 4 for l in steps)
        rows.append(f'{bars}/{len(steps)} ({nested} nested)')
        phases.add(tuple(sorted(set(re.findall(r'(\S+)\s+phase=(\w+)', '\n'.join(block[:cut]))))))
    print(version, 'bars of steps, per run:', ', '.join(rows), '; distinct phase readings:', len(phases))
