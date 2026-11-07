"""The cases whose note the #213 fix decides: every case where the prototype's first-round note differs from main's
(a crash on main, or another label), as C# tuples for CapturedFieldValueTests' DecidedNotes.

Usage: python -I gen_notes_table.py <case file of main> <case file of the prototype> <out .cs fragment>
"""
import sys
from pathlib import Path


def first_round(path):
    cases = {}
    for line in Path(path).read_bytes().decode("utf-8").split("\n"):
        if not line.startswith("R1 "):
            continue
        key, rest = line[3:].split(" :: ", 1)
        cases[key] = rest
    return cases


def cs(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


main, proto = first_round(sys.argv[1]), first_round(sys.argv[2])
assert set(main) == set(proto), "the two runs hold different cases"
rows = []
for key in sorted(proto):
    if main[key] == proto[key]:
        continue
    parts = proto[key].split(" :: ")
    assert parts[0] == "unwoven=nothing" and parts[1] == "woven=nothing", (key, parts[:2])
    assert parts[2].startswith("note="), (key, parts[2])
    shape, build, entry = key.split("|")
    rows.append(f"        ({cs(shape)}, BuildConfiguration.{build}, {cs(entry)}, {cs(parts[2][len('note='):])}),")
Path(sys.argv[3]).write_bytes(("\n".join(rows) + "\n").encode("utf-8"))
print(f"{len(rows)} decided cases")
