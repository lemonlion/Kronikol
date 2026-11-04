"""What varied over a baseline's rounds, suite by suite, and each suite's outcome. Usage: p00_noise.py [root, default p00]"""
import collections
import glob
import os
import sys

import compare

root = sys.argv[1] if len(sys.argv) > 1 else "p00"
base = os.path.join(compare.RESULTS, root)
rounds = sorted(os.listdir(base))
print("rounds:", rounds)
for suite in sorted(os.listdir(os.path.join(base, rounds[0]))):
    outcomes = set()
    seen = collections.defaultdict(lambda: collections.defaultdict(set))
    for number in rounds:
        folder = os.path.join(base, number, suite)
        if not os.path.isdir(folder):
            continue
        with open(os.path.join(folder, "outcome.txt"), encoding="utf-8") as handle:
            outcomes.add(compare.outcome_key(handle.read().splitlines()))
        for path in glob.glob(os.path.join(folder, "*.txt")):
            name = os.path.basename(path)
            if name == "outcome.txt" or name.endswith(compare.SKIP_FILES):
                continue
            for test, lines in compare.read(path).items():
                seen[name][test].add(tuple(lines))
    varied = sorted({test.rsplit(".", 1)[-1].split("_")[0][:24] for tests in seen.values() for test, outputs in tests.items() if len(outputs) > 1})
    outcome = next(iter(outcomes)) if len(outcomes) == 1 else f"VARIED {outcomes}"
    print(f"{suite}: {outcome}" + (f"\n    varied: {' '.join(varied)}" if varied else ""))
