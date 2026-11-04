#!/usr/bin/env python
# Second set of ingest cases: what a bar that is a marked row's own, in other words, does to the walk.
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

# P: a store's bars for every row, the first Background row's bar in other words than the tests file's row
c = case("P-store-bg-bar-in-other-words")
t = "p0"
c.start(t, "store", "F", 0)
c.step(t, "a shop named <name>", "Given", 10, background=True); c.bar(t, "Given a shop named Acme", 10); c.pair(t, "shop", 20)
c.step(t, "a customer", "And", 50, background=True); c.bar(t, "And a customer", 50); c.pair(t, "customer", 60)
c.step(t, "a basket", "When", 100); c.bar(t, "When a basket", 100); c.pair(t, "basket", 120)
c.step(t, "it is empty", "Then", 200); c.bar(t, "Then it is empty", 200); c.pair(t, "check", 220)
c.end(t, 500)

# Q: the same with a scenario step's bar in other words, the control: no marked row in the way
c = case("Q-store-own-step-bar-in-other-words")
t = "q0"
c.start(t, "store", "F", 0)
c.step(t, "a shop named <name>", "Given", 10); c.bar(t, "Given a shop named Acme", 10); c.pair(t, "shop", 20)
c.step(t, "a basket", "When", 100); c.bar(t, "When a basket", 100); c.pair(t, "basket", 120)
c.step(t, "it is empty", "Then", 200); c.bar(t, "Then it is empty", 200); c.pair(t, "check", 220)
c.end(t, 500)

# R: the tests file draws the bars, a Background row whose first line is a drawn step's first line
c = case("R-bg-first-line-is-a-steps")
t = "r0"
c.start(t, "first line", "F", 0)
c.step(t, "the order is placed\nfor cash", "Given", 10, background=True)
c.step(t, "the order is placed\nfor card", "When", 100); c.pair(t, "card", 120)
c.step(t, "it is confirmed", "Then", 200); c.pair(t, "confirm", 220)
c.end(t, 500)

# S: the tests file draws the bars, a Background row whose text is held by a drawn step's only once spaces are folded
c = case("S-bg-text-in-a-bar-once-folded")
t = "s0"
c.start(t, "folded", "F", 0)
c.step(t, "a cart", "Given", 10, background=True)
c.step(t, "a car takes the order", "When", 100); c.pair(t, "order", 120)
c.step(t, "it is confirmed", "Then", 200); c.pair(t, "confirm", 220)
c.end(t, 500)

# U: two assertions before the first step, and one after it, in a passing test
c = case("U-assertions-before-the-first-step")
t = "u0"
c.start(t, "assertions", "F", 0)
c.tests.append(dict(event="assertion", testId=t, text="the stack is up", status="passed", timestamp=at(5)))
c.tests.append(dict(event="assertion", testId=t, text="the page is open", status="passed", timestamp=at(6)))
c.step(t, "a basket", "When", 100); c.pair(t, "basket", 120)
c.tests.append(dict(event="assertion", testId=t, text="it answered", status="passed", timestamp=at(130)))
c.step(t, "it is empty", "Then", 200); c.pair(t, "check", 220)
c.end(t, 500)

# V: a test with assertions and no steps at all, the control for the flag
c = case("V-assertions-and-no-steps")
t = "v0"
c.start(t, "no steps", "F", 0)
c.tests.append(dict(event="assertion", testId=t, text="the stack is up", status="passed", timestamp=at(5)))
c.pair(t, "basket", 120)
c.end(t, 500)

for c in cases:
    c.write()
print(" ".join(c.name for c in cases))
