#!/usr/bin/env python
# Writes one capture + tests file per case under <out>/<case>/ . Each case is a separate ingest, so a flag is that case's own.
import json, os, sys, datetime as dt
root = sys.argv[1]
t0 = dt.datetime(2026, 11, 3, 10, 0, 0, tzinfo=dt.timezone.utc)
def at(ms): return (t0 + dt.timedelta(milliseconds=ms)).isoformat().replace("+00:00", "Z")

class Case:
    def __init__(self, name):
        self.name, self.inter, self.tests = name, [], []
    def pair(self, test, path, ms):
        rid = f"{test}-{path}-{ms}"
        self.inter.append(dict(type="Request", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", requestResponseId=rid, timestamp=at(ms)))
        self.inter.append(dict(type="Response", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", statusCode="200", requestResponseId=rid, timestamp=at(ms + 5)))
    def step(self, test, text, kw, ms, dur=100, **more):
        d = dict(event="step", testId=test, status="passed", durationMs=dur, **more)
        if kw is not None: d["keyword"] = kw
        if text is not None: d["text"] = text
        if ms is not None: d["timestamp"] = at(ms)
        self.tests.append(d)
    def start(self, test, name, feature, ms, **more):
        self.tests.append(dict(event="start", testId=test, testName=name, feature=feature, timestamp=at(ms), **more))
    def end(self, test, ms, status="passed", **more):
        self.tests.append(dict(event="end", testId=test, status=status, durationMs=500, timestamp=at(ms), **more))
    def bar(self, test, label, ms):
        # a raw marker as a capture holds it: the two halves InteractionRecord.FromLog writes
        rid = f"{test}-bar-{ms}"
        common = dict(kind="marker", markerKind="Step", type="Request", uri="http://marker/", serviceName="Shop", callerName="Tests", testId=test, requestResponseId=rid, timestamp=at(ms))
        self.inter.append(dict(common, plantUml="hnote across <<stepDelimiter>> #black:<color:white>" + label))
        self.inter.append(dict(common, markerEnd=True))
    def write(self):
        d = os.path.join(root, self.name)
        os.makedirs(os.path.join(d, "captures"), exist_ok=True)
        with open(os.path.join(d, "captures", "calls.ndjson"), "w", encoding="utf-8") as f:
            for r in self.inter: f.write(json.dumps(r) + "\n")
        with open(os.path.join(d, "tests.ndjson"), "w", encoding="utf-8") as f:
            for r in self.tests: f.write(json.dumps(r) + "\n")

cases = []
def case(name):
    c = Case(name); cases.append(c); return c

# A: an explicit Background whose text is held by the first scenario step's bar: "a customer" in "When a customer places an order"
c = case("A-bg-text-in-first-bar")
for n in range(2):
    t = f"a{n}"; b = n * 10000
    c.start(t, f"order {n}", "F", b)
    c.step(t, "a customer", "Given", b + 10, background=True)
    c.step(t, f"a customer places order {n}", "When", b + 100); c.pair(t, "orders", b + 120)
    c.step(t, "it is confirmed", "Then", b + 200); c.pair(t, "confirm", b + 220)
    c.end(t, b + 500)

# B: an assertion before the first step: BuildStepTree makes it a top-level row, and no bar is drawn for it
c = case("B-assertion-before-first-step")
t = "b0"
c.start(t, "assertion first", "F", 0)
c.tests.append(dict(event="assertion", testId=t, text="the stack is up", status="passed", timestamp=at(5)))
c.step(t, "a basket", "When", 100); c.pair(t, "basket", 120)
c.step(t, "it is empty", "Then", 200); c.pair(t, "check", 220)
c.end(t, 500)

# C: a retried test: the first attempt failed, the second passed; do the first attempt's bars reach the walk?
c = case("C-retry")
t = "c0"
c.start(t, "retried", "F", 0, attempt=1)
c.step(t, "a basket", "When", 100, attempt=1); c.pair(t, "basket", 120)
c.end(t, 300, status="failed", attempt=1, error="boom")
c.start(t, "retried", "F", 1000, attempt=2)
c.step(t, "a basket", "When", 1100, attempt=2); c.pair(t, "basket", 1120)
c.step(t, "it is empty", "Then", 1200, attempt=2); c.pair(t, "check", 1220)
c.end(t, 1500, attempt=2)

# D: a step with no text: the row reads "(step)", the bar "step"
c = case("D-step-with-no-text")
t = "d0"
c.start(t, "no text", "F", 0)
c.step(t, None, "When", 100); c.pair(t, "a", 120)
c.step(t, "it is empty", "Then", 200); c.pair(t, "b", 220)
c.end(t, 500)

# E: a capture that draws its own bars, one of them for a hook the tests file does not list, every row timed
c = case("E-capture-own-bars-hook-bar")
t = "e0"
c.start(t, "hook bar", "F", 0)
c.bar(t, "Seed the shop", 10); c.pair(t, "seed", 20)
c.bar(t, "When a basket", 100); c.step(t, "a basket", "When", 100); c.pair(t, "basket", 120)
c.bar(t, "Then it is empty", 200); c.step(t, "it is empty", "Then", 200); c.pair(t, "check", 220)
c.end(t, 500)

# F: untimed rows (each DrawsNoBar) with a capture's own bars, the middle bar's text is not the row's
c = case("F-untimed-rows-own-bars-one-bar-differs")
t = "f0"
c.start(t, "untimed", "F", 0)
c.step(t, "a basket", "Given", None)
c.step(t, "an order with <amount>", "When", None)
c.step(t, "it is confirmed", "Then", None)
c.bar(t, "Given a basket", 100); c.pair(t, "basket", 120)
c.bar(t, "When an order with 5", 200); c.pair(t, "order", 220)
c.bar(t, "Then it is confirmed", 300); c.pair(t, "confirm", 320)
c.end(t, 500)

# G: a hook bar in the capture, two steps sharing a first line: the bars are one out, the text check is what says so
c = case("G-shared-first-line-one-out")
t = "g0"
c.start(t, "shared first line", "F", 0)
c.bar(t, "Seed the shop", 10); c.pair(t, "seed", 20)
c.step(t, "the order is placed\nfor cash", "When", 100); c.bar(t, "When the order is placed", 100); c.pair(t, "cash", 120)
c.step(t, "the order is placed\nfor card", "And", 200); c.bar(t, "And the order is placed", 200); c.pair(t, "card", 220)
c.step(t, "it is confirmed", "Then", 300); c.bar(t, "Then it is confirmed", 300); c.pair(t, "confirm", 320)
c.end(t, 500)

# H: the same with two steps sharing an opening of 400 characters
c = case("H-shared-400-chars-one-out")
t = "h0"
opening = "the customer " + "x" * 390
c.start(t, "shared opening", "F", 0)
c.bar(t, "Seed the shop", 10); c.pair(t, "seed", 20)
c.step(t, opening + " pays cash", "When", 100); c.bar(t, "When " + opening + " pays cash", 100); c.pair(t, "cash", 120)
c.step(t, opening + " pays card", "And", 200); c.bar(t, "And " + opening + " pays card", 200); c.pair(t, "card", 220)
c.step(t, "it is confirmed", "Then", 300); c.bar(t, "Then it is confirmed", 300); c.pair(t, "confirm", 320)
c.end(t, 500)

# I: raw bars an older version or another producer wrote, not escaped as this version writes them
c = case("I-raw-bars-unescaped")
t = "i0"
c.start(t, "unescaped", "F", 0)
c.step(t, r"the file C:\temp\new is read", "When", 100); c.bar(t, r"When the file C:\temp\new is read", 100); c.pair(t, "read", 120)
c.step(t, "a value <$name> is sent", "And", 200); c.bar(t, "And a value <$name> is sent", 200); c.pair(t, "send", 220)
c.step(t, "it is confirmed", "Then", 300); c.bar(t, "Then it is confirmed", 300); c.pair(t, "confirm", 320)
c.end(t, 500)

# J: the bars are one out, and two step texts differ by a space only once folded
c = case("J-fold-false-match-one-out")
t = "j0"
c.start(t, "fold", "F", 0)
c.bar(t, "Seed the shop", 10); c.pair(t, "seed", 20)
c.step(t, "a new order", "When", 100); c.bar(t, "When a new order", 100); c.pair(t, "n", 120)
c.step(t, "anew order", "And", 200); c.bar(t, "And anew order", 200); c.pair(t, "m", 220)
c.step(t, "it is confirmed", "Then", 300); c.bar(t, "Then it is confirmed", 300); c.pair(t, "confirm", 320)
c.end(t, 500)

# K: a control, timed rows, the tests file draws the bars, no background
c = case("K-control")
t = "k0"
c.start(t, "control", "F", 0)
c.step(t, "a basket", "When", 100); c.pair(t, "basket", 120)
c.step(t, "it is empty", "Then", 200); c.pair(t, "check", 220)
c.end(t, 500)

# L: a store projected from an in-process run: bars for the background too, the tests file marks it background
c = case("L-store-bars-with-background")
for n in range(2):
    t = f"l{n}"; b = n * 10000
    c.start(t, f"store {n}", "F", b)
    c.step(t, "a shop", "Given", b + 10, background=True); c.bar(t, "Given a shop", b + 10); c.pair(t, "shop", b + 20)
    c.step(t, "a basket", "When", b + 100); c.bar(t, "When a basket", b + 100); c.pair(t, "basket", b + 120)
    c.step(t, "it is empty", "Then", b + 200); c.bar(t, "Then it is empty", b + 200); c.pair(t, "check", b + 220)
    c.end(t, b + 500)

# M: a store with a hook's bar first, and an explicit background
c = case("M-store-hook-bar-and-background")
t = "m0"
c.start(t, "store hook", "F", 0)
c.bar(t, "Seed the shop", 5); c.pair(t, "seed", 6)
c.step(t, "a shop", "Given", 10, background=True); c.bar(t, "Given a shop", 10); c.pair(t, "shop", 20)
c.step(t, "a basket", "When", 100); c.bar(t, "When a basket", 100); c.pair(t, "basket", 120)
c.step(t, "it is empty", "Then", 200); c.bar(t, "Then it is empty", 200); c.pair(t, "check", 220)
c.end(t, 500)

# N: a sub-step, and a doc string and a table on steps
c = case("N-nested-doc-table")
t = "n0"
c.start(t, "nested", "F", 0)
c.step(t, "a basket", "Given", 100, table=[["a", "b"], ["1", "2"]]); c.pair(t, "basket", 120)
c.step(t, "an inner step", None, 130, level=1); c.pair(t, "inner", 140)
c.step(t, "it is sent", "When", 200, docString='{\n  "a": 1\n}'); c.pair(t, "send", 220)
c.step(t, "it is empty", "Then", 300); c.pair(t, "check", 320)
c.end(t, 500)

# O: a failed scenario in a heuristic background feature: later scenarios fail in the shared Given
c = case("O-heuristic-bg-other-outcome")
for n in range(3):
    t = f"o{n}"; b = n * 10000
    c.start(t, f"outcome {n}", "F", b)
    st = "failed" if n == 1 else "passed"
    d = dict(event="step", testId=t, text="a shop", keyword="Given", status=st, durationMs=100 + n * 50, timestamp=at(b + 10))
    if n == 1: d["error"] = "no shop"
    c.tests.append(d); c.pair(t, "shop", b + 20)
    c.step(t, f"it works {n}", "Then", b + 100); c.pair(t, "check", b + 120)
    c.end(t, b + 500, status=st, **({"error": "no shop"} if n == 1 else {}))

for c in cases:
    c.write()
print(" ".join(c.name for c in cases))
