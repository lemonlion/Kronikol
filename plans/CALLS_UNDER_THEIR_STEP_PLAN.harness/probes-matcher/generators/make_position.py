#!/usr/bin/env python
"""Ingest cases for the walk's place: rows the tests file lists and draws no bar for. Each case is a separate ingest. No text overlaps here, unlike kronikol-fe's A, R and S.

Usage: make_position.py <out dir>
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

    def start(self, test, name, ms, feature="F"):
        self.tests.append(dict(event="start", testId=test, testName=name, feature=feature, timestamp=at(ms)))

    def end(self, test, ms, status="passed"):
        self.tests.append(dict(event="end", testId=test, status=status, durationMs=500, timestamp=at(ms)))

    def assertion(self, test, text, ms):
        self.tests.append(dict(event="assertion", testId=test, text=text, status="passed", timestamp=at(ms)))

    def bar(self, test, label, ms):
        rid = f"{test}-bar-{ms}"
        common = dict(kind="marker", markerKind="Step", type="Request", uri="http://marker/", serviceName="Shop", callerName="Tests", testId=test, requestResponseId=rid, timestamp=at(ms))
        self.inter.append(dict(common, plantUml="hnote across <<stepDelimiter>> #black:<color:white>" + label))
        self.inter.append(dict(common, markerEnd=True))

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


# The wiki's own shape: a Background marked in the tests file, the tests file draws the bars, and no text in common.
c = case("X1-marked-background")
c.start("x", "marked", 0)
c.step("x", "the account exists", "Given", 10, background=True)
c.pair("x", "seed", 20)
c.step("x", "a basket", "When", 100)
c.pair("x", "basket", 120)
c.step("x", "it is empty", "Then", 200)
c.pair("x", "check", 220)
c.end("x", 500)

# Two marked Background rows, and two scenarios, as a feature's would be.
c = case("X2-two-rows-two-scenarios")
for n in range(2):
    t, b = f"x{n}", n * 10000
    c.start(t, f"scenario {n}", b)
    c.step(t, "the account exists", "Given", b + 10, background=True)
    c.step(t, "the account is open", "And", b + 15, background=True)
    c.pair(t, "seed", b + 20)
    c.step(t, f"basket {n}", "When", b + 100)
    c.pair(t, "basket", b + 120)
    c.step(t, f"it is empty {n}", "Then", b + 200)
    c.pair(t, "check", b + 220)
    c.end(t, b + 500)

# An assertion ahead of the first step: a row of its own, and a note, no bar.
c = case("X3-assertion-first")
c.start("x", "assertion first", 0)
c.assertion("x", "the stack is up", 5)
c.step("x", "a basket", "When", 100)
c.pair("x", "basket", 120)
c.step("x", "it is empty", "Then", 200)
c.pair("x", "check", 220)
c.end("x", 500)

# An untimed step between two timed ones: a row, and no bar, as a bar needs a place.
c = case("X4-untimed-row-between")
c.start("x", "untimed between", 0)
c.step("x", "a basket", "Given", 100)
c.pair("x", "basket", 120)
c.step("x", "something with no time", "And", None)
c.step("x", "it is sent", "When", 200)
c.pair("x", "send", 220)
c.step("x", "it is empty", "Then", 300)
c.pair("x", "check", 320)
c.end("x", 500)

# A marked Background whose text the first step's holds, kronikol-fe's A, one scenario.
c = case("X5-marked-background-text-in-the-bar")
c.start("x", "marked, text overlaps", 0)
c.step("x", "a customer", "Given", 10, background=True)
c.step("x", "a customer places an order", "When", 100)
c.pair("x", "orders", 120)
c.step("x", "it is confirmed", "Then", 200)
c.pair("x", "confirm", 220)
c.end("x", 500)

# A step with no text, and a marked Background: both at once.
c = case("X6-no-text-and-a-background")
c.start("x", "both", 0)
c.step("x", "the account exists", "Given", 10, background=True)
c.step("x", None, "When", 100)
c.pair("x", "a", 120)
c.step("x", "it is empty", "Then", 200)
c.pair("x", "b", 220)
c.end("x", 500)

# Controls: the capture draws the bars, so a marked row has one, and the mark must not move the walk.
c = case("Y1-store-draws-a-marked-background")
c.start("y", "store", 0)
c.step("y", "the account exists", "Given", 10, background=True)
c.bar("y", "Given the account exists", 10)
c.pair("y", "seed", 20)
c.step("y", "a basket", "When", 100)
c.bar("y", "When a basket", 100)
c.pair("y", "basket", 120)
c.step("y", "it is empty", "Then", 200)
c.bar("y", "Then it is empty", 200)
c.pair("y", "check", 220)
c.end("y", 500)

# A store whose tests file has an assertion first: the capture's bars still have one row each, the assertion's none.
c = case("Y2-store-with-assertion-first")
c.start("y", "store with an assertion", 0)
c.assertion("y", "the stack is up", 5)
c.step("y", "a basket", "When", 100)
c.bar("y", "When a basket", 100)
c.pair("y", "basket", 120)
c.step("y", "it is empty", "Then", 200)
c.bar("y", "Then it is empty", 200)
c.pair("y", "check", 220)
c.end("y", 500)

# A store whose rows have no times: the capture's bars are the rows'.
c = case("Y3-store-with-untimed-rows")
c.start("y", "store untimed", 0)
c.step("y", "a basket", "Given", None)
c.step("y", "it is sent", "When", None)
c.bar("y", "Given a basket", 100)
c.pair("y", "basket", 120)
c.bar("y", "When it is sent", 200)
c.pair("y", "send", 220)
c.end("y", 500)

# A control with nothing to pass over.
c = case("Y4-plain")
c.start("y", "plain", 0)
c.step("y", "a basket", "When", 100)
c.pair("y", "basket", 120)
c.step("y", "it is empty", "Then", 200)
c.pair("y", "check", 220)
c.end("y", 500)

for c in cases:
    c.write()
print(" ".join(c.name for c in cases))
