"""Rows that failed on the old code against the rows whose published read differs from R2's. Every other failure is listed for reading."""
import re, sys, pathlib
sys.stdout.reconfigure(encoding="utf-8")
here = pathlib.Path(sys.argv[1]).parent
lines = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace").splitlines()
tsv = {}
for line in (here / "cases.tsv").read_text(encoding="utf-8").splitlines():
    k, p, r = line.split("\t")
    tsv[k] = (p, r)
failed = set()
other = []
for line in lines:
    m = re.match(r"\s+Failed Kronikol\.Tests\.Ingestion\.StepWalkTests\.(\w+)(.*)", line)
    if m:
        n = re.search(r'name: "([^"]+)"', m.group(2))
        if n:
            failed.add(n.group(1))
        else:
            other.append(m.group(1) + m.group(2)[:60])
differ = {k for k, (p, r) in tsv.items() if p != r}
print("rows failed:", len(failed), " rows that differ:", len(differ))
print("failed but published read the same:", sorted(failed - differ))
print("differ but did not fail:", sorted(differ - failed))
print("other facts failed:", *other, sep="\n  ")
