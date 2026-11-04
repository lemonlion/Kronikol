"""An ingest lane's own shapes, for the plan's claims about it: a nested step (level 1), and a label the bar changes.

Writes interactions.ndjson and tests.ndjson beside itself. Each scenario has a call in each stretch, timed inside its step.
"""
import json
import pathlib
import uuid

here = pathlib.Path(__file__).parent
T0 = "2026-11-03T10:00:{:06.3f}Z"


def at(seconds):
    return T0.format(seconds)


interactions = []
tests = []


def call(test, name, path, seconds):
    rid = str(uuid.uuid4())
    base = dict(testId=test, testName=name, method="GET", uri=f"http://svc/{path}", serviceName="svc", callerName="Caller",
                traceId=str(uuid.uuid4()), requestResponseId=rid)
    interactions.append(dict(base, type="Request", timestamp=at(seconds)))
    interactions.append(dict(base, type="Response", timestamp=at(seconds + 0.05), statusCode="200"))


def scenario(test, name, steps, calls):
    tests.append(dict(event="start", testId=test, testName=name, feature="Ingest shapes", timestamp=at(0)))
    for s in steps:
        tests.append(dict(event="step", testId=test, status="passed", **s))
    tests.append(dict(event="end", testId=test, status="passed", timestamp=at(59)))
    for path, seconds in calls:
        call(test, name, path, seconds)


LONG = "the customer places a very long order for many breakfasts with every topping there is and a side of something else again and again"
BIG = " ".join(["word"] * 400)
scenario("nested", "A nested step's calls", [
    dict(text="the parent", keyword="Given", timestamp=at(1), durationMs=5000),
    dict(text="the first child", level=1, timestamp=at(2), durationMs=1000),
    dict(text="the second child", level=1, timestamp=at(4), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(7), durationMs=1000),
], [("parent-own", 1.5), ("first-child", 2.5), ("second-child", 4.5), ("next", 7.5)])
scenario("deep", "Two levels down", [
    dict(text="the parent", keyword="Given", timestamp=at(1), durationMs=5000),
    dict(text="the child", level=1, timestamp=at(2), durationMs=3000),
    dict(text="the grandchild", level=2, timestamp=at(3), durationMs=1000),
], [("child", 2.5), ("grandchild", 3.5)])
scenario("long", "A label over 110 characters", [
    dict(text=LONG, keyword="Given", timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("long-label", 1.5), ("next", 3.5)])
scenario("backslash", "A label holding a backslash", [
    dict(text=r"the file C:\temp\orders.json is read", keyword="Given", timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("backslash", 1.5), ("next", 3.5)])
scenario("param", "A label with LightBDD's table parameter", [
    dict(text="the table <$rows> is read", keyword="Given", timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("param", 1.5), ("next", 3.5)])
scenario("creole", "A label of creole pairs", [
    dict(text="a **bold** and __under__ and --strike-- step", keyword="Given", timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("creole", 1.5), ("next", 3.5)])
scenario("table", "A step with a table", [
    dict(text="a step with a table", keyword="Given", timestamp=at(1), durationMs=1000, table=[["a", "b"], ["1", "2"]]),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("table", 1.5), ("next", 3.5)])
scenario("huge", "A label past the bar's cap", [
    dict(text=BIG, keyword="Given", timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("huge", 1.5), ("next", 3.5)])
scenario("lower", "No keyword, and a lower case first letter", [
    dict(text="lower case and no keyword at all", timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("lower", 1.5), ("next", 3.5)])
scenario("newline", "A label with a line break", [
    dict(text="first line\nsecond line", keyword="Given", timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("newline", 1.5), ("next", 3.5)])

# Added for the two matchers on one walk (results/matchers-compared.txt): what each does to a label the other handles.
for test, name, text in [
    ("tilde", "A label holding a tilde", "the ~ tilde and ~~wave~~ step"),
    ("percent", "A label with a builtin call", "today is %date() in the report"),
    ("separator", "A label with a line separator", "before\u2028after the separator"),
    ("tiny", "One-letter words past the bar's cap", " ".join(["a"] * 700)),
    ("markup2", "A second line the bar escapes", "first line\n<b>second</b> ~line"),
    ("tab", "Tabs, long enough to wrap", "\t".join(["word"] * 60)),
    ("late", "A backslash past the opening", "x" * 70 + " C:" + chr(92) + "temp"),
    ("slashes", "Backslashes the bar cuts, as it does not wrap them", chr(92).join(["x"] * 80)),
]:
    scenario(test, name, [
        dict(text=text, keyword="Given", timestamp=at(1), durationMs=1000),
        dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
    ], [(test, 1.5), ("next", 3.5)])

# Labels the bar really cuts, since none of those above is: long ones wrap, which picks the styled form and its budget of about 16,000.
scenario("cut-styled", "A label past the styled bar's budget", [
    dict(text=" ".join(["word"] * 4000), keyword="Given", timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("cut-styled", 1.5), ("next", 3.5)])
scenario("cut-coloured", "One word the wrapper will not cut, past the coloured bar's cap", [
    dict(text="<" + "x" * 700, timestamp=at(1), durationMs=1000),
    dict(text="the next", keyword="When", timestamp=at(3), durationMs=1000),
], [("cut-coloured", 1.5), ("next", 3.5)])

(here / "interactions.ndjson").write_text("\n".join(json.dumps(r) for r in interactions) + "\n", encoding="utf-8", newline="\n")
(here / "tests.ndjson").write_text("\n".join(json.dumps(r) for r in tests) + "\n", encoding="utf-8", newline="\n")
print(len(interactions), "interaction records,", len(tests), "test records")
