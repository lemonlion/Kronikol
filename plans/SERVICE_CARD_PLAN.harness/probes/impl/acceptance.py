"""plans/SERVICE_CARD_PLAN.md 5.5 on real reports, with the tool built from the implementation.

Usage: python -I -X utf8 acceptance.py <Kronikol.Tool.dll> <scratchpad> <out-dir>

1. The ten same-commit pairs of 2.4: `kronikol card` on each side, and whether the two `behaviour` byte ranges are equal.
2. Every card: behaviourHash is the SHA-256 of the bytes from the `{` after the last `"behaviour":` to the byte before
   the file's closing `}` (4.3).
3. The 18 lanes of run 40491266394 against the corrected probe cards (compare_cards.py), outside GraphQL.
"""
import hashlib
import json
import os
import subprocess
import sys

tool, scratch, out = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(out, exist_ok=True)
here = os.path.dirname(os.path.abspath(__file__))


def card(report, name):
    target = os.path.join(out, name + ".service.json")
    p = subprocess.run(["dotnet", tool, "card", report, "-o", target], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if p.returncode != 0:
        raise SystemExit(f"card failed on {report}: {p.stderr.strip()}")
    return target


def behaviour(path):
    data = open(path, "rb").read()
    assert data.endswith(b"}\n"), path
    start = data.rindex(b'"behaviour":') + len(b'"behaviour":')
    return data[start:len(data) - 2], json.loads(data)["run"]["behaviourHash"], len(data)


def first(*paths):
    for p in paths:
        if os.path.exists(p):
            return p
    raise SystemExit("none of " + ", ".join(paths))


A = os.path.join(scratch, "bp", "40290999167")
B = os.path.join(scratch, "audit", "B", "downloads", "40473765203")
L = os.path.join(scratch, "audit", "B", "local", "out")
D = os.path.join(scratch, "decoded")
pairs = [(f"A-vs-B {lane}", first(os.path.join(A, f"{lane}-report"), os.path.join(D, f"{lane}.json")), os.path.join(B, f"{lane}-report"))
         for lane in ["bddfy-in-memory", "lightbdd-in-memory", "nunit-in-memory", "reqnroll-in-docker", "reqnroll-in-memory",
                      "tunit-in-memory", "xunit-external-sut", "xunit-in-memory"]]
pairs += [(f"L1-vs-L2 {lane}", os.path.join(L, f"{lane}-run1"), os.path.join(L, f"{lane}-run2")) for lane in ["reqnroll-in-memory", "xunit-in-memory"]]

lines = ["pair | equal behaviour | hash holds | sizes (bytes)"]
equal = 0
for name, a, b in pairs:
    slug = name.replace(" ", "-")
    ba, ha, sa = behaviour(card(a, slug + "-a"))
    bb, hb, sb = behaviour(card(b, slug + "-b"))
    holds = hashlib.sha256(ba).hexdigest() == ha and hashlib.sha256(bb).hexdigest() == hb
    equal += ba == bb
    lines.append(f"{name} | {'yes' if ba == bb else 'NO'} | {'yes' if holds else 'NO'} | {sa}, {sb}")
lines.append(f"equal on {equal} of {len(pairs)}")

lanes = os.path.join(scratch, "audit", "B", "downloads", "40491266394")
probes = os.path.join(scratch, "verifyE", "out3", "corrected")
lines.append("")
lines.append("lane of run 40491266394 | against the corrected probe card")
for entry in sorted(os.listdir(lanes)):
    if not entry.endswith("-report") or not os.path.isdir(os.path.join(lanes, entry)):
        continue
    lane = entry[:-len("-report")]
    probe = os.path.join(probes, f"C-{lane}.service.json")
    if not os.path.exists(probe):
        lines.append(f"{lane} | no probe card")
        continue
    mine = card(os.path.join(lanes, entry), "lane-" + lane)
    _, h, _ = behaviour(mine)
    p = subprocess.run([sys.executable, "-I", "-X", "utf8", os.path.join(here, "compare_cards.py"), mine, probe],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    lines.append(f"{lane} | {p.stdout.strip().splitlines()[0] if p.stdout.strip() else p.stderr.strip()[:200]}")

text = "\n".join(lines) + "\n"
open(os.path.join(out, "acceptance.txt"), "w", encoding="utf-8", newline="\n").write(text)
print(text)
