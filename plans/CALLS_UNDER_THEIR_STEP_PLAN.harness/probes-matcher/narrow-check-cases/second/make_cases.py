#!/usr/bin/env python
# Ingest cases for the walk's look-ahead. Each case: rows in tests.ndjson, bars and calls in captures/calls.ndjson.
import json, os, sys, datetime as dt

root = sys.argv[1]
t0 = dt.datetime(2026, 11, 4, 10, 0, 0, tzinfo=dt.timezone.utc)
LEGACY = "hnote across <<stepDelimiter>> #black:<color:white>"


def at(ms):
    return (t0 + dt.timedelta(milliseconds=ms)).isoformat().replace("+00:00", "Z")


def case(name, rows, seq, flags="", test="s"):
    """rows: ('a', text) | ('s', keyword, text). seq: ('bar', label) | ('call', path) | ('kstep', keyword, text) | ('row', text) | ('custom', text)."""
    d = os.path.join(root, name)
    os.makedirs(os.path.join(d, "captures"), exist_ok=True)
    out, ms = [], 100
    for item in seq:
        ms += 10
        if item[0] == "bar":
            out.append(dict(kind="marker", markerKind="Step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test,
                            requestResponseId=f"{test}-bar-{ms}", timestamp=at(ms), plantUml=f"\n{LEGACY}{item[1]}\n\n"))
            out.append(dict(kind="marker", markerKind="Step", type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test,
                            requestResponseId=f"{test}-bar-{ms}-e", timestamp=at(ms), markerEnd=True))
        elif item[0] in ("row", "custom"):
            kind = "Row" if item[0] == "row" else "Custom"
            out.append(dict(kind="marker", markerKind=kind, type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test,
                            requestResponseId=f"{test}-m-{ms}", timestamp=at(ms), plantUml=f"\nhnote across #white: {item[1]}\n\n"))
            out.append(dict(kind="marker", markerKind=kind, type="Request", uri="http://override.com/", serviceName="", callerName="", testId=test,
                            requestResponseId=f"{test}-m-{ms}-e", timestamp=at(ms), markerEnd=True))
        elif item[0] == "kstep":
            rec = dict(kind="step", testId=test, text=item[2], timestamp=at(ms))
            if item[1]:
                rec["keyword"] = item[1]
            out.append(rec)
        else:
            path = item[1]
            rid = f"{test}-{path}-{ms}"
            out.append(dict(type="Request", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", timestamp=at(ms), requestResponseId=rid))
            out.append(dict(type="Response", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", statusCode="200", timestamp=at(ms + 1), requestResponseId=rid))
    tests = [dict(event="start", testId=test, testName="t", feature="F", timestamp=at(0))]
    ts = 10
    for r in rows:
        ts += 1
        if r[0] == "a":
            tests.append(dict(event="assertion", testId=test, text=r[1], status="passed", timestamp=at(ts)))
        else:
            rec = dict(event="step", testId=test, status="passed", durationMs=1, text=r[2], timestamp=at(ts))
            if r[1]:
                rec["keyword"] = r[1]
            tests.append(rec)
    tests.append(dict(event="end", testId=test, status="passed", durationMs=500, timestamp=at(ms + 100)))
    with open(os.path.join(d, "captures", "calls.ndjson"), "w", encoding="utf-8") as fh:
        for r in out:
            fh.write(json.dumps(r) + "\n")
    with open(os.path.join(d, "tests.ndjson"), "w", encoding="utf-8") as fh:
        for r in tests:
            fh.write(json.dumps(r) + "\n")
    with open(os.path.join(d, "flags.txt"), "w", encoding="utf-8") as fh:
        fh.write(flags)


# F: a hook's bar taken for the row after an assertion's, by the loose half of the check. The hook's bar comes before every row's.
case("F1-hook-bar-holds-next-rows-text",
     [("a", "ready"), ("s", "When", "pay"), ("s", "When", "ship")],
     [("bar", "Prepay hook"), ("call", "hook"), ("bar", "When pay"), ("call", "pay"), ("bar", "When ship"), ("call", "ship")])
case("F2-row-text-in-the-bar-prefix",
     [("a", "ready"), ("s", None, "step"), ("s", None, "ship")],
     [("bar", "setup"), ("call", "hook"), ("bar", "Step"), ("call", "stepcall"), ("bar", "Ship"), ("call", "ship")])
case("F3-blank-row-after-an-assertion",
     [("a", "ready"), ("s", "Given", "  "), ("s", "When", "ship")],
     [("bar", "setup"), ("call", "hook"), ("bar", "Given"), ("call", "blank"), ("bar", "When ship"), ("call", "ship")])
case("F4-assertion-bar-no-glyph-holds-next-rows-text",
     [("a", "the payment is ok"), ("s", None, "payment"), ("s", None, "ship")],
     [("bar", "The payment is ok"), ("call", "assertcall"), ("bar", "Payment"), ("call", "pay"), ("bar", "Ship"), ("call", "ship")])
case("F5-run-of-assertions-hook-reads-a-later-one",
     [("a", "x"), ("a", "y"), ("s", "When", "pay"), ("s", "When", "ship"), ("s", "When", "bill")],
     [("bar", "hook ✓ y"), ("call", "hook1"), ("bar", "hook two"), ("call", "hook2"), ("bar", "When pay"), ("call", "pay"), ("bar", "When ship"), ("call", "ship"), ("bar", "When bill"), ("call", "bill")])
case("F6-assertion-then-mid-list-assertion-hook",
     [("s", "When", "pay"), ("a", "ready"), ("s", "When", "ship"), ("s", "When", "bill")],
     [("bar", "When pay"), ("call", "pay"), ("bar", "Unship hook"), ("call", "hook"), ("bar", "When ship"), ("call", "ship"), ("bar", "When bill"), ("call", "bill")])

# S: the size of B3's kind, and what shares its mechanism.
case("S1-b3-longer-rows-after-the-hook",
     [("a", "ready"), ("s", "When", "pay"), ("s", "When", "ship"), ("s", "When", "bill"), ("s", "When", "file")],
     [("bar", "When pay"), ("call", "pay"), ("bar", "hook setup"), ("call", "hook"), ("bar", "When ship"), ("call", "ship"), ("bar", "When bill"), ("call", "bill"), ("bar", "When file"), ("call", "file")])
case("S2-capture-kind-step-and-tests-file-both-draw",
     [("a", "ready"), ("s", "When", "pay"), ("s", "When", "ship")],
     [("kstep", "When", "pay"), ("call", "pay"), ("kstep", "When", "ship"), ("call", "ship")])
case("S3-row-bar-drawn-twice",
     [("a", "ready"), ("s", "When", "pay"), ("s", "When", "ship")],
     [("bar", "When pay"), ("call", "pay1"), ("bar", "When pay"), ("call", "pay2"), ("bar", "When ship"), ("call", "ship")])

# C: controls the look-ahead must not change or must improve.
case("C1-assertion-first", [("a", "ready"), ("s", "When", "pay"), ("s", "When", "ship")],
     [("bar", "When pay"), ("call", "pay"), ("bar", "When ship"), ("call", "ship")])
case("C2-several-assertions", [("a", "x"), ("a", "y"), ("a", "z"), ("s", "When", "pay"), ("a", "w"), ("s", "When", "ship")],
     [("bar", "When pay"), ("call", "pay"), ("bar", "When ship"), ("call", "ship")])
case("C3-assertion-last-hook", [("s", "When", "pay"), ("a", "done")],
     [("bar", "When pay"), ("call", "pay"), ("bar", "hook teardown"), ("call", "hook")])
case("C4-row-and-custom-markers", [("a", "ready"), ("s", "When", "pay"), ("a", "w"), ("s", "When", "ship")],
     [("row", "When ship"), ("call", "before"), ("custom", "When pay"), ("bar", "When pay"), ("call", "pay"), ("row", "x"), ("bar", "When ship"), ("call", "ship")])
case("C5-two-plain-hooks-then-rows", [("a", "x"), ("a", "y"), ("s", "When", "pay"), ("s", "When", "ship")],
     [("bar", "hook1"), ("call", "hook1"), ("bar", "hook2"), ("call", "hook2"), ("bar", "When pay"), ("call", "pay"), ("bar", "When ship"), ("call", "ship")])
case("C6-assertion-holds-glyph-bar-late", [("a", "x"), ("s", "When", "pay"), ("a", "y"), ("s", "When", "ship")],
     [("bar", "When pay"), ("call", "pay"), ("bar", "✓ y"), ("call", "assertcall"), ("bar", "When ship"), ("call", "ship")])
