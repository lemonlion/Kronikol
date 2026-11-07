"""The values the fix must record: from a values pass, every `name = Harness(name) [the `name` argument]` and every
`count`/`total` = 7 (P19), as C# tuples (case, "name = identity") for CapturedFieldValueTests' Recorded table.

Usage: python -I gen_presence_table.py <values file> <out .cs fragment>
"""
import re
import sys
from pathlib import Path

rows, case = [], None
for line in Path(sys.argv[1]).read_bytes().decode("utf-8", "replace").split("\n"):
    if line and not line.startswith(" ") and not line.startswith("#"):
        case = line.split(" :: ", 1)[0]
        continue
    s = line.strip()
    if not s.startswith("VALUES "):
        continue
    found = set()
    for m in re.finditer(r"(\w+) = (Harness\((\w+)\) \[the `(\w+)` argument\]|int 7|long 7)", s):
        name, identity = m.group(1), m.group(2)
        if identity.startswith("Harness(") and not (m.group(3) == name == m.group(4)):
            continue
        found.add(f"{name} = {identity}")
    shape, build, entry = case.split("|")
    for pair in sorted(found):
        rows.append(f'        ("{shape}", BuildConfiguration.{build}, "{entry}", "{pair.replace(chr(96), chr(96))}"),')
Path(sys.argv[2]).write_bytes(("\n".join(rows) + "\n").encode("utf-8"))
print(len(rows), "recorded values to pin")
