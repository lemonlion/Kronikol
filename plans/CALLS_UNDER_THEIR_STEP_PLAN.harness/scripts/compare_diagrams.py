"""Compares each scenario's diagram source between two runs, ignoring what differs from run to run by nature."""
import pathlib
import re
import sys

a, b = map(pathlib.Path, sys.argv[1:3])


def norm(text):
    text = re.sub(r"customer-\d+", "customer-N", text)
    text = re.sub(r"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", "GUID", text)
    text = re.sub(r"00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}", "TRACEPARENT", text)
    text = re.sub(r"\b\d+(\.\d+)?\s*(ms|s)\b", "T", text)
    text = re.sub(r"\b\d{4}-\d{2}-\d{2}T[0-9:.]+Z?", "TS", text)
    return text


same = different = 0
for left in sorted(a.glob("*.puml"), key=lambda p: p.name):
    right = b / left.name
    x, y = norm(left.read_text(encoding="utf-8")).splitlines(), norm(right.read_text(encoding="utf-8")).splitlines()
    if x == y:
        same += 1
        continue
    different += 1
    first = next((i for i, (p, q) in enumerate(zip(x, y)) if p != q), min(len(x), len(y)))
    print(f"DIFF {left.name} lines {len(x)} -> {len(y)}, first at {first}")
    print(f"   published: {x[first][:110] if first < len(x) else ''}")
    print(f"   prototype: {y[first][:110] if first < len(y) else ''}")
print(f"same {same}, different {different}")
