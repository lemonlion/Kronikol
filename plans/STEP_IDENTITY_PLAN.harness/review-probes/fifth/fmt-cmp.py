#!/usr/bin/env python
# For two output folders: per data file, is it the same once times and ids are masked, and if not, the masked lines that differ, bounded.
# Reads files by script and prints no more than a few short lines; HTML and the .krkl are compared by their lengths only.
import difflib, gzip, os, re, sys

MASKS = [
    (re.compile(r"\d{4}-\d\d-\d\dT[\d:.]+(Z|[+-]\d\d:\d\d)?"), "<time>"),
    (re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"), "<guid>"),
    (re.compile(r"\b\d\d:\d\d:\d\d(\.\d+)?\b"), "<span>"),
    (re.compile(r"[\"']?[A-Za-z]:[\\/]+[^\"'\s]*"), "<path>"),
]

def read(path):
    raw = open(path, "rb").read()
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.decompress(raw)
    text = raw.decode("utf-8", "replace")
    for pattern, to in MASKS:
        text = pattern.sub(to, text)
    return text.splitlines()

a, b = sys.argv[1], sys.argv[2]
limit = int(sys.argv[3]) if len(sys.argv) > 3 else 14
names = sorted(set(os.listdir(a)) | set(os.listdir(b)))
for name in names:
    pa, pb = os.path.join(a, name), os.path.join(b, name)
    if os.path.isdir(pa) or os.path.isdir(pb):
        continue
    if not os.path.exists(pa) or not os.path.exists(pb):
        print(f"ONLY {'first' if os.path.exists(pa) else 'second'}: {name}")
        continue
    if name.endswith(".krkl") or name.endswith(".cs") or name in ("CLAUDE.md", "AGENTS.md"):
        print(f"size {name}: {os.path.getsize(pa)} -> {os.path.getsize(pb)}")
        continue
    la, lb = read(pa), read(pb)
    if la == lb:
        print(f"SAME {name} ({len(la)} lines)")
        continue
    diff = [l for l in difflib.unified_diff(la, lb, lineterm="", n=0) if not l.startswith(("---", "+++", "@@"))]
    print(f"DIFF {name} ({len(la)} -> {len(lb)} lines, {len(diff)} changed)")
    for line in diff[:limit]:
        print("   ", line[:150])
    if len(diff) > limit:
        print(f"    ... {len(diff) - limit} more")
