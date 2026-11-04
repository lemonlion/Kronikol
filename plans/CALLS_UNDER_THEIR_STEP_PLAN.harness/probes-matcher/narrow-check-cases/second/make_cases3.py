#!/usr/bin/env python
# A row with no text under a bar that is not its own, and a hook's bar read by DrawsStep alone.
import json, os, sys, datetime as dt

root = sys.argv[1]
t0 = dt.datetime(2026, 11, 4, 10, 0, 0, tzinfo=dt.timezone.utc)
LEGACY = "hnote across <<stepDelimiter>> #black:<color:white>"


def at(ms):
    return (t0 + dt.timedelta(milliseconds=ms)).isoformat().replace("+00:00", "Z")


def case(name, rows, seq):
    d = os.path.join(root, name)
    os.makedirs(os.path.join(d, "captures"), exist_ok=True)
    out, ms = [], 100
    for op, value in seq:
        ms += 10
        if op == "bar":
            for rid, extra in ((f"bar-{ms}", dict(plantUml=f"\n{LEGACY}{value}\n\n")), (f"bar-{ms}-e", dict(markerEnd=True))):
                out.append(dict(kind="marker", markerKind="Step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId="s", requestResponseId=rid, timestamp=at(ms), **extra))
        else:
            out.append(dict(type="Request", testId="s", method="POST", uri=f"http://shop.test/{value}", serviceName="Shop", callerName="Tests", content="{}", timestamp=at(ms), requestResponseId=f"{value}-{ms}"))
            out.append(dict(type="Response", testId="s", method="POST", uri=f"http://shop.test/{value}", serviceName="Shop", callerName="Tests", content="{}", statusCode="200", timestamp=at(ms + 1), requestResponseId=f"{value}-{ms}"))
    tests, ts = [dict(event="start", testId="s", testName="t", feature="F", timestamp=at(0))], 10
    for row in rows:
        ts += 1
        if row[0] == "a":
            tests.append(dict(event="assertion", testId="s", text=row[1], status="passed", timestamp=at(ts)))
        else:
            rec = dict(event="step", testId="s", status="passed", durationMs=1, timestamp=at(ts))
            if row[1]:
                rec["keyword"] = row[1]
            if row[2] is not None:
                rec["text"] = row[2]
            tests.append(rec)
    tests.append(dict(event="end", testId="s", status="passed", durationMs=500, timestamp=at(ms + 100)))
    with open(os.path.join(d, "captures", "calls.ndjson"), "w", encoding="utf-8") as fh:
        fh.writelines(json.dumps(r) + "\n" for r in out)
    with open(os.path.join(d, "tests.ndjson"), "w", encoding="utf-8") as fh:
        fh.writelines(json.dumps(r) + "\n" for r in tests)


e = "<U+005C><U+200B>"
case("N1-no-text-row-takes-a-two-word-bar", [("s", None, None), ("s", "When", "ship")],
     [("bar", "Cleanup step"), ("call", "hook"), ("bar", "When ship"), ("call", "ship")])
case("N2-no-text-keyword-row-takes-a-two-word-bar", [("s", "When", None), ("s", "When", "ship")],
     [("bar", "Cleanup step"), ("call", "hook"), ("bar", "When ship"), ("call", "ship")])
case("N3-no-text-row-after-an-assertion", [("a", "ready"), ("s", None, None), ("s", "When", "ship")],
     [("bar", "Cleanup step"), ("call", "hook"), ("bar", "Step"), ("call", "own"), ("bar", "When ship"), ("call", "ship")])
case("F7-hook-bar-read-by-drawsstep-alone", [("a", "ready"), ("s", "When", r"pay C:\x"), ("s", "When", "ship")],
     [("bar", f"Before pay C:{e}x"), ("call", "hook"), ("bar", f"When pay C:{e}x"), ("call", "pay"), ("bar", "When ship"), ("call", "ship")])
case("B1-bddfy-and-under-and-escape", [("s", "Then", r"x at C:\temp"), ("s", "And", r"y at C:\temp")],
     [("bar", f"Then x at C:{e}temp"), ("call", "x"), ("bar", f"And And y at C:{e}temp"), ("call", "y")])
case("B2-bddfy-and-under-and-short-control", [("s", "Then", "x"), ("s", "And", "y")],
     [("bar", "Then x"), ("call", "x"), ("bar", "And And y"), ("call", "y")])
