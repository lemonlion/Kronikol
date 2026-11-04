"""Each unmutated round against all the other rounds of cut 9: what differs here differs with no mutation."""
import glob
import os
import sys

import compare

rounds = sorted(os.path.basename(p) for p in glob.glob(os.path.join(compare.RESULTS, compare.BASELINE["root"], "*")))
orig = compare.baseline_dirs
for round_no in rounds:
    compare.baseline_dirs = lambda family, suite, r=round_no: (orig(family, suite)[0], [d for d in orig(family, suite)[1] if os.path.basename(os.path.dirname(d)) != r])
    verdict = compare.Verdict()
    report = []
    out = os.path.join(compare.RESULTS, compare.BASELINE["root"], round_no)
    for folder in sorted(glob.glob(os.path.join(out, "*-*"))):
        family, suite = os.path.basename(folder).split("-", 1)
        compare.compare_suite(family, suite, folder, verdict, report)
    print(f"round {round_no}: solid={verdict.solid} order={verdict.order} racing-new={verdict.racing_new} racing-seen={verdict.racing_seen}")
    for line in report:
        if "racing, shape seen" not in line and not line.startswith("      "):
            print("  " + line[:170])
