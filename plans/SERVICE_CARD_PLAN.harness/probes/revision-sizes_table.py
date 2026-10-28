"""The revised card's sizes per report, from revision-card-revised.cs's written files: the whole file compact and indented, each
gzipped at level 9 (Python's zlib, mtime 0), and the parts the probe printed. Usage:
python -I -X utf8 revision-sizes_table.py <sizes-revised.txt> <cards dir>"""
import gzip, re, sys
from pathlib import Path
text = Path(sys.argv[1]).read_text(encoding="utf-8")
cards = Path(sys.argv[2])
blocks = re.split(r"^== ", text, flags=re.M)[1:]
print(f"{'report':26} {'ops':>4} {'compact':>8} {'gz':>6} {'indented':>9} {'gz':>6} | {'behaviour':>9} {'no index':>8} {'run':>6} | {'b.index':>7} {'r.index':>7} {'counts':>6} {'examples':>8}")
for b in blocks:
    name = b.splitlines()[0].strip()
    ops = re.search(r"; ops (\d+);", b).group(1)
    sizes = re.search(r"file compact (\d+) indented (\d+); behaviour compact (\d+) indented \d+; behaviour without its index compact (\d+) indented \d+; run compact (\d+)", b)
    parts = re.search(r"run.counts (\d+), run.examples (\d+), run.scenarios (\d+), run.parties \d+; behaviour.scenarios (\d+)", b)
    fc, fi, bc, bn, rc = (int(x) for x in sizes.groups())
    counts, examples, rindex, bindex = (int(x) for x in parts.groups())
    gc = len(gzip.compress((cards / f"{name}.service.json").read_bytes(), 9, mtime=0))
    gi = len(gzip.compress((cards / f"{name}.service.indented.json").read_bytes(), 9, mtime=0))
    print(f"{name:26} {ops:>4} {fc:>8} {gc:>6} {fi:>9} {gi:>6} | {bc:>9} {bn:>8} {rc:>6} | {bindex:>7} {rindex:>7} {counts:>6} {examples:>8}")
