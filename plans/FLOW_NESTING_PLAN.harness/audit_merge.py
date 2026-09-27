"""The audit of 2026-09-27 (plan section 7.7): does `kronikol merge` keep what rule R4 reads?

    python audit_merge.py <kronikol.exe> <generated TestRunReport.json> <out-dir>

Takes the report `audit_properties.py` generated, writes its ids and traces as GUIDs (the form every Kronikol
writer uses, and the one the merge reader parses), splits it into two mergeable shards as two runners would
write them, merges them with the real tool, and compares `flow` on every scenario of the source and of the
merged file, unfiltered and with `--service db`. Scenarios are matched by name and their addresses mapped, since
a merge may order them differently. A view that differs is classed by whether its nesting differs (the
indentation and `inside` of each call) and by whether every changed line is a request with no pairing id.
Everything is written under <out-dir>, outside the repository.
"""
import json, os, re, subprocess, sys, uuid
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

CALL = re.compile(r"^( *)sX/i(\d+) ")


def main():
    tool, generated, out = sys.argv[1:4]
    report = json.load(open(generated, encoding="utf-8"))
    for feature in report["features"]:
        for scenario in feature["scenarios"]:
            for record in scenario["httpInteractions"]:
                for key in ("requestResponseId", "traceId"):
                    if record.get(key):
                        record[key] = str(uuid.uuid5(uuid.NAMESPACE_URL, record[key]))

    for folder in ("source", "shards", "merged"):
        os.makedirs(os.path.join(out, folder), exist_ok=True)
    source = os.path.join(out, "source", "TestRunReport.json")
    json.dump(report, open(source, "w", encoding="utf-8"))
    report["mergeableFormatVersion"] = 1
    scenarios = report["features"][0]["scenarios"]
    half = len(scenarios) // 2
    for name, part in (("a", scenarios[:half]), ("b", scenarios[half:])):
        shard = dict(report, features=[dict(report["features"][0], scenarios=part)])
        json.dump(shard, open(os.path.join(out, "shards", f"{name}.json"), "w", encoding="utf-8"))

    merged_html = os.path.join(out, "merged", "TestRunReport.html")
    subprocess.run([tool, "merge", os.path.join(out, "shards"), "-o", merged_html], check=True, capture_output=True)
    merged = os.path.join(out, "merged", "TestRunReport.json")

    def by_name(path):
        data = json.load(open(path, encoding="utf-8"))
        every = [s for f in data["features"] for s in f["scenarios"]]
        return {s["name"]: (n, s) for n, s in enumerate(every)}

    before, after = by_name(source), by_name(merged)
    assert set(before) == set(after), (len(before), len(after))

    def flow(path, n, view):
        text = subprocess.run([tool, "query", "flow", path, f"s{n}", "--max-bytes", "0", *view],
                              capture_output=True, text=True, encoding="utf-8").stdout
        return [re.sub(rf"\bs{n}(?=[/ ])", "sX", line) for line in text.split("\n") if not line.startswith("! ")]

    def nesting(lines):
        shape = []
        for line in lines:
            m = CALL.match(line)
            if m:
                inside = re.search(r"inside sX/i(\d+)$", line)
                shape.append((m.group(2), len(m.group(1)), inside.group(1) if inside else None))
        return shape

    jobs = [(name, view) for name in before for view in ([], ["--service", "db"])]
    with ThreadPoolExecutor(8) as pool:
        results = list(pool.map(lambda j: (j, flow(source, before[j[0]][0], j[1]), flow(merged, after[j[0]][0], j[1])), jobs))

    kinds = Counter()
    nested = 0
    for (name, view), a, b in results:
        nested += any("indented calls ran inside" in line for line in a)
        if a == b:
            continue
        records = before[name][1]["httpInteractions"]
        changed = [int(CALL.match(x).group(2)) for x, y in zip(a, b) if x != y and CALL.match(x)]
        no_id = all(not records[i].get("requestResponseId") for i in changed)
        kinds[(nesting(a) != nesting(b), no_id)] += 1

    differ = sum(kinds.values())
    print(f"{len(before)} scenarios merged from two shards; {len(results)} views, unfiltered and --service db, "
          f"{nested} of them nested")
    print(f"{differ} views differ after the merge")
    for (nest, no_id), count in sorted(kinds.items()):
        print(f"  {count:>5}  nesting differs: {'yes' if nest else 'no'}; every changed line a request with no pairing id: "
              f"{'yes' if no_id else 'no'}")


if __name__ == "__main__":
    main()
