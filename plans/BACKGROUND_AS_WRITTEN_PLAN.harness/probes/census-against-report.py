#!/usr/bin/env python
"""Where a report's rows and corpus-backgrounds.py's count of a suite's feature files part: census-against-report.py <kronikol> <reports> <features-folder>

Per source file: the report's rows, against one row for a Scenario and one for each data row under an outline's Examples. Prints the files that differ and what each writes.
"""
import collections
import json
import pathlib
import re
import subprocess
import sys

kronikol, report, folder = sys.argv[1:4]
root = pathlib.Path(folder)


def query(*args):
    done = subprocess.run([kronikol, "query", *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    return done.stdout


rows = collections.Counter()
titles = collections.defaultdict(collections.Counter)
offset = 0
while True:
    page = json.loads(query("scenarios", report, "--json", "--limit", "200", "--max-bytes", "2000000", "--offset", str(offset)))
    for item in page["items"]:
        source = (item.get("sourceFile") or "").replace("\\", "/")
        rows[source] += 1
        titles[source][item["scenario"]] += 1
    following = page.get("next")
    if not following or "--offset" not in following:
        break
    offset = int(following[following.index("--offset") + 1])

written = {}
for path in sorted(root.rglob("*.feature")):
    count, in_examples, header_seen = 0, False, False
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if re.match(r"(Scenario Outline|Scenario Template):", line):
            in_examples = False
        elif re.match(r"(Scenario|Example):", line):
            count += 1
            in_examples = False
        elif re.match(r"(Examples|Scenarios):", line):
            in_examples, header_seen = True, False
        elif in_examples and line.startswith("|"):
            if header_seen:
                count += 1
            header_seen = True
        elif in_examples and line and not line.startswith(("#", "@")):
            in_examples = False
    written[str(path.relative_to(root.parent)).replace("\\", "/")] = count

print(f"report rows: {sum(rows.values())}; rows the files write, by this count: {sum(written.values())}; files: {len(written)}")
for source in sorted(set(rows) | set(written)):
    if rows.get(source, 0) != written.get(source, 0):
        print(f"{source}: report {rows.get(source, 0)}, written {written.get(source, 0)}")
        for title, n in titles[source].most_common(6):
            print(f"    {n} x {title[:90]}")
