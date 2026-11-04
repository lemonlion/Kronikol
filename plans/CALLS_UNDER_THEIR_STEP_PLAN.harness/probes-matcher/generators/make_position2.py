#!/usr/bin/env python
"""More ingest cases for the walk's place: what happens to a marked row after the report moves it, and a nested row first.

Usage: make_position2.py <out dir>
"""
import datetime as dt
import json
import os
import sys

root = sys.argv[1]
t0 = dt.datetime(2026, 11, 3, 10, 0, 0, tzinfo=dt.timezone.utc)


def at(ms):
    return (t0 + dt.timedelta(milliseconds=ms)).isoformat().replace("+00:00", "Z")


class Case:
    def __init__(self, name):
        self.name, self.inter, self.tests = name, [], []

    def pair(self, test, path, ms):
        rid = f"{test}-{path}-{ms}"
        self.inter.append(dict(type="Request", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", requestResponseId=rid, timestamp=at(ms)))
        self.inter.append(dict(type="Response", testId=test, method="POST", uri=f"http://shop.test/{path}", serviceName="Shop", callerName="Tests", content="{}", statusCode="200", requestResponseId=rid, timestamp=at(ms + 5)))

    def step(self, test, text, kw, ms, dur=100, **more):
        d = dict(event="step", testId=test, status="passed", durationMs=dur, **more)
        if kw is not None:
            d["keyword"] = kw
        if text is not None:
            d["text"] = text
        if ms is not None:
            d["timestamp"] = at(ms)
        self.tests.append(d)

    def start(self, test, name, ms, feature="F", **more):
        self.tests.append(dict(event="start", testId=test, testName=name, feature=feature, timestamp=at(ms), **more))

    def end(self, test, ms, status="passed", **more):
        self.tests.append(dict(event="end", testId=test, status=status, durationMs=500, timestamp=at(ms), **more))

    def assertion(self, test, text, ms, **more):
        self.tests.append(dict(event="assertion", testId=test, text=text, status="passed", timestamp=at(ms), **more))

    def write(self):
        d = os.path.join(root, self.name)
        os.makedirs(os.path.join(d, "captures"), exist_ok=True)
        with open(os.path.join(d, "captures", "calls.ndjson"), "w", encoding="utf-8") as f:
            for r in self.inter:
                f.write(json.dumps(r) + "\n")
        with open(os.path.join(d, "tests.ndjson"), "w", encoding="utf-8") as f:
            for r in self.tests:
                f.write(json.dumps(r) + "\n")


cases = []


def case(name):
    c = Case(name)
    cases.append(c)
    return c


# Scenarios sharing an opening, which stays where it is, as the next row is a When (BackgroundStepsDetector.cs:40-42). Here the shared rows have no times, so they drew no bar, and the walk must not count them.
c = case("Z1-detector-lifts-untimed-rows")
for n in range(3):
    t, b = f"z{n}", n * 10000
    c.start(t, f"lifted {n}", b)
    c.step(t, "a shop", "Given", None)
    c.step(t, f"basket {n}", "When", b + 100)
    c.pair(t, "basket", b + 120)
    c.step(t, f"it is empty {n}", "Then", b + 200)
    c.pair(t, "check", b + 220)
    c.end(t, b + 500)

# The same with the shared rows timed, so each drew its bar: they keep them.
c = case("Z2-detector-lifts-timed-rows")
for n in range(3):
    t, b = f"z{n}", n * 10000
    c.start(t, f"lifted {n}", b)
    c.step(t, "a shop", "Given", b + 10)
    c.pair(t, "shop", b + 20)
    c.step(t, f"basket {n}", "When", b + 100)
    c.pair(t, "basket", b + 120)
    c.step(t, f"it is empty {n}", "Then", b + 200)
    c.pair(t, "check", b + 220)
    c.end(t, b + 500)

# A retried test: the earlier attempt's marked rows must not reach the last attempt's.
c = case("Z3-retry-with-a-marked-background")
c.start("z", "retried", 0, attempt=1)
c.step("z", "the account exists", "Given", 10, background=True, attempt=1)
c.step("z", "a basket", "When", 100, attempt=1)
c.pair("z", "basket", 120)
c.end("z", 300, status="failed", attempt=1, error="boom")
c.start("z", "retried", 1000, attempt=2)
c.step("z", "the account exists", "Given", 1010, background=True, attempt=2)
c.step("z", "a basket", "When", 1100, attempt=2)
c.pair("z", "basket", 1120)
c.step("z", "it is empty", "Then", 1200, attempt=2)
c.pair("z", "check", 1220)
c.end("z", 1500, attempt=2)

# A nested row with no top-level row before it: it becomes a top-level row, and level 1 draws no bar.
c = case("Z4-nested-row-first")
c.start("z", "nested first", 0)
c.step("z", "an inner step", None, 50, level=1)
c.pair("z", "inner", 60)
c.step("z", "a basket", "When", 100)
c.pair("z", "basket", 120)
c.step("z", "it is empty", "Then", 200)
c.pair("z", "check", 220)
c.end("z", 500)

# An assertion under a step, and one failing before the first step: the second is a row, with no bar.
c = case("Z5-failing-assertion-first")
c.start("z", "failing assertion first", 0)
c.assertion("z", "the stack is up", 5)
c.step("z", "a basket", "When", 100)
c.pair("z", "basket", 120)
c.assertion("z", "it answered", 130)
c.step("z", "it is empty", "Then", 200)
c.pair("z", "check", 220)
c.end("z", 500)

# A long label under a marked Background: both faults at once.
c = case("Z6-long-label-and-a-background")
long_text = "the customer places a very long order for many breakfasts with every topping there is and a side of something else again and again"
c.start("z", "both", 0)
c.step("z", "the account exists", "Given", 10, background=True)
c.step("z", long_text, "When", 100)
c.pair("z", "long", 120)
c.step("z", "it is empty", "Then", 200)
c.pair("z", "check", 220)
c.end("z", 500)

# The same Background text as a row's, marked in one row and not the other, and the row after it shares the text.
c = case("Z7-background-text-is-the-next-row")
c.start("z", "same text", 0)
c.step("z", "a shop", "Given", 10, background=True)
c.step("z", "a shop", "Given", 100)
c.pair("z", "own", 120)
c.step("z", "it is empty", "Then", 200)
c.pair("z", "check", 220)
c.end("z", 500)

# A test whose steps all drew no bar.
c = case("Z8-only-marked-rows")
c.start("z", "only marked", 0)
c.step("z", "the account exists", "Given", 10, background=True)
c.step("z", "something with no time", "When", None)
c.pair("z", "call", 120)
c.end("z", 500)

for c in cases:
    c.write()
print(" ".join(c.name for c in cases))
