#!/usr/bin/env python
# Writes a capture and a tests file: scenarios with an explicit Background, and a step whose text a bar does not carry verbatim.
import json, os, sys, datetime as dt
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
t0 = dt.datetime(2026, 11, 3, 10, 0, 0, tzinfo=dt.timezone.utc)
def at(ms): return (t0 + dt.timedelta(milliseconds=ms)).isoformat().replace("+00:00", "Z")
inter, tests = [], []
def pair(test, path, ms):
    rid = f"{test}-{path}-{ms}"
    inter.append(dict(type="Request", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", requestResponseId=rid, timestamp=at(ms)))
    inter.append(dict(type="Response", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", statusCode="200", requestResponseId=rid, timestamp=at(ms + 5)))
def step(test, text, kw, ms, dur=100, **more):
    tests.append(dict(event="step", testId=test, text=text, keyword=kw, status="passed", durationMs=dur, timestamp=at(ms), **more))
for n, name in enumerate(["with background one", "with background two", "with background three"]):
    t = f"bg{n}"; base = n * 10000
    tests.append(dict(event="start", testId=t, testName=name, feature="F explicit background", timestamp=at(base)))
    step(t, "a shop", "Given", base + 10, background=True)
    step(t, "a customer", "And", base + 120, background=True)
    step(t, "a basket", "When", base + 240)
    pair(t, "basket", base + 260)
    step(t, "it is empty", "Then", base + 360)
    pair(t, "check", base + 380)
    tests.append(dict(event="end", testId=t, status="passed", durationMs=500, timestamp=at(base + 500)))
# no background: the same steps, as the control
t = "plain0"; base = 50000
tests.append(dict(event="start", testId=t, testName="no background", feature="G control", timestamp=at(base)))
step(t, "a basket", "When", base + 240); pair(t, "basket", base + 260)
step(t, "it is empty", "Then", base + 360); pair(t, "check", base + 380)
tests.append(dict(event="end", testId=t, status="passed", durationMs=500, timestamp=at(base + 500)))
# a step text the bar rewrites: new lines, angle brackets, a very long text
t = "odd0"; base = 60000
tests.append(dict(event="start", testId=t, testName="odd step text", feature="H odd text", timestamp=at(base)))
step(t, "a value <b>bold</b> and \"quoted\"", "Given", base + 10); pair(t, "a", base + 20)
step(t, "x" * 600, "When", base + 240); pair(t, "b", base + 260)
step(t, "line one\nline two", "Then", base + 360); pair(t, "c", base + 380)
tests.append(dict(event="end", testId=t, status="passed", durationMs=500, timestamp=at(base + 500)))
os.makedirs(os.path.join(out, "captures"), exist_ok=True)
with open(os.path.join(out, "captures", "calls.ndjson"), "w", encoding="utf-8") as f:
    for r in inter: f.write(json.dumps(r) + "\n")
with open(os.path.join(out, "tests.ndjson"), "w", encoding="utf-8") as f:
    for r in tests: f.write(json.dumps(r) + "\n")
print(len(inter), len(tests))
