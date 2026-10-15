"""Counts needles in a binary as UTF-8 and as UTF-16LE, and lists the UTF-16 #US literals holding '/_/' or a path."""
import re
import sys

BACKSLASH = chr(92)
needles = ["clone-a", "clone-b", "repro165", "scratchpad", "C:" + BACKSLASH + "Users", "/_/", "/_/IssueStack/",
           "IssueFeature.cs", "Assertion at line", "assertion at line"]
literal = re.compile(r"[\x20-\x7e]*(?:/_/|:" + re.escape(BACKSLASH) + r"Users)[\x20-\x7e]*")
for path in sys.argv[1:]:
    b = open(path, "rb").read()
    counts = []
    for n in needles:
        u8, u16 = b.count(n.encode()), b.count(n.encode("utf-16-le"))
        if u8 or u16:
            counts.append(f"{n!r} u8={u8} u16={u16}")
    print(f"== {path.replace(BACKSLASH, '/').split('/')[-1]} ({len(b)} bytes): " + ("; ".join(counts) or "none of the needles"))
    lits = set()
    for start in (0, 1):
        s = b[start:].decode("utf-16-le", errors="replace")
        for m in literal.finditer(s):
            lits.add(m.group(0))
    for l in sorted(lits):
        print(f"   utf16 literal: {l[:170]}")
