#!/usr/bin/env python
# Third set: own rows on an ingest. A file per scenario on a shared opening step, by the tests file's step index; a table per scenario.
import json, os, sys, datetime as dt
root = sys.argv[1]
t0 = dt.datetime(2026, 11, 3, 10, 0, 0, tzinfo=dt.timezone.utc)
def at(ms): return (t0 + dt.timedelta(milliseconds=ms)).isoformat().replace("+00:00", "Z")
inter, tests = [], []
def pair(test, path, ms):
    rid = f"{test}-{path}-{ms}"
    inter.append(dict(type="Request", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", requestResponseId=rid, timestamp=at(ms)))
    inter.append(dict(type="Response", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", statusCode="200", requestResponseId=rid, timestamp=at(ms + 5)))
d = os.path.join(root, "W-file-per-scenario-on-a-shared-step")
os.makedirs(os.path.join(d, "captures"), exist_ok=True)
for n in range(3):
    t = f"w{n}"; b = n * 10000
    f = os.path.join(d, f"shot-{n}.txt")
    open(f, "w").write(str(n))
    tests.append(dict(event="start", testId=t, testName=f"shared {n}", feature="F", timestamp=at(b)))
    tests.append(dict(event="step", testId=t, keyword="Given", text="a shop", status="passed", durationMs=100, timestamp=at(b + 10), table=[["v"], [str(n)]]))
    pair(t, "shop", b + 20)
    tests.append(dict(event="attachment", testId=t, name=f"shot-{n}.txt", path=os.path.abspath(f), mediaType="text/plain", step=0, timestamp=at(b + 30)))
    tests.append(dict(event="step", testId=t, keyword="Then", text=f"it works {n}", status="passed", durationMs=100, timestamp=at(b + 100)))
    pair(t, "check", b + 120)
    tests.append(dict(event="end", testId=t, status="passed", durationMs=500, timestamp=at(b + 500)))
with open(os.path.join(d, "captures", "calls.ndjson"), "w", encoding="utf-8") as fh:
    for r in inter: fh.write(json.dumps(r) + "\n")
with open(os.path.join(d, "tests.ndjson"), "w", encoding="utf-8") as fh:
    for r in tests: fh.write(json.dumps(r) + "\n")
