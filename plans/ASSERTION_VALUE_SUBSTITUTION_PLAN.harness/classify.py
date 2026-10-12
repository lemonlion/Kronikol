#!/usr/bin/env python3
"""Counts assertion labels by what their quoted values are (ASSERTION_VALUE_SUBSTITUTION_PLAN.md, section 2.3).

Reads `kronikol query assertions` output (or the probe's run output) from a file or stdin, one assertion per line,
and counts the shapes that can be told from the label's text alone:

  null-call     'null' before a call's brackets or type arguments: 'null'(), 'null'<int>()   (F1)
  value-call    another quoted value before a call's brackets: '2'(...)                        (F3, before R1)
  value-index   a quoted value before an index: '[ "x", "y" ]'[0]                              (F4)
  value-member  a quoted value before a member that is not called: 'Inner(v)'.Value            (F4, partial)
  value-method  a quoted value before a member that is called: 'Blue'.ToString()               (Q1's form after R1)
  bare-word     a quoted single identifier: a real value ('Blue') or a member's name ('Value'), which only the
                statement's source can tell apart (F5); listed for a look, not counted as a defect

Usage:  kronikol query assertions <reports-dir> > before.txt
        python classify.py before.txt
"""
import re
import sys
from collections import defaultdict

PATTERNS = [
    ("null-call", re.compile(r"'null'\s*[(<]")),
    ("value-call", re.compile(r"'[^']*'\s*\(")),
    ("value-index", re.compile(r"'[^']*'\[")),
    ("value-method", re.compile(r"'[^']*'\.[A-Za-z_][A-Za-z0-9_]*\s*[(<]")),
    ("value-member", re.compile(r"'[^']*'\.[A-Za-z_]")),
]
BARE_WORD = re.compile(r"'[A-Za-z_][A-Za-z0-9_]*'(?=\s*($|[),\]]))")


def classify(line):
    for name, pattern in PATTERNS:
        if pattern.search(line):
            return name
    return None


def main():
    text = open(sys.argv[1], encoding="utf-8").read() if len(sys.argv) > 1 else sys.stdin.read()
    lines = [l.strip() for l in text.splitlines() if l.strip().startswith(("✓", "✗"))]
    groups = defaultdict(list)
    bare = []
    for line in lines:
        group = classify(line)
        if group:
            groups[group].append(line)
        elif BARE_WORD.search(line):
            bare.append(line)
    quoted = sum(1 for l in lines if "'" in l)
    print(f"assertion lines: {len(lines)}, with a quoted value: {quoted}")
    for name, _ in PATTERNS:
        print(f"  {name:13} {len(groups[name])}")
    print(f"  {'bare-word':13} {len(bare)} (check against the source)")
    for name, _ in PATTERNS:
        for line in groups[name][:5]:
            print(f"    [{name}] {line}")
    for line in bare[:10]:
        print(f"    [bare-word] {line}")


if __name__ == "__main__":
    main()
