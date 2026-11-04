"""R3's round-trip fact, over dumps of dump.py's format: does every call's stepPath name a row the same scenario lists?

Usage: round_trip.py <dump.txt>...
Prints each call whose path is not listed, then a count per file. A call with no step (None) names nothing, so it is not counted.
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROW = re.compile(r"^\s+[-✗~·]?\s*(b?\d+(?:\.\d+)*)\s")
CALL = re.compile(r"^\s+s\d+/i\d+\s+(.*?)\s*-> (\S+)\s*$")

for path in sys.argv[1:]:
    calls = wrong = 0
    for block in open(path, encoding="utf-8").read().split("=== ")[1:]:
        lines = block.splitlines()
        rows = set()
        for line in lines[1:]:
            if CALL.match(line):
                continue
            match = ROW.match(line)
            if match:
                rows.add(match.group(1))
        for line in lines[1:]:
            match = CALL.match(line)
            if not match or match.group(2) == "None":
                continue
            calls += 1
            if match.group(2) not in rows:
                wrong += 1
                print(f"  {path.rsplit('/', 1)[-1]}: {lines[0][:60]}: {match.group(1)[:44]} -> {match.group(2)} (rows: {' '.join(sorted(rows))[:60]})")
    print(f"{path}: {calls} calls on a step, {wrong} naming a row the scenario does not list")
