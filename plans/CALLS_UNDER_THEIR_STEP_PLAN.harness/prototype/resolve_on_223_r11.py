"""Settles the one conflict of this plan's layer on #223's prototype at c7252f6c: both changed StepMarkerMatches, for #128.

Usage: resolve_on_223_r11.py <worktree> [r2|theirs223]

r2, the default, keeps r2-alone.patch's matcher. theirs223 keeps that commit's, which loses four of the probe's 17 labels (results/matchers-compared.txt).
"""
import pathlib
import re
import sys

worktree, which = sys.argv[1], (sys.argv[2] if len(sys.argv) > 2 else "r2")
p = pathlib.Path(worktree) / "src/Kronikol/Reports/ReportGenerator.cs"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
block = re.compile(r"<<<<<<< ours\n(.*?)=======\n(.*?)>>>>>>> theirs\n", re.S)
found = block.findall(s)
assert len(found) == 1, len(found)
ours, theirs = found[0]
chosen = {"theirs223": ours, "r2": theirs}[which]
s = block.sub(lambda _: chosen, s)
p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("settled on c7252f6c:", which)
