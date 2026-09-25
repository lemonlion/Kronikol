"""What nesting costs: every scenario of each report rendered flat (today) and nested (the plan).

    python flow_bytes.py <report.json>...

Prints total and worst-case bytes, indented lines, the deepest level, and how many `inside`
references an unfiltered view needs (the plan expects none on a real report, section 2.4).
"""
import json, re, sys
from flow_prototype import render

total_flat = total_nested = refs = indented = scenarios = deepest = 0
worst = (0.0, "")
for path in sys.argv[1:]:
    with open(path, encoding="utf-8-sig") as f:
        data = json.load(f)
    count = sum(len(f.get("scenarios") or []) for f in data.get("features") or [])
    lane = path.replace("\\", "/").split("/tests/")[-1].split("/")[0]
    for ordinal in range(count):
        flat, nested = render(data, ordinal, "flat"), render(data, ordinal, "nested")
        scenarios += 1
        fb, nb = len(flat.encode()), len(nested.encode())
        total_flat += fb
        total_nested += nb
        if fb and (nb - fb) / fb > worst[0]:
            worst = ((nb - fb) / fb, f"{lane} s{ordinal}: {fb} -> {nb} B")
        refs += nested.count("  inside s")
        for line in nested.splitlines():
            m = re.match(r"^( *)s\d+/i\d+", line)
            if m:
                depth = (len(m.group(1)) - 2) // 2
                deepest = max(deepest, depth)
                indented += depth > 0
print(f"{scenarios} scenarios: flow bytes {total_flat:,} flat -> {total_nested:,} nested "
      f"({(total_nested - total_flat) / total_flat:+.1%})")
print(f"largest relative increase: {worst[0]:+.1%} ({worst[1]})")
print(f"indented lines {indented:,}, deepest level {deepest}, `inside` references in unfiltered views: {refs}")
