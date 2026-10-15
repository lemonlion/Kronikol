"""Summarises `kronikol query assertions` output: notes, fallback labels, quoted values, one line per note."""
import re
import sys

text = open(sys.argv[1], encoding="utf-8").read()
lines = text.splitlines()
notes = []
for i, line in enumerate(lines):
    s = line.strip()
    if s.startswith(("✓", "✗")):
        where = ""
        if i + 1 < len(lines) and lines[i + 1].strip().startswith("at "):
            where = lines[i + 1].strip()[3:]
        label = re.sub(r"^[✓✗]\s+\S+\s+", "", s)
        notes.append((s[0], where, label))
fallback = [n for n in notes if re.match(r"(?i)assertion at line \d+", n[2])]
quoted = [n for n in notes if "'" in n[2]]
print(f"notes: {len(notes)}, fallback 'Assertion at line N': {len(fallback)}, with a quoted value: {len(quoted)}")
for mark, where, label in notes:
    print(f"  {mark} {where:28} {label}")
