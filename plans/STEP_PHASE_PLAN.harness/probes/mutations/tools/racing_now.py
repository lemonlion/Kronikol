"""Which tests vary over the unmutated rounds of cut 9, per suite: the list a mutant's difference is read against."""
import collections
import os

import compare
from mutrun import FAMILIES

for family in FAMILIES:
    for suite in FAMILIES[family]:
        _, dirs = compare.baseline_dirs(family, suite)
        names = set()
        for d in dirs:
            names |= {f for f in os.listdir(d) if f.endswith(".txt") and f != "outcome.txt" and not f.endswith(".trace.txt")}
        for name in sorted(names):
            seen = collections.defaultdict(collections.Counter)
            shapes = collections.defaultdict(set)
            count = 0
            for d in dirs:
                p = os.path.join(d, name)
                if os.path.exists(p):
                    count += 1
                    for test, lines in compare.read(p).items():
                        seen[test][tuple(lines)] += 1
                        shapes[test].add(compare.shape(list(lines), name))
            racing = {t: c for t, c in seen.items() if len(c) > 1}
            if racing:
                print(f"{family}/{suite}/{name}: {count} dumps, {len(seen)} tests, {len(racing)} vary")
                for t, c in sorted(racing.items()):
                    print(f"    {t.rsplit('.', 1)[-1][:60]}: {len(c)} outputs, {len(shapes[t])} shapes")
