"""Unfiltered `flow` of every scenario, old build against new, byte for byte. Prints counts and differing addresses."""
import concurrent.futures, json, subprocess, sys

old, new, reports = sys.argv[1], sys.argv[2], sys.argv[3:]
total = differing = 0
for report in reports:
    with open(report, encoding="utf-8-sig") as f:
        count = sum(len(feature.get("scenarios") or []) for feature in json.load(f).get("features") or [])

    def run(ordinal):
        a = subprocess.run([old, "query", "flow", report, f"s{ordinal}", "--max-bytes", "0"], capture_output=True, encoding="utf-8").stdout
        b = subprocess.run([new, "query", "flow", report, f"s{ordinal}", "--max-bytes", "0"], capture_output=True, encoding="utf-8").stdout
        return ordinal, a, b

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        for ordinal, a, b in pool.map(run, range(count)):
            total += 1
            if a != b:
                differing += 1
                print(f"DIFFERS {report.split('/')[-6]} s{ordinal}")
                for x, y in zip(a.split("\n") + [""] * 50, b.split("\n") + [""] * 50):
                    if x != y:
                        print(f"    old: {x[:140]}\n    new: {y[:140]}")
                        break
print(f"total: {total} scenarios, {differing} differ")
