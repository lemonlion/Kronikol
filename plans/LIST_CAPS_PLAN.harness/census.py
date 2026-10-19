"""For each lane repro.sh wrote, compare every failing scenario's attachments (Failures.jsonl lists them all) with
what each reader shows: `query failures` (text and --json), `query steps sN` and Failures.md.

Usage: python -I census.py <OUT folder repro.sh wrote>   (prints a markdown table; reads nothing else)
"""
import json
import re
import sys
from pathlib import Path

out = Path(sys.argv[1])
ATTACH = re.compile(r"^\s*attachment: (.+?) → ")
MORE = re.compile(r"^\s*… (\d+) more attachment")


def failures_text_blocks(text):
    """The `failures` text split per scenario: a block starts at a line `sN  Feature > Name`."""
    blocks, current = {}, None
    for line in text.splitlines():
        head = re.match(r"^(s\d+)  ", line)
        if head:
            current = head.group(1)
            blocks[current] = []
        elif current:
            blocks[current].append(line)
    return blocks


def digest_worked_through(md):
    """Addresses whose block Failures.md writes out in full, and how many attachment lines each block has."""
    worked, current = {}, None
    for line in md.splitlines():
        head = re.match(r"^`(s\d+)` · stableId", line)
        if head:
            current = head.group(1)
            worked[current] = 0
        elif line.startswith("## "):
            current = None
        elif current and re.match(r"^- .+ — `", line):
            worked[current] += 1
    return worked


print("| Lane | Scenario | In the data (own + steps) | `failures` text | said how many more | `failures --json` (own + steps) "
      "| `--json --out` | `steps sN` | Failures.md |")
print("|---|---|---|---|---|---|---|---|---|")
for lane in ("recipe", "messages", "hooks"):
    folder = out / lane
    if not folder.exists():
        continue
    digest = [record for record in (json.loads(line) for line in
              (folder / "Failures.jsonl").read_text(encoding="utf-8").splitlines() if line.strip())
              if record.get("kind") == "failure"]
    text = failures_text_blocks((folder / "failures.txt").read_text(encoding="utf-8"))
    envelope = json.loads((folder / "failures.json").read_text(encoding="utf-8"))
    unbudgeted = json.loads((folder / "failures.out.json").read_text(encoding="utf-8"))
    md = digest_worked_through((folder / "Failures.md").read_text(encoding="utf-8"))
    for record in digest:
        address = record["address"]
        own = [a for a in record.get("attachments", []) if "step" not in a]
        stepped = [a for a in record.get("attachments", []) if "step" in a]
        lines = text.get(address, [])
        shown = sum(1 for line in lines if ATTACH.match(line))
        more = sum(int(MORE.match(line).group(1)) for line in lines if MORE.match(line))
        item = next((i for i in envelope["items"] if i["address"] == address), None)
        item_out = next((i for i in unbudgeted["items"] if i["address"] == address), None)

        def json_counts(i):
            if i is None:
                return "record absent"
            return f"{len(i['attachments'])} + {sum(len(s.get('attachments', [])) for s in i['failingSteps'])}"

        steps_file = folder / f"steps-{address}.txt"
        steps_shown = (sum(1 for line in steps_file.read_text(encoding="utf-8").splitlines() if ATTACH.match(line))
                       if steps_file.exists() else "not run")
        in_md = f"{md[address]} (worked through)" if address in md else "table row, none"
        print(f"| {lane} | {address} | {len(own)} + {len(stepped)} | {shown} | {more if more else 'nothing'} "
              f"| {json_counts(item)} | {json_counts(item_out)} | {steps_shown} | {in_md} |")
