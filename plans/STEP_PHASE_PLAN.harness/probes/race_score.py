"""Parallel Reqnroll on one shared handler, per run: scenarios whose calls read the wrong phase for their step, and scenarios wrong once a missing or misplaced boundary counts too."""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
for version in sys.argv[2:]:
    rows = []
    for run in sorted((root / version).iterdir(), key=lambda p: p.name):
        dump = run / 'rev5-reqnroll-par.txt'
        if not dump.exists():
            continue
        tests, cur = {}, None
        for line in dump.read_text(encoding='utf-8', errors='replace').splitlines():
            if line.startswith('## '):
                cur = line[3:]
                tests[cur] = []
            elif cur is not None and line.strip():
                tests[cur].append(line.strip())
        by_phase = both = 0
        for lines in tests.values():
            step = None
            bad_phase = False
            markers_before_when = when_seen = 0
            marker_right = False
            for line in lines:
                if line.startswith('steps:'):
                    break
                m = re.match(r'\[Step\] <color:white>(\w+)', line)
                if m:
                    # And and But continue the step before them.
                    step = step if m[1] in ('And', 'But') else m[1]
                    when_seen += step == 'When'
                elif line.startswith('--- MARKER'):
                    marker_right = marker_right or step == 'When'
                    markers_before_when += step != 'When'
                else:
                    p = re.search(r'phase=(\w+)', line)
                    if p and step:
                        want = 'Setup' if step == 'Given' else 'Action'
                        bad_phase = bad_phase or p[1] != want
            by_phase += bad_phase
            both += bad_phase or not marker_right or markers_before_when > 0
        rows.append(f'{by_phase}/{both} of {len(tests)}')
    print(version, 'by phase / with the boundary, per run:', ', '.join(rows))
