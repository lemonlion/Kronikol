"""Where the boundary stands in a test, unmutated against a mutant, and each one's real calls by phase.
Usage: marker.py <mutant id> <family-suite> <dump file> <test regex>
With results/<id>/repeat/<n>/ present, every repeat is counted too."""
import collections
import glob
import os
import re
import sys

import compare

mutant, place, name, pattern = sys.argv[1:5]
compare.BASELINE["root"] = {"p": "p00", "n": "n00"}.get(mutant[0], "m00")
base_root = os.path.join(compare.RESULTS, compare.BASELINE["root"])
wanted = re.compile(pattern)


def gather(paths):
    seen = collections.defaultdict(collections.Counter)
    for path in paths:
        if os.path.exists(path):
            for test, lines in compare.read(path).items():
                if wanted.search(test):
                    seen[test][compare.shape(lines, name)] += 1
    return seen


base = gather(glob.glob(os.path.join(base_root, "*", place, name)))
now = gather([os.path.join(compare.RESULTS, mutant, place, name)] + glob.glob(os.path.join(compare.RESULTS, mutant, "repeat", "*", place, name)))
for test in sorted(set(base) | set(now)):
    print(test[:100])
    for label, counter in (("unmutated", base.get(test, {})), ("mutant", now.get(test, {}))):
        for item, count in sorted(counter.items(), key=lambda kv: -kv[1]):
            if item is None:
                print(f"  {label} x{count}: (a drawn diagram: no shape read)")
                continue
            marker, calls = item
            print(f"  {label} x{count}: {marker} | {' '.join(c.replace('Request ', '') for c in calls if not c.startswith('Response'))[:260]}")
