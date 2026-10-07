"""S7 of plans/DIFF_BODY_CALL_PAIRING_PLAN.md: the call pairing over two runs from the other writers of the records it
reads - `kronikol ingest` and `kronikol merge` - checked against research/pairing.py the way s6 checks the .NET writer.

Usage: python producers.py <tool dir holding Kronikol.Tool.dll and Kronikol.dll> <work dir outside the repository>

ingest: two runs of three tests written as capture feeds, as a capturer would write them. The second run makes the
health checks in another order, creates orders under new ids, and queries three times where the first queried four.
merge: the two mergeable shards the test suite keeps (tests/Kronikol.Tests/TestData/Reports), merged as they are, and
merged again as a second run would have written them: in every scenario the first two calls swapped, every call a new
pairing id, and every GUID in a URI regenerated. The merged reports are what `diff` reads.

For each pair: pairing.py --design gives the rule's answer for every address with a body, and s6/acceptance.cs runs
the engine on each. Anything other than "pairs as the rule pairs it" or "refuses, exit 2, with its reason" is a finding.
"""
import json
import pathlib
import re
import shutil
import subprocess
import sys
import uuid

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[2]
T0 = "2026-10-07T10:00:00.000+00:00"


def stamp(ms):
    seconds, millis = divmod(ms, 1000)
    minutes, seconds = divmod(seconds, 60)
    return f"2026-10-07T10:{minutes:02d}:{seconds:02d}.{millis:03d}+00:00"


def call(test, service, method, uri, request, response, at):
    rid = str(uuid.uuid4())
    common = {"method": method, "uri": uri, "serviceName": service, "callerName": "web", "requestResponseId": rid,
              "testId": test, "traceId": str(uuid.uuid4())}
    return [dict(common, type="Request", content=request, timestamp=stamp(at)),
            dict(common, type="Response", content=response, statusCode="200", timestamp=stamp(at + 30))]


def feed(second):
    """One run's capture feed and its tests feed."""
    records, at = [], 0

    def add(test, *args):
        nonlocal at
        at += 50
        records.extend(call(test, *args, at))

    health = ["goat", "supplier", "kitchen"] if second else ["goat", "kitchen", "supplier"]
    for service in health:
        add("t-health", service, "GET", f"http://{service}:80/health", None,
            json.dumps({"status": "degraded" if second and service == "kitchen" else "ok", "service": service}))
    order = str(uuid.uuid4())
    add("t-order", "breakfast", "POST", "http://breakfast:80/orders", json.dumps({"items": 2}), json.dumps({"id": order}))
    add("t-order", "breakfast", "PATCH", f"http://breakfast:80/orders/{order}/status", json.dumps({"status": "Ready"}),
        json.dumps({"id": order, "status": "Ready", "run": 2 if second else 1}))
    for n in range(3 if second else 4):
        add("t-query", "CosmosDB", "QUERY", "https://cosmos:8081/orders", json.dumps({"page": n}), json.dumps({"rows": n}))
    tests = []
    for test, name in (("t-health", "health › all three answer"), ("t-order", "orders › an order is marked ready"),
                       ("t-query", "orders › the list pages")):
        tests.append({"event": "start", "testId": test, "testName": name, "feature": "breakfast.spec.ts", "timestamp": T0})
        tests.append({"event": "end", "testId": test, "status": "passed", "durationMs": 100, "timestamp": stamp(5000)})
    return records, tests


GUID = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")


def second_run_of(shard):
    """A mergeable shard as the next run of the same tests would have written it."""
    data = json.loads(shard.read_text(encoding="utf-8"))
    ids = {}

    def fresh(match):
        return ids.setdefault(match.group(0).lower(), str(uuid.uuid4()))

    for feature in data.get("features") or []:
        for scenario in feature.get("scenarios") or []:
            entries = scenario.get("httpInteractions") or []
            calls = []
            for entry in entries:
                rid = entry.get("requestResponseId")
                group = next((c for c in calls if rid and c[0].get("requestResponseId") == rid), None)
                if group is None:
                    calls.append([entry])
                else:
                    group.append(entry)
            if len(calls) >= 2:
                calls[0], calls[1] = calls[1], calls[0]
            for group in calls:
                new = str(uuid.uuid4())
                for entry in group:
                    if entry.get("requestResponseId"):
                        entry["requestResponseId"] = new
                    if isinstance(entry.get("uri"), str):
                        entry["uri"] = GUID.sub(fresh, entry["uri"])
            scenario["httpInteractions"] = [entry for group in calls for entry in group]
    return json.dumps(data)


def run(*args, cwd=None):
    result = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace",
                            env={**__import__("os").environ, "KRONIKOL_HISTORY": "off", "PYTHONUTF8": "1"})
    if result.returncode != 0:
        raise SystemExit(f"{' '.join(map(str, args))}: exit {result.returncode}\n{result.stdout}\n{result.stderr}")
    return result.stdout


def main():
    tool, work = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
    shutil.rmtree(work, ignore_errors=True)
    work.mkdir(parents=True)
    shutil.copy2(HERE.parent / "s6" / "acceptance.cs", work / "acceptance.cs")
    kronikol = ["dotnet", str(tool / "Kronikol.Tool.dll")]
    pairs = []

    for label, second in (("a", False), ("b", True)):
        captures = work / f"ingest-{label}"
        captures.mkdir()
        records, tests = feed(second)
        (captures / "web.ndjson").write_text("".join(json.dumps(r) + "\n" for r in records), encoding="utf-8")
        (captures / "tests.ndjson").write_text("".join(json.dumps(t) + "\n" for t in tests), encoding="utf-8")
        run(*kronikol, "ingest", str(captures / "web.ndjson"), "--tests", str(captures / "tests.ndjson"), "-o", str(work / f"ingested-{label}"))
    pairs.append(("ingest", work / "ingested-a" / "TestRunReport.json", work / "ingested-b" / "TestRunReport.json"))

    shards = sorted((REPO / "tests" / "Kronikol.Tests" / "TestData" / "Reports").glob("*.mergeable.json"))
    first, again = work / "shards-a", work / "shards-b"
    first.mkdir()
    again.mkdir()
    for shard in shards:
        shutil.copy2(shard, first / shard.name)
        (again / shard.name).write_text(second_run_of(shard), encoding="utf-8")
    for label, folder in (("a", first), ("b", again)):
        run(*kronikol, "merge", str(folder), "-o", str(work / f"merged-{label}" / "TestRunReport.html"))
    pairs.append(("merge", work / "merged-a" / "TestRunReport.json", work / "merged-b" / "TestRunReport.json"))

    for name, old, new in pairs:
        design = work / f"{name}-design.tsv"
        print(f"== {name}: {old.relative_to(work)} -> {new.relative_to(work)}")
        print(run(sys.executable, str(HERE.parent / "research" / "pairing.py"), str(old), str(new), "--design", str(design)))
        print(run("dotnet", "run", "acceptance.cs", "--", str(tool), str(old), str(new), str(design), str(work / f"{name}-engine.tsv"), cwd=work))


if __name__ == "__main__":
    main()
