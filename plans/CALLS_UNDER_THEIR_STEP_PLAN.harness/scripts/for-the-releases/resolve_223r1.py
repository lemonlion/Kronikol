"""Resolves R1's layer over #223's R1: the completion's message comes from MessageOf, and the walk keeps fe's latestOpen.

Usage: resolve_223r1.py <worktree>
"""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])


def hunks(text):
    return re.compile(r"<<<<<<<[^\n]*\n(.*?)(?:\|\|\|\|\|\|\|[^\n]*\n.*?)?=======\n(.*?)>>>>>>>[^\n]*\n", re.S).sub


p = root / "src/Kronikol/Tracking/StepCollector.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
s, n = re.subn(r"<<<<<<<[^\n]*\n            CompleteStep\(false, MessageOf\(ex\)\);\n(?:\|\|\|\|\|\|\|[^\n]*\n.*?)?=======\n            CompleteAwaited\(step, false, ex\.Message\);\n>>>>>>>[^\n]*\n",
               "            CompleteAwaited(step, false, MessageOf(ex));\n", s, flags=re.S)
assert n == 2, n
assert "<<<<<<<" not in s
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))

p = root / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
matches = list(re.finditer(r"<<<<<<<[^\n]*\n(.*?)(?:\|\|\|\|\|\|\|[^\n]*\n.*?)?=======\n(.*?)>>>>>>>[^\n]*\n", s, re.S))
assert len(matches) == 2, len(matches)
for m in reversed(matches):
    ours, theirs = m.group(1), m.group(2)
    if "latestOpen" in ours:
        keep = ours
    else:
        assert '["stepPath"]' in ours and '["stepPath"]' in theirs
        print("fe's schema text:", re.search(r'\["description"\] = "([^"]*)"', ours).group(1))
        print("mine:", re.search(r'\["description"\] = "([^"]*)"', theirs).group(1))
        keep = theirs
    s = s[:m.start()] + keep + s[m.end():]
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("resolved")
