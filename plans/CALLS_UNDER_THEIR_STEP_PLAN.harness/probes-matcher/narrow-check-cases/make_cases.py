"""New ingest cases for the narrow check of R2's compare. Usage: make_cases.py <out dir>"""
import os
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, r"C:/Code/Kronikol-225/plans/CALLS_UNDER_THEIR_STEP_PLAN.harness/probes-matcher/review-cases")
from lib import *  # noqa

R = sys.argv[1]
BS = "<U+005C><U+200B>"


def esc(t):
    return t.replace("\\", BS)


# --- A: a bar R2 reads for a row that did not draw it, where published reads no step. Row 0 draws nothing; the bar is row 1's.
def two_rows(name, kw0, t0, kw1, t1, flags=None):
    c = Case(R, name, flags)
    c.start("s", "t", 0)
    c.step("s", t0, kw0, 50)
    c.step("s", t1, kw1, 100)
    c.lbar("s", ((kw1 + " ") if kw1 else "") + esc(t1), 100)
    c.pair("s", "pay", 110)
    c.end("s", 500)
    c.write()


two_rows("A1-first-letter-under-a-keyword", "When", "pay C:\\now", "When", "Pay C:\\now")
two_rows("A2-another-keyword", "Given", "pay C:\\now", "When", "pay C:\\now")
two_rows("A3-no-keyword-row-under-a-keyword-bar", None, "pay C:\\now", "When", "pay C:\\now")
two_rows("A4-quote-first", None, '"pay" C:\\now', None, '"Pay" C:\\now')
two_rows("A5-digit-first", None, "3 items C:\\now", None, "3 Items C:\\now")
two_rows("A6-camelCase-first", None, "iPhone C:\\now", None, "IPhone C:\\now")
two_rows("A7-no-capitalise", None, "pay C:\\now", None, "Pay C:\\now", ["--no-capitalise"])
# The plain form of A2, A4: published's own check reads these, for the record.
two_rows("A2p-another-keyword-plain", "Given", "pay now", "When", "pay now")

# A8: a blank display line and a pipe in a WRAPPED FIRST line, which EscapeBodyLine never sees.
a = "C:\\" + "a" * 92  # escaped: 2 + 16 + 92 = 110 characters
b = a + "  |" + "b" * 109  # a doubled space at the wrap, and a word of 110 opening with a pipe
assert len(esc(a)) == 110
c = Case(R, "A8-wrapped-first-line-blank-and-pipe")
c.start("s", "t", 0)
c.step("s", a, "", 50)
c.step("s", b, "", 100)
c.bar("s", RICH + esc(a) + "\\n\\n|" + "b" * 109, 100)
c.pair("s", "pay", 110)
c.end("s", 500)
c.write()

# --- B: the walk's pass over an assertion's row, with a bar that has no row (a hook's).
c = Case(R, "B1-assertion-then-a-hook-bar-raw")
c.start("s", "t", 0)
c.assertion("s", "ready", 10)
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

c = Case(R, "B2-assertion-then-a-hook-bar-kind-step")
c.start("s", "t", 0)
c.assertion("s", "ready", 10)
c.step("s", "pay", "When", 100)
c.step("s", "ship", "When", 200)
c.kstep("s", "hook setup", None, 5)
c.pair("s", "hook", 7)
c.pair("s", "pay", 110)
c.pair("s", "ship", 210)
c.end("s", 500)
c.write()

# --- C: a bar that belongs to its row and that R2 does not read.
# C1: a cut exactly between a table name's line break and its pipe. A bar Build writes, for a step with two named tables.
words, lines, line = [], [], ""
target = 15955 - len("\\n\\nName:\\n")
text_words = []
while True:
    w = "word%04d" % len(text_words)
    if len(line) + 1 + len(w) > 110:
        lines.append(line)
        line = w
    else:
        line = (line + " " + w).strip()
    text_words.append(w)
    total = sum(len(x) + 2 for x in lines) + len(line)
    if total >= target - 12:
        break
# the last word takes up what is left
last = target - (sum(len(x) + 2 for x in lines) + len(line) + 1)
assert 1 <= last <= 40, last
tail = "z" * last
text_words.append(tail)
line = line + " " + tail
assert len(line) <= 110
lines.append(line)
label = "\\n".join(lines)
assert len(label) == target
c = Case(R, "C1-cut-after-a-table-name")
c.start("s", "t", 0)
c.step("s", " ".join(text_words), "", 100)
c.bar("s", RICH + label + "\\n\\nName:\\n\u2026", 100)
c.pair("s", "pay", 110)
c.end("s", 500)
c.write()

# C1b: the control, one character of the name fewer kept, so the cut falls inside the name.
c = Case(R, "C1b-cut-inside-a-table-name")
c.start("s", "t", 0)
c.step("s", " ".join(text_words), "", 100)
c.bar("s", RICH + label + "\\n\\nName:\u2026", 100)
c.pair("s", "pay", 110)
c.end("s", 500)
c.write()

# C2, C3: a row capitalised before the walk, under a raw bar that was not, whose first character is not a plain letter pair.
for name, text in (("C2-circled-letter", "\u24d0 is C:\\now"), ("C3-micro-sign", "\u00b5s budget C:\\now")):
    c = Case(R, name)
    c.start("s", "t", 0)
    c.step("s", text, None, 100)
    c.lbar("s", esc(text), 100)
    c.pair("s", "pay", 110)
    c.end("s", 500)
    c.write()

# C4: a keyword of two words, in another case on the bar.
c = Case(R, "C4-two-word-keyword-other-case")
c.start("s", "t", 0)
c.step("s", "C:\\now is read", "given that", 100)
c.lbar("s", "Given that " + esc("C:\\now is read"), 100)
c.pair("s", "pay", 110)
c.end("s", 500)
c.write()


# --- D: every text shape, keyword or none, on each kind of bar: all of these belong to their rows.
def capitalise(t):
    i = 0
    while i < len(t) and (t[i].isspace() or t[i] in "\u2713\u2717\u26a0\u2022-\u2013\u2014*"):
        i += 1
    if i >= len(t) or t[i] in "\"'([{\u201c\u2018\u00ab\u201e\u2039`":
        return t
    u = t[i].upper()
    if u == t[i]:
        return t
    j = i + 1
    while j < len(t):
        if t[j].isupper():
            return t
        if not (t[j].isalnum() or t[j] in "_."):
            break
        j += 1
    return t[:i] + u + t[i + 1:]


def wrapped(label):
    if len(label) <= 110:
        return LEGACY + label
    out, line = [], ""
    for w in label.split(" "):
        if line and len(line) + 1 + len(w) > 110:
            out.append(line)
            line = w
        else:
            line = (line + " " + w) if line else w
    out.append(line)
    return RICH + "\\n".join(out)


long_text = "pay " + " ".join("word%02d" % i for i in range(30)) + " C:\\now"
TEXTS = [
    ("plain", "pay C:\\now"), ("upper", "Pay C:\\now"), ("tick", "\u2713 pay C:\\now"), ("dash", "- pay C:\\now"),
    ("quote", '"pay" C:\\now'), ("digit", "3 items C:\\now"), ("camel", "iPhone C:\\now"), ("dotted", "page.goBack C:\\now"),
    ("loader", "<$pay> now"), ("builtin", "%date() pay"), ("long", long_text), ("accent", "\u00e9 pay C:\\now"),
    ("spaced", "  pay C:\\now"), ("twolines", "pay C:\\now\nand a second line"),
]


def esc_all(t):
    t = t.strip()
    t = t.replace("\\", BS).replace("<$", "~<$").replace("%date(", "<U+0025>date(")
    return t


def grid(name, flags):
    c = Case(R, name, flags)
    n = 0
    for short, text in TEXTS:
        for kw in (None, "When"):
            for source in ("tests", "kindstep", "rawasis", "rawcap"):
                tid = f"g{n}"
                n += 1
                call = f"{short}-{'kw' if kw else 'nokw'}-{source}"
                c.start(tid, call, 0)
                if source == "tests":
                    c.step(tid, text, kw, 100)
                elif source == "kindstep":
                    c.step(tid, text, kw, None)
                    c.kstep(tid, text, kw, 100)
                else:
                    c.step(tid, text, kw, 100)
                    body = capitalise(text) if source == "rawcap" else text
                    if short == "twolines":
                        first, second = body.split("\n")
                        lab = (((kw + " ") if kw else "") + esc_all(first))
                        c.bar(tid, RICH + lab + "\\n" + second, 100)
                    else:
                        c.bar(tid, wrapped(((kw + " ") if kw else "") + esc_all(body)), 100)
                c.pair(tid, call, 110)
                c.end(tid, 500)
    c.write()


grid("D1-grid-capitalise-on", [])
grid("D2-grid-no-capitalise", ["--no-capitalise"])
print("written")
