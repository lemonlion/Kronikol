"""Marking-New-and-Changed-Tests.md's recipes on a 5.0.0 build: each jq program's edit done in Python with jq's semantics
(`unique` sorts), then the tool reads the result. A: tests NDJSON + ingest. B: Cucumber Messages + ingest. Each ends with
`query scenarios --tag pr- --json`. Usage: python -I check.py <workdir>"""
import json
import os
import shutil
import subprocess
import sys

TOOL = "C:/Code/Kronikol-tags/src/Kronikol.Tool/bin/Debug/net10.0/Kronikol.Tool.dll"
FIXTURE = "C:/Code/Kronikol-tags/tests/Kronikol.Tests/TestData/Cucumber/playwright-bdd-9.2-messages.ndjson"
work = sys.argv[1]
shutil.rmtree(work, ignore_errors=True)
os.makedirs(work)


def tool(*args):
    r = subprocess.run(["dotnet", TOOL, *args], cwd=work, capture_output=True, text=True, encoding="utf-8")
    if r.returncode != 0:
        print("EXIT", r.returncode, args, r.stdout[-800:], r.stderr[-800:])
    return r.stdout


def marks(changed, match):
    out = []
    for t in changed["tests"]:
        if match(t):
            if t["change"] == "added":
                out.append("pr-new")
            elif t["change"] == "edited":
                out.append("pr-edited")
    return out


def tagged(path):
    items = json.loads(tool("query", "scenarios", path, "--tag", "pr-", "--json"))["items"]
    return sorted((i["scenario"], tuple(i["tags"])) for i in items)


# A: the tests NDJSON lane
changed_a = {"tests": [
    {"path": "src/orders/orders.test.ts", "line": 40, "end": 58, "change": "edited"},
    {"path": "src/orders/refunds.test.ts", "line": 12, "end": 30, "change": "added"}]}
records = [
    {"event": "start", "testId": "t1", "testName": "Places an order", "feature": "Orders",
     "sourceFile": "src/orders/orders.test.ts", "sourceLine": 45, "tags": ["smoke"]},
    {"event": "end", "testId": "t1", "status": "passed", "durationMs": 5},
    {"event": "start", "testId": "t2", "testName": "Refunds an order", "feature": "Orders",
     "sourceFile": "src/orders/refunds.test.ts", "sourceLine": 15},
    {"event": "end", "testId": "t2", "status": "passed", "durationMs": 5},
    {"event": "start", "testId": "t3", "testName": "Browses the catalogue", "feature": "Orders",
     "sourceFile": "src/orders/browse.test.ts", "sourceLine": 5},
    {"event": "end", "testId": "t3", "status": "passed", "durationMs": 5}]
cap = os.path.join(work, "kronikol-capture")
os.makedirs(cap)
with open(os.path.join(cap, "tests.ndjson"), "w", encoding="utf-8", newline="\n") as f:
    for r in records:
        if r["event"] == "start":
            file, line = r.get("sourceFile") or "", r.get("sourceLine", -1)
            m = marks(changed_a, lambda t: t["path"] == file and t["line"] <= line <= t.get("end", t["line"]))
            if m:
                r["tags"] = sorted(set(r.get("tags", []) + m))
        f.write(json.dumps(r) + "\n")
# a capture holds the interactions too: one request and its response for the first test
with open(os.path.join(cap, "interactions.ndjson"), "w", encoding="utf-8", newline="\n") as f:
    f.write(json.dumps({"type": "Request", "uri": "http://api/orders/1", "serviceName": "api", "callerName": "web",
                        "testId": "t1", "method": "GET", "requestResponseId": "r1", "timestamp": "2026-01-01T10:00:00.001Z"}) + "\n")
    f.write(json.dumps({"type": "Response", "uri": "http://api/orders/1", "serviceName": "api", "callerName": "web",
                        "testId": "t1", "method": "GET", "requestResponseId": "r1", "statusCode": 200,
                        "timestamp": "2026-01-01T10:00:00.002Z"}) + "\n")
tool("ingest", "kronikol-capture", "--tests", "kronikol-capture/tests.ndjson", "-o", "ReportsA")
print("A", tagged(os.path.join(work, "ReportsA")))

# B: Cucumber Messages, the fixture a playwright-bdd run on Windows wrote (backslashed URIs, a rule, an outline)
changed_b = {"tests": [
    {"path": "features/kronikol-demo.feature", "line": 24, "change": "added"},
    {"path": "features/kronikol-demo.feature", "line": 42, "change": "edited"}]}
messages = [json.loads(l) for l in open(FIXTURE, encoding="utf-8")]
lines = {}
for m in messages:
    doc = m.get("gherkinDocument")
    if doc:
        for c in doc["feature"].get("children", []):
            if c.get("scenario"):
                lines[c["scenario"]["id"]] = c["scenario"]["location"]["line"]
            for rc in (c.get("rule") or {}).get("children", []):
                if rc.get("scenario"):
                    lines[rc["scenario"]["id"]] = rc["scenario"]["location"]["line"]


def file_of(uri):
    if uri.startswith("[") and "]:" in uri:
        uri = uri[uri.index("]:") + 2:]
    return "/".join(uri.split("\\"))


with open(os.path.join(work, "messages.tagged.ndjson"), "w", encoding="utf-8", newline="\n") as f:
    for m in messages:
        if "pickle" in m:
            uri, line = file_of(m["pickle"]["uri"]), lines.get(m["pickle"]["astNodeIds"][0])
            mk = ["@" + x for x in marks(changed_b, lambda t: t["path"] == uri and t["line"] == line)]
            if mk:
                m["pickle"]["tags"] = m["pickle"].get("tags", []) + [{"name": x} for x in mk]
        f.write(json.dumps(m) + "\n")
tool("ingest", "--cucumber-messages", "messages.tagged.ndjson", "-o", "ReportsB")
print("B", tagged(os.path.join(work, "ReportsB")))
