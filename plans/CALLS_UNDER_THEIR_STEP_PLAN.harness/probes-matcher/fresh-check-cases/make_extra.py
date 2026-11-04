"""Extra cases, written by this check, each one ingest. Usage: make_extra.py <out dir>"""
import sys, os
sys.path.insert(0, r"C:/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness/probes-matcher/review-cases")
sys.dont_write_bytecode = True
from lib import *
R = sys.argv[1]

# N1: a multi-word keyword on an escaped label, tests file draws: only the row's own keyword can read this bar.
c = Case(R, "N1-two-word-keyword-escaped")
c.start("s", "t", 0); c.step("s", r"the file C:\temp\new is read", "Given that", 100); c.pair("s", "read", 110); c.step("s", "it is paid", "When", 200); c.pair("s", "pay", 210); c.end("s", 500); c.write()

# N2: a line break right after a row's label with no cut: row A has no bar, its text the first two display lines of row B's wrapped label.
words = [f"word{i}" for i in range(60)]
line, lines = "", []
for w in ["When"] + words:
    if len(line) + 1 + len(w) > 110:
        lines.append(line); line = w
    else:
        line = (line + " " + w).strip()
lines.append(line)
label = "\\n".join(lines)
a_text = " ".join(" ".join(lines[:2]).split(" ")[1:])
b_text = " ".join(words)
c = Case(R, "N2-line-break-after-label-no-cut")
c.start("s", "t", 0); c.step("s", a_text, "When", 50); c.step("s", b_text, "When", 100)
c.bar("s", RICH + label, 100); c.pair("s", "pay", 110); c.end("s", 500); c.write()

# N3: J15's shape with no escape in the text: published's own check reads it, case aside.
c = Case(R, "N3-case-only-plain")
c.start("s", "t", 0); c.step("s", "pay now", "When", 50); c.step("s", "PAY NOW", "When", 100)
c.lbar("s", "When PAY NOW", 100); c.pair("s", "pay", 110); c.end("s", 500); c.write()
