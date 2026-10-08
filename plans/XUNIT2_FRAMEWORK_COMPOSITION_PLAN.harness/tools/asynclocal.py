"""Builds the per-test AsyncLocal visibility table from the prototype's instrumentation log.

Inputs: asynclocal.tsv (one row per event: starting, before, body, result, written by probe/Prototype/ProbeLog.cs),
asynclocal.scenarios.tsv (one row per ITest), and the run's TRX (for the path each test called).

An event the sink's AsyncLocal was not visible in is logged with test "?". It is tied back to its test anyway:
a body event by the path it was about to call (the TRX says which test called it), a Before event by the id
TestTracking's Before put on the flow, which the body event of the same test logs too.

Usage: python tools/asynclocal.py <asynclocal.tsv> <run.trx> [--json out.json]
"""
import argparse
import csv
import json
import pathlib
import re
import xml.etree.ElementTree as ET

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def kv(detail):
    return dict(re.findall(r"(\w+)=(\S+)", detail))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("log")
    ap.add_argument("trx")
    ap.add_argument("--json")
    args = ap.parse_args()

    path_to_test = {}
    for result in ET.parse(args.trx).getroot().iter(f"{NS}UnitTestResult"):
        out = result.find(f"{NS}Output")
        stdout = (out.findtext(f"{NS}StdOut") or "") if out is not None else ""
        m = re.search(r"probe-call: GET (\S+)", stdout)
        if m:
            path_to_test[m.group(1)] = result.get("testName")

    with open(args.log, encoding="utf-8") as fh:
        events = list(csv.DictReader(fh, delimiter="\t", quoting=csv.QUOTE_NONE))
    scenarios_file = pathlib.Path(args.log).with_suffix(".scenarios.tsv")
    befores_attr = {}
    if scenarios_file.exists():
        with open(scenarios_file, encoding="utf-8") as fh:
            for row in csv.DictReader(fh, delimiter="\t", quoting=csv.QUOTE_NONE):
                befores_attr[row["test"]] = row["beforeAttributes"]

    tests = {}

    def slot(name):
        return tests.setdefault(name, {"test": name})

    body_by_tracking = {}
    for e in events:
        if e["event"] == "body":
            d = kv(e["detail"])
            name = e["test"] if e["test"] != "?" else path_to_test.get(d.get("path"), "?path=" + d.get("path", ""))
            body_by_tracking[d.get("trackingId")] = name
            t = slot(name)
            t["bodyThread"], t["bodyVisible"], t["bodyMicros"] = int(e["thread"]), d.get("visible") == "1", int(e["micros"])
    options = [e["detail"] for e in events if e["event"] == "options"]
    for e in events:
        d = kv(e["detail"])
        if e["event"] == "starting":
            t = slot(e["test"])
            t["startingThread"], t["startingMicros"] = int(e["thread"]), int(e["micros"])
            t["previousOnFlow"] = e["detail"].split("previous=", 1)[1]
        elif e["event"] == "before":
            name = e["test"] if e["test"] != "?" else body_by_tracking.get(d.get("trackingId"), "?before:" + d.get("trackingId", "-"))
            t = slot(name)
            t["beforeThread"], t["beforeVisible"], t["beforeMicros"] = int(e["thread"]), d.get("visible") == "1", int(e["micros"])
            t["trackingIdSeenInBefore"] = d.get("trackingIdSeen") == "1"
        elif e["event"] == "result":
            t = slot(e["test"])
            t["resultThread"] = int(e["thread"])
            t["resultVisible"] = e["detail"].split("visible=", 1)[1].split(":", 1)[0]
    for name, t in tests.items():
        t["beforeAttributes"] = befores_attr.get(name, "")

    rows = sorted(tests.values(), key=lambda t: t["test"])
    print(f"options: {'; '.join(options)}")
    print(f"{'test':<70} {'start thr':>9} {'before thr':>10} {'vis':>4} {'us after start':>14} {'body thr':>8} {'vis':>4} {'result thr':>10} {'result sees':>11}  before attributes")
    for t in rows:
        def g(k, f=str):
            return f(t[k]) if k in t else "-"
        dt_us = str(t["beforeMicros"] - t["startingMicros"]) if "beforeMicros" in t and "startingMicros" in t else "-"
        print(f"{t['test'][:70]:<70} {g('startingThread'):>9} {g('beforeThread'):>10} {('yes' if t.get('beforeVisible') else 'no') if 'beforeVisible' in t else '-':>4} {dt_us:>14} "
              f"{g('bodyThread'):>8} {('yes' if t.get('bodyVisible') else 'no') if 'bodyVisible' in t else '-':>4} {g('resultThread'):>10} {g('resultVisible'):>11}  {t['beforeAttributes'] or '(no Before)'}")

    with_before = [t for t in rows if "beforeVisible" in t]
    with_body = [t for t in rows if "bodyVisible" in t]
    with_result = [t for t in rows if "resultVisible" in t]
    summary = {
        "options": options,
        "tests": len([t for t in rows if "startingThread" in t]),
        "beforeEvents": len(with_before),
        "visibleInBefore": sum(t["beforeVisible"] for t in with_before),
        "beforeOnStartingThread": sum(1 for t in with_before if t.get("beforeThread") == t.get("startingThread")),
        "bodyEvents": len(with_body),
        "visibleInBody": sum(t["bodyVisible"] for t in with_body),
        "resultEvents": len(with_result),
        "resultSame": sum(1 for t in with_result if t["resultVisible"] == "same"),
        "resultNull": sum(1 for t in with_result if t["resultVisible"] == "null"),
        "resultOther": sum(1 for t in with_result if t["resultVisible"] == "other"),
        "resultOnStartingThread": sum(1 for t in with_result if t.get("resultThread") == t.get("startingThread")),
        "noBefore": sorted(t["test"] for t in rows if "startingThread" in t and not t["beforeAttributes"]),
        "previousOnFlowNotNull": sum(1 for t in rows if t.get("previousOnFlow", "null") != "null"),
        "trackingIdSeenInBefore": sum(1 for t in with_before if t.get("trackingIdSeenInBefore")),
        "beforeMicrosAfterStarting": sorted(t["beforeMicros"] - t["startingMicros"] for t in rows if "beforeMicros" in t and "startingMicros" in t),
    }
    print()
    print(f"tests with ITestStarting: {summary['tests']}")
    print(f"Before:  {summary['visibleInBefore']} of {summary['beforeEvents']} saw the scenario set on ITestStarting; "
          f"{summary['beforeOnStartingThread']} ran on the thread ITestStarting was delivered on; "
          f"{summary['trackingIdSeenInBefore']} saw TestTracking's id (it ran first)")
    print(f"body:    {summary['visibleInBody']} of {summary['bodyEvents']} saw it")
    print(f"result:  {summary['resultSame']} of {summary['resultEvents']} saw the same scenario, {summary['resultNull']} saw none, "
          f"{summary['resultOther']} saw another test's; {summary['resultOnStartingThread']} delivered on the ITestStarting thread")
    print(f"ITestStarting found another test's scenario already on its flow: {summary['previousOnFlowNotNull']}")
    print(f"tests with ITestStarting and no Before ({len(summary['noBefore'])}): {', '.join(summary['noBefore'])}")
    if summary["beforeMicrosAfterStarting"]:
        v = summary["beforeMicrosAfterStarting"]
        print(f"Before after ITestStarting, microseconds: min {v[0]}, median {v[len(v) // 2]}, max {v[-1]}")
    if args.json:
        pathlib.Path(args.json).write_text(json.dumps({"summary": summary, "rows": rows}, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
