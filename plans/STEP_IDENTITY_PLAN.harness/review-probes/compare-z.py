#!/usr/bin/env python
# Compares two read-z.sh outputs by scenario title: rows, and calls by step and path. usage: compare-z.py <left> <right> [chars]. Prints the differences and a count.
import re, sys

sys.stdout.reconfigure(encoding="utf-8")
left, right = sys.argv[1], sys.argv[2]
limit = int(sys.argv[3]) if len(sys.argv) > 3 else 400


def scenarios(path):
    text = open(path, encoding="utf-8", errors="replace").read()
    found = {}
    for part in re.split(r"^### ", text, flags=re.M)[1:]:
        head, _, body = part.partition("\n")
        if not re.match(r"s\d+ steps", head):
            continue
        rows, calls, title, in_calls = [], [], None, False
        for line in body.splitlines():
            if line.startswith("--- "):
                in_calls = True
                continue
            if not line.strip() or re.match(r"^(!|next:|\d+ calls? ·|attachments?:|step )", line):
                continue
            if in_calls:
                cols = line.split()
                if len(cols) >= 3:
                    calls.append(cols[0] + " " + cols[2])
            elif title is None and re.match(r"^s\d+\s", line):
                title = re.sub(r"\s+\d+(\.\d+)?m?s\s*$", "", re.sub(r"^s\d+\s+", "", line))
            else:
                line = re.sub(r"\[i\d+(-i\d+)?\]( \d+ calls)?", "", line)
                line = re.sub(r"\s+\d+(\.\d+)?s\b", "", line)
                rows.append(re.sub(r"(attachment: \S+).*", r"\1", line).rstrip())
        if title:
            found[title] = (rows, sorted(calls))
    return found


a, b = scenarios(left), scenarios(right)
same = 0
for key in sorted(set(a) | set(b)):
    if a.get(key) == b.get(key):
        same += 1
        continue
    x, y = a.get(key, (["(absent)"], [])), b.get(key, (["(absent)"], []))
    print(f"=== {key}")
    if x[0] != y[0]:
        print("  LEFT  " + " | ".join(s.strip() for s in x[0])[:limit])
        print("  RIGHT " + " | ".join(s.strip() for s in y[0])[:limit])
    if x[1] != y[1]:
        print("  LEFT calls  " + " / ".join(x[1])[:limit])
        print("  RIGHT calls " + " / ".join(y[1])[:limit])
print(f"{same} scenarios the same, {len(set(a) | set(b)) - same} differ")
