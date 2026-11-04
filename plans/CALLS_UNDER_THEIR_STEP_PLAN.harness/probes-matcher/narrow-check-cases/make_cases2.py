"""The second batch of cases. Usage: make_cases2.py <out dir>"""
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, r"C:/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness/probes-matcher/review-cases")
from lib import *  # noqa

R = sys.argv[1]

# B3: the hook's bar between the two steps, an assertion's row first.
c = Case(R, "B3-assertion-then-a-hook-bar-between")
c.start("s", "t", 0)
c.assertion("s", "ready", 10)
c.step("s", "pay", "When", 100)
c.step("s", "ship", "When", 200)
c.lbar("s", "When pay", 100)
c.pair("s", "pay", 110)
c.lbar("s", "hook setup", 150)
c.pair("s", "hook", 160)
c.lbar("s", "When ship", 200)
c.pair("s", "ship", 210)
c.end("s", 500)
c.write()

# B4: the same bars with no assertion's row: published and R2 should agree, so B1's difference is the pass-over alone.
c = Case(R, "B4-control-no-assertion")
c.start("s", "t", 0)
c.step("s", "pay", "When", 100)
c.step("s", "ship", "When", 200)
c.lbar("s", "hook setup", 5)
c.pair("s", "hook", 7)
c.lbar("s", "When pay", 100)
c.pair("s", "pay", 110)
c.lbar("s", "When ship", 200)
c.pair("s", "ship", 210)
c.end("s", 500)
c.write()

# B5: an assertion's row and a bar with no row at all: the walk runs off the rows.
c = Case(R, "B5-only-an-assertion-row")
c.start("s", "t", 0)
c.assertion("s", "ready", 10)
c.lbar("s", "hook setup", 5)
c.pair("s", "hook", 7)
c.end("s", 500)
c.write()

# E1: a cut bar with nothing else changed: no escape and no wrap, as a word holding `<` is not split. Rows apart only past the cut.
base = "<b>" + "a" * 700
c = Case(R, "E1-past-a-cut-no-escape")
c.start("s", "t", 0)
c.step("s", base + "X", "", 50)
c.step("s", base + "Y", "", 100)
kept = (base + "Y")[: 549 - 1]
c.lbar("s", kept + "\u2026", 100)
c.pair("s", "pay", 110)
c.end("s", 500)
c.write()

# A9: the first letter on a wrapped label, with a keyword: rows apart only there, and the label is not escaped at all.
long_tail = " ".join("word%02d" % i for i in range(30))
c = Case(R, "A9-first-letter-wrapped-no-escape")
c.start("s", "t", 0)
c.step("s", "pay " + long_tail, "When", 50)
c.step("s", "Pay " + long_tail, "When", 100)
line, lines = "", []
for w in ("When Pay " + long_tail).split(" "):
    if line and len(line) + 1 + len(w) > 110:
        lines.append(line)
        line = w
    else:
        line = (line + " " + w) if line else w
lines.append(line)
c.bar("s", RICH + "\\n".join(lines), 100)
c.pair("s", "pay", 110)
c.end("s", 500)
c.write()
print("written")
