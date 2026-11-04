"""Two versions of #226's patch, file by file: which files' diffs differ, and by how many lines. Usage: compare_226_patches.py <old patch> <new patch>"""
import difflib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")


def files(path):
    text = open(path, encoding="utf-8", newline="").read().replace("\r\n", "\n")
    out = {}
    for part in re.split(r"(?m)^(?=diff --git )", text):
        if part.startswith("diff --git"):
            name = part.split("\n", 1)[0].split(" b/")[-1]
            # Hunk headers and index lines move with any earlier change, so they are left out.
            out[name] = [line for line in part.split("\n") if not line.startswith(("@@", "index "))]
    return out


old, new = files(sys.argv[1]), files(sys.argv[2])
for name in sorted(set(old) | set(new)):
    a, b = old.get(name, []), new.get(name, [])
    if a == b:
        print(f"same      {name}")
        continue
    changed = [line for line in difflib.ndiff(a, b) if line[:2] in ("- ", "+ ")]
    print(f"differs   {name}: {len(changed)} lines")
    if name.endswith(("StepWeaver.cs", "StepCollector.cs")):
        for line in changed[:40]:
            print("    " + line[:150])
