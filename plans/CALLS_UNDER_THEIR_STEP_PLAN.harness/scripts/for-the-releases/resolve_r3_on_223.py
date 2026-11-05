"""Resolves R3's layer over R1's on #223's R1 and 5.29.0.

Usage: resolve_r3_on_223.py <worktree>
"""
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
PATTERN = re.compile(r"<<<<<<<[^\n]*\n(.*?)(?:\|\|\|\|\|\|\|[^\n]*\n.*?)?=======\n(.*?)>>>>>>>[^\n]*\n", re.S)


def load(path):
    raw = (root / path).read_bytes().decode("utf-8")
    return raw.replace("\r\n", "\n"), "\r\n" in raw


def save(path, s, crlf):
    assert "<<<<<<<" not in s, path
    (root / path).write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


s, crlf = load("src/Kronikol/Reports/ReportGenerator.cs")


def choose(m):
    ours, theirs = m.group(1), m.group(2)
    if "var read = current;" in ours:
        return ("                    // The step the call was logged under, where this scenario's steps carry their ids. With no stamp,\n"
                "                    // or no ids to go by, the step before it. A record a hold let through late names a step already passed and moves\n"
                "                    // nothing, so what follows it reads as it would have.\n"
                "                    var read = current;\n")
    assert '["stepPath"]' in ours
    merged = ours.replace("prefixed b for a background step (b0, 0, 1, ...). Null", "prefixed b for a background step (b0, 0, 1, ...), dotted for a sub-step (0.1). Null")
    assert merged != ours
    return merged


s = PATTERN.sub(choose, s)
save("src/Kronikol/Reports/ReportGenerator.cs", s, crlf)

s, crlf = load("src/Kronikol/Tracking/StepCollector.cs")
s, n = PATTERN.subn(lambda m: m.group(1) + "\n" + m.group(2), s)
assert n == 1, n
save("src/Kronikol/Tracking/StepCollector.cs", s, crlf)
print("resolved")
