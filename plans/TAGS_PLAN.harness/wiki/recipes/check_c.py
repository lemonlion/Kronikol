"""Marking-New-and-Changed-Tests.md's .NET recipe on a 5.0.0 build: tag a mergeable TestRunReport.json as its two jq
programs do (by source location, then by feature and scenario name), render each with kronikol merge, and ask the
result for --tag pr-. Usage: python -I check_c.py <dir holding Reports/>"""
import json
import os
import subprocess
import sys

TOOL = "C:/Code/Kronikol-tags/src/Kronikol.Tool/bin/Debug/net10.0/Kronikol.Tool.dll"
work = sys.argv[1]


def tool(*args):
    r = subprocess.run(["dotnet", TOOL, *args], cwd=work, capture_output=True, text=True, encoding="utf-8")
    if r.returncode != 0:
        print("EXIT", r.returncode, args, r.stdout[-600:], r.stderr[-600:])
    return r.stdout


def marks(changed, match):
    return [{"added": "pr-new", "edited": "pr-edited"}[t["change"]] for t in changed["tests"]
            if t["change"] in ("added", "edited") and match(t)]


def tagged(path):
    items = json.loads(tool("query", "scenarios", path, "--tag", "pr-", "--json"))["items"]
    return sorted((i["scenario"], tuple(i["tags"])) for i in items)


report = json.load(open(os.path.join(work, "Reports", "TestRunReport.json"), encoding="utf-8"))
scenario = report["features"][0]["scenarios"][0]
print("keys", sorted(k for k in scenario if k in ("name", "sourceFile", "sourceLine", "tags")),
      "feature name" if "name" in report["features"][0] else "NO FEATURE NAME")

# by source location, with $project = tests/Orders.Tests
project = "tests/Orders.Tests"
changed = {"tests": [{"path": "tests/Orders.Tests/Features/Orders.feature", "line": 20, "change": "added"}]}
by_location = json.loads(json.dumps(report))
for feature in by_location["features"]:
    for s in feature["scenarios"]:
        file = project + "/" + s["sourceFile"] if s.get("sourceFile") else ""
        line = s.get("sourceLine", -1)
        m = marks(changed, lambda t: t["path"] == file and t["line"] == line)
        if m:
            s["tags"] = sorted(set((s.get("tags") or []) + m))
json.dump(by_location, open(os.path.join(work, "tagged-run.json"), "w", encoding="utf-8"))
tool("merge", "tagged-run.json", "-o", "Tagged/TestRunReport.html", "-t", "Orders component tests")
print("C by location", tagged(os.path.join(work, "Tagged")))

# by name, for the adapters that carry no source location
changed = {"tests": [{"feature": "Orders", "scenario": "Place an order", "change": "edited"},
                     {"feature": "Catalogue", "scenario": "Browse", "change": "added"}]}
by_name = json.loads(json.dumps(report))
for feature in by_name["features"]:
    for s in feature["scenarios"]:
        m = marks(changed, lambda t: t.get("feature") == feature["name"] and t.get("scenario") == s["name"])
        if m:
            s["tags"] = sorted(set((s.get("tags") or []) + m))
json.dump(by_name, open(os.path.join(work, "tagged-by-name.json"), "w", encoding="utf-8"))
tool("merge", "tagged-by-name.json", "-o", "TaggedByName/TestRunReport.html")
print("C by name", tagged(os.path.join(work, "TaggedByName")))
