"""Over all mutants' diff.txt: which tests changed under how many mutants, by class. A test many unrelated mutants change is a race the unmutated rounds did not catch."""
import collections
import glob
import os
import re

from dumps import MUT

RESULTS = os.path.join(MUT, "results")
counts = collections.defaultdict(lambda: collections.defaultdict(list))
for path in sorted(glob.glob(os.path.join(RESULTS, "m*", "diff.txt"))):
    mutant = os.path.basename(os.path.dirname(path))
    if mutant == "m00":
        continue
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            match = re.match(r"\[(.*?)\] (\S+): (SOLID|ORDER|RACING-NEW) (.*)", line)
            if match:
                place, dump, kind, test = match.groups()
                counts[(place, dump, test[:80])][kind].append(mutant)
rows = []
for key, kinds in counts.items():
    total = sum(len(v) for v in kinds.values())
    rows.append((total, key, kinds))
for total, key, kinds in sorted(rows, key=lambda r: -r[0]):
    if total >= 3:
        text = " ".join(f"{kind}:{len(mutants)}" for kind, mutants in kinds.items())
        print(f"{total:3d} {key[0].split('/')[-1]}/{key[1].replace('.txt', '')} {key[2].rsplit('.', 1)[-1][:50]}  {text}  {' '.join(sum(kinds.values(), []))[:110]}")
