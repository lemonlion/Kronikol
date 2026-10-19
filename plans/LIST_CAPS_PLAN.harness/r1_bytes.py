"""Simulate R1's `failures` text for each lane from Failures.jsonl and the 4.26.9 text, and measure bytes per failure.
Usage: python -I r1_bytes.py <OUT folder repro.sh wrote>   (reads its recipe, messages and hooks lanes; writes nothing)"""
import json, re, sys
from pathlib import Path

run = Path(sys.argv[1])
CAP, HEAD = 6, 1
PREFIXES = {"harness": None, "ci": "/home/runner/work/shop/shop/tests/e2e/Reports/"}

def names(entries):
    seen = {}
    for a in entries:
        seen[a["name"]] = seen.get(a["name"], 0) + 1
    out, parts = "", []
    for n, c in seen.items():
        parts.append(n if c == 1 else f"{n} ×{c}")
    text = ""
    for i, p in enumerate(parts):
        cand = p if not text else text + ", " + p
        if len(cand) > 100:
            return text + ", …"
        text = cand
    return text

def lines_for(entries, indent, address, path_of):
    if len(entries) <= CAP:
        return [f"{indent}attachment: {a['name']} → {path_of(a)}" for a in entries]
    head, tail, cut = entries[:HEAD], entries[len(entries) - (CAP - HEAD):], entries[HEAD:len(entries) - (CAP - HEAD)]
    return ([f"{indent}attachment: {a['name']} → {path_of(a)}" for a in head]
            + [f"{indent}… {len(cut)} more attachment(s) ({names(cut)}): attachments {address}"]
            + [f"{indent}attachment: {a['name']} → {path_of(a)}" for a in tail])

for lane in ("recipe", "messages", "hooks"):
    folder = run / lane
    text = (folder / "failures.txt").read_text(encoding="utf-8")
    blocks, current = {}, None
    for line in text.splitlines():
        m = re.match(r"^(s\d+)  ", line)
        if m:
            current = m.group(1); blocks[current] = []
        elif current is not None and line:
            blocks[current].append(line)
        elif not line:
            current = None
    report_dir = str(folder / "report")
    for prefix_name, prefix in PREFIXES.items():
        def path_of(a, prefix=prefix):
            rel = a["path"]
            return str(Path(report_dir, rel)) if prefix is None else prefix + rel
        for record in (json.loads(l) for l in (folder / "Failures.jsonl").read_text(encoding="utf-8").splitlines() if l.strip()):
            if record.get("kind") != "failure":
                continue
            addr = record["address"]
            body = blocks[addr]
            kept = [l for l in body if "attachment:" not in l]
            own = [a for a in record.get("attachments", []) if "step" not in a]
            failing = {l.split()[1] for l in body if l.lstrip().startswith("✗ ")}
            on_failing = [a for a in record.get("attachments", []) if a.get("step") in failing]
            other = [a for a in record.get("attachments", []) if "step" in a and a.get("step") not in failing]
            # today: the step's first 6 (all here) + the scenario's first 4
            today = kept + [f"    attachment: {a['name']} → {path_of(a)}" for a in on_failing[:6]] \
                         + [f"  attachment: {a['name']} → {path_of(a)}" for a in own[:4]]
            r1 = kept + lines_for(on_failing, "    ", next(iter(failing), addr), path_of) + lines_for(own, "  ", addr, path_of)
            if other:
                r1.append(f"  … {len(other)} more attachment(s) on other steps ({names(other)}): attachments {addr}")
            head = f"{addr}  x"
            b_today = sum(len(l.encode()) + 1 for l in today) + len(head) + 2
            b_r1 = sum(len(l.encode()) + 1 for l in r1) + len(head) + 2
            print(f"{lane:8} {addr} {prefix_name:7} own {len(own):2} failing-step {len(on_failing)} other {len(other)}"
                  f" | today {b_today:5} B | R1 {b_r1:5} B | +{b_r1 - b_today} B | per 6,000 B: {6000 // b_today} -> {6000 // b_r1}")
            if prefix_name == "ci" and addr == "s0":
                for l in r1:
                    if "attachment" in l and "more" in l:
                        print("     ", l)
