"""A mutant's verdict, the tests it changed (names only), and the rows of those matching a regex.
Usage: show.py <id> [<regex of tests to print>] [--files <regex of dump file names>]"""
import os
import re
import sys

from dumps import MUT

mutant = sys.argv[1]
wanted = re.compile(sys.argv[2]) if len(sys.argv) > 2 and sys.argv[2] else None
path = os.path.join(MUT, "results", mutant, "diff.txt")
with open(path, encoding="utf-8") as handle:
    lines = handle.read().splitlines()
print(mutant, lines[0])
blocks, current = [], None
for line in lines[3:]:
    if line.startswith("["):
        current = [line]
        blocks.append(current)
    elif current is not None:
        current.append(line)
names = []
for block in blocks:
    match = re.match(r"\[(.*?)\] (\S*):? ?(SOLID|ORDER|RACING-NEW|racing, shape seen|NEW TEST OUTPUT|TEST GONE)? ?(.*)", block[0])
    if not match:
        names.append(block[0][:120])
        continue
    place, dump, kind, test = match.groups()
    short = test.rsplit(".", 1)[-1].split("_")[0] if "." in test else " ".join(test.split()[:1])
    tag = {"SOLID": "", "ORDER": "(o)", "RACING-NEW": "(r!)", "racing, shape seen": "(r)"}.get(kind, f"({kind})")
    suffix = "uml" if ".plantuml." in dump else ("off" if "separate-off" in dump else "")
    names.append(f"{place.split('/')[-1]}:{short}{tag}{':' + suffix if suffix else ''}")
print(" ".join(names)[:1600])
if wanted:
    for block in blocks:
        if wanted.search(block[0]):
            print(block[0][:150])
            for row in block[1:][:26]:
                print("   ", row.strip()[:140])
