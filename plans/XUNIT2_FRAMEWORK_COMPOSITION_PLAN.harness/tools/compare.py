"""Compares one run's xUnit results (TRX) with the Kronikol report it wrote (TestRunReport.json).

Every probe test writes "probe-call: GET <path>" to its output before its one tracked call, so the TRX says
which path each test called, and the report says which scenario holds that call. A test is mapped to the
scenario holding its path. A test whose path no scenario holds (it never reached its call: skipped,
constructor, InitializeAsync or class fixture failure; or its call was attributed to an id the report has no
scenario for) is mapped by feature and name to a scenario with no calls, when one has the names it should have.

Prints a table (one row per xUnit test) and the lists of tests with no scenario and scenarios with no test,
whether each unplaced call appears anywhere in the report file, the run's start and end in the report against
the TRX's, and the paths in the report's background section. --json writes the same facts for
tools/summarize.py; --extract writes the few report fields this script reads (report-extract.json), which
--report also takes, so a run can be compared again after its report directory is gone.

TestRunReport.json is read here and only here, field by field: it is never printed.

Usage: python tools/compare.py --trx run.trx --report <Reports dir | TestRunReport.json | report-extract.json>
                               [--json compare.json] [--extract report-extract.json] [--label text]
"""
import argparse
import datetime as dt
import json
import pathlib
import re
import sys
import urllib.parse
import xml.etree.ElementTree as ET

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
CALL = re.compile(r"probe-call: GET (\S+)")
SHARED = re.compile(r"shared-thing: (\S+) created=(\d+)")
DI = re.compile(r"di-service: (\S+) (.+)")
OUTCOME = {"Passed": "Passed", "Failed": "Failed", "NotExecuted": "Skipped"}


def parse_time(text):
    if not text:
        return None
    text = text.strip()
    m = re.match(r"^(.*?\.\d{1,6})\d*(Z|[+-]\d\d:\d\d)?$", text)  # TRX writes 7 fractional digits
    if m:
        text = m.group(1) + (m.group(2) or "")
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    value = dt.datetime.fromisoformat(text)
    if value.tzinfo is None:
        value = value.replace(tzinfo=dt.timezone.utc)
    return value.astimezone(dt.timezone.utc)


def iso(value):
    return value.strftime("%H:%M:%S.%f")[:-3] + "Z" if value else "-"


def trx_seconds(text):
    if not text:
        return None
    h, m, s = text.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def split_pascal(text):
    text = re.sub(r"([a-z])([A-Z])", r"\1 \2", text)
    return re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1 \2", text)


def kronikol_format(display_name):
    """Port of ScenarioTitleResolver.FormatScenarioDisplayName. None where the C# throws (nothing after the last '.')."""
    paren = display_name.find("(")
    params = None
    if paren >= 0:
        method_path = display_name[:paren]
        content = display_name[paren + 1:].rstrip(")")
        if content:
            params = content if len(content) <= 200 else content[:200] + "\u2026"
    else:
        method_path = display_name
    dot = method_path.rfind(".")
    name = method_path[dot + 1:] if dot >= 0 else method_path
    human = re.sub(r"\s+", " ", split_pascal(name).replace("_", " ")).strip()
    if not human:
        return None
    human = human[0].upper() + human[1:].lower()
    return f"{human} [{params}]" if params is not None else human


def expected_name(test):
    """What a correct report calls the test: a [Fact(DisplayName)] as written, anything else as the formatter would."""
    name = test["name"]
    head = name.split("(")[0]
    if " " in head:
        return name
    # methodDisplay=method drops namespace and class from the display name; the formatter keeps only the method anyway.
    return kronikol_format(name) or name


def expected_feature(test):
    simple = (test["className"] or "").split(".")[-1].split("+")[-1]
    return " ".join(w[:1].upper() + w[1:] for w in split_pascal(simple).replace("_", " ").split())


def read_trx(path):
    root = ET.parse(path).getroot()
    times = root.find(f"{NS}Times").attrib
    methods = {}
    for unit in root.iter(f"{NS}UnitTest"):
        tm = unit.find(f"{NS}TestMethod")
        if tm is not None:
            methods[unit.get("id")] = (tm.get("className"), tm.get("name"))
    tests = []
    for result in root.iter(f"{NS}UnitTestResult"):
        if result.find(f"{NS}InnerResults") is not None:
            continue  # an aggregate; its inner results are listed on their own
        out = result.find(f"{NS}Output")
        stdout = (out.findtext(f"{NS}StdOut") or "") if out is not None else ""
        message = (out.findtext(f"{NS}ErrorInfo/{NS}Message") or "") if out is not None else ""
        calls = CALL.findall(stdout)
        shared = SHARED.findall(stdout)
        di = DI.findall(stdout)
        class_name, method = methods.get(result.get("testId"), (None, None))
        tests.append({
            "name": result.get("testName"),
            "className": class_name,
            "method": method,
            "fqn": f"{class_name}.{method}" if class_name else result.get("testName"),
            "outcome": result.get("outcome"),
            "seconds": trx_seconds(result.get("duration")),
            "start": iso(parse_time(result.get("startTime"))),
            "end": iso(parse_time(result.get("endTime"))),
            "path": calls[0] if calls else None,
            "error": message.strip().splitlines()[0] if message.strip() else "",
            "sharedThing": shared[0] if shared else None,
            "diService": di[0] if di else None,
        })
    return {
        "start": parse_time(times.get("start")),
        "finish": parse_time(times.get("finish")),
        "tests": tests,
    }


def path_of(uri):
    return urllib.parse.urlsplit(uri).path


def read_report(location, paths):
    file = pathlib.Path(location)
    if file.is_dir():
        file = file / "TestRunReport.json"
    if not file.exists():
        return None
    raw = file.read_text(encoding="utf-8-sig")
    doc = json.loads(raw)
    if doc.get("extractOf") == "TestRunReport.json":
        return doc
    scenarios = []
    for feature in doc.get("features") or []:
        for s in feature.get("scenarios") or []:
            calls = [path_of(i["uri"]) for i in s.get("httpInteractions") or [] if i.get("type") == "Request"]
            message = (s.get("errorMessage") or "").strip()
            scenarios.append({
                "id": s.get("id"),
                "feature": feature.get("name"),
                "name": s.get("name"),
                "result": s.get("result"),
                "seconds": s.get("durationSeconds"),
                "endedAt": s.get("endedAt"),
                "error": message.splitlines()[0] if message else "",
                "calls": calls,
            })
    background = doc.get("background") or {}
    diagnostics = {}
    for d in doc.get("diagnostics") or []:
        diagnostics[d.get("kind")] = diagnostics.get(d.get("kind"), 0) + 1
    return {
        "extractOf": "TestRunReport.json",
        "startText": doc.get("startTime"),
        "endText": doc.get("endTime"),
        "scenarios": scenarios,
        "background": [{"path": path_of(i["uri"]), "expiredFrom": i.get("expiredFrom"), "attribution": i.get("attributionSource")}
                       for i in background.get("interactions") or [] if i.get("type") == "Request"],
        "backgroundCount": background.get("calls"),
        "diagnostics": diagnostics,
        # How often each test's path occurs anywhere in the file (interactions, diagrams, anything).
        "pathOccurrences": {p: raw.count(p) for p in sorted(paths)},
    }


def compare(trx, report):
    rows, used = [], set()
    by_id = {s["id"]: s for s in report["scenarios"]} if report else {}

    def by_names(test, reason):
        want_name, want_feature = expected_name(test).lower(), expected_feature(test).lower()
        named = [s for s in report["scenarios"] if not s["calls"] and s["id"] not in used
                 and (s["name"] or "").lower() == want_name and (s["feature"] or "").lower() == want_feature]
        return (named[0], reason) if named else (None, None)

    for test in sorted(trx["tests"], key=lambda t: (t["fqn"], t["name"])):
        row = {"test": test, "scenario": None, "via": None, "flags": [], "notes": []}
        if report:
            if test["path"]:
                owners = [s for s in report["scenarios"] if test["path"] in s["calls"]]
                expired = [b for b in report["background"] if b["path"] == test["path"]]
                if len(owners) == 1:
                    row["scenario"], row["via"] = owners[0], "call"
                elif len(owners) > 1:
                    row["scenario"], row["via"] = owners[0], f"call (also in {len(owners) - 1} more)"
                    row["flags"].append("CALL-IN-SEVERAL")
                elif expired and expired[0]["expiredFrom"] in by_id:
                    row["scenario"], row["via"] = by_id[expired[0]["expiredFrom"]], "call moved to background"
                    row["flags"].append("CALL-IN-BACKGROUND")
                else:
                    row["scenario"], row["via"] = by_names(test, "names (its call is in no scenario)")
                    if row["scenario"] is not None:
                        row["flags"].append("CALL-MISSING")
                    if not report["pathOccurrences"].get(test["path"]):
                        row["notes"].append("call-nowhere-in-TestRunReport.json")
            else:
                row["scenario"], row["via"] = by_names(test, "names (test made no call)")
        s = row["scenario"]
        if s is None:
            row["flags"].append("NO-SCENARIO")
        else:
            used.add(s["id"])
            if OUTCOME.get(test["outcome"], test["outcome"]) != s["result"]:
                row["flags"].append("RESULT")
            if s["name"] is None or s["name"].lower() != expected_name(test).lower():
                row["flags"].append("NAME")
            trx_s, rep_s = test["seconds"] or 0.0, s["seconds"] or 0.0
            # The TRX logger writes 1 ms for a test xUnit timed at 0 (skips, constructor and fixture failures).
            if not (trx_s <= 0.0011 and rep_s == 0) and abs(trx_s - rep_s) > max(0.002, 0.02 * trx_s):
                row["flags"].append("DURATION")
            if s["result"] == "Failed" and test["error"] and s["error"] and test["error"] != s["error"]:
                # The TRX's first line is "<exception type> : <message>" for an exception that is not an
                # assertion; the report keeps ITestFailed.Messages alone. Same error, type left out: a note.
                if test["error"].endswith(" : " + s["error"]):
                    row["notes"].append("error-type-not-in-report")
                else:
                    row["flags"].append("ERROR-TEXT")
            if test["path"] and s["calls"] and set(s["calls"]) != {test["path"]}:
                row["flags"].append("FOREIGN-CALLS")
        rows.append(row)
    unmatched = [s for s in (report["scenarios"] if report else []) if s["id"] not in used]
    return rows, unmatched


def fmt_seconds(value):
    return "-" if value is None else f"{value:.3f}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--trx", required=True)
    ap.add_argument("--report", required=True)
    ap.add_argument("--json")
    ap.add_argument("--extract")
    ap.add_argument("--label", default="")
    args = ap.parse_args()

    trx = read_trx(args.trx)
    report = read_report(args.report, {t["path"] for t in trx["tests"] if t["path"]})
    if report and args.extract:
        pathlib.Path(args.extract).write_text(json.dumps(report, indent=1), encoding="utf-8")
    rows, unmatched = compare(trx, report)

    out = []
    p = out.append
    p(f"# compare {args.label}".rstrip())
    p(f"TRX: {len(trx['tests'])} test results; report: " + (f"{len(report['scenarios'])} scenarios" if report else "NO TestRunReport.json"))
    p("")
    p("flags: RESULT, NAME, DURATION, ERROR-TEXT = the scenario disagrees with the test; FOREIGN-CALLS = it holds another test's call;")
    p("       CALL-IN-BACKGROUND = the test's call is listed as a background call; CALL-MISSING = matched by names, its call is in no")
    p("       scenario; NO-SCENARIO = no scenario holds the test's call, and none has its feature and name")
    p(f"{'xUnit test (TRX)':<62} {'outcome':<11} {'secs':>6}  {'via':<26} {'scenario (report)':<46} {'result':<8} {'secs':>6}  flags")
    for r in rows:
        t, s = r["test"], r["scenario"]
        p(f"{t['name'][:62]:<62} {t['outcome']:<11} {fmt_seconds(t['seconds']):>6}  {(r['via'] or '-')[:26]:<26} "
          f"{(s['name'] if s else '-')[:46]:<46} {(s['result'] if s else '-'):<8} {fmt_seconds(s['seconds']) if s else '-':>6}  {' '.join(r['flags']) or 'ok'}"
          + (f"  ({', '.join(r['notes'])})" if r["notes"] else ""))
    p("")
    p("Per test: the call it made, the calls under its scenario, and the first line of each error")
    for r in rows:
        t, s = r["test"], r["scenario"]
        p(f"- {t['name']}  [{t['fqn']}]")
        p(f"    test call: {t['path'] or '(none: never reached its body)'}; scenario: "
          + (f"{s['feature']} > {s['name']}, calls {', '.join(s['calls']) if s['calls'] else '(none)'}" if s else "(none)"))
        if t["path"] and report and not any(t["path"] in x["calls"] for x in report["scenarios"]):
            p(f"    the call's path occurs {report['pathOccurrences'].get(t['path'], 0)} time(s) anywhere in TestRunReport.json")
        if t["error"] or (s and s["error"]):
            p(f"    TRX error: {t['error'] or '-'}")
            p(f"    report error: {(s['error'] if s else '') or '-'}")
        if t["sharedThing"]:
            p(f"    assembly fixture: instance {t['sharedThing'][0]}, created {t['sharedThing'][1]} time(s)")
        if t["diService"]:
            p(f"    injected service: instance {t['diService'][0]}, says \"{t['diService'][1].strip()}\"")
    p("")
    no_scenario = [r["test"]["name"] for r in rows if r["scenario"] is None]
    p(f"xUnit tests with no scenario ({len(no_scenario)}):")
    for name in no_scenario:
        p(f"  - {name}")
    p(f"Scenarios with no xUnit test ({len(unmatched)}):")
    for s in unmatched:
        p(f"  - {s['feature']} > {s['name']} [{s['result']}, {fmt_seconds(s['seconds'])}s, calls: {', '.join(s['calls']) or 'none'}]")
    p("")
    if report:
        results = {}
        for s in report["scenarios"]:
            key = f"{s['result']}{'' if s['seconds'] else ' (no duration)'}"
            results[key] = results.get(key, 0) + 1
        p("Scenario results in the report: " + ", ".join(f"{k} {v}" for k, v in sorted(results.items())))
        p("")
    test_starts = sorted(t["start"] for t in trx["tests"] if t["start"] != "-")
    test_ends = sorted(t["end"] for t in trx["tests"] if t["end"] != "-")
    p("Run times (UTC):")
    p(f"  TRX run:            start {iso(trx['start'])}  finish {iso(trx['finish'])}  ({(trx['finish'] - trx['start']).total_seconds():.3f}s)")
    if test_starts:
        p(f"  TRX tests:          first start {test_starts[0]}  last end {test_ends[-1]}")
    if report:
        start, end = parse_time(report["startText"]), parse_time(report["endText"])
        span = (end - start).total_seconds() if start and end else None
        p(f"  report:             start {report['startText']}  end {report['endText']}  (end - start = {span}s; the report writes whole seconds)")
        ended = sorted(s["endedAt"] for s in report["scenarios"] if s["endedAt"])
        if ended:
            p(f"  report scenarios:   earliest endedAt {ended[0]}  latest endedAt {ended[-1]}")
            p(f"  scenarios whose endedAt is before the report's start: {sum(1 for e in ended if parse_time(e) < start)} of {len(ended)}")
    p("")
    if report:
        p(f"Background section: {report['backgroundCount'] or 0} call(s)")
        names = {s["id"]: s["name"] for s in report["scenarios"]}
        for b in report["background"]:
            p(f"  - {b['path']}  (attribution {b['attribution']}, expired from: {names.get(b['expiredFrom'], b['expiredFrom'] or '-')})")
        p("Report diagnostics: " + (", ".join(f"{k} x{v}" for k, v in sorted(report["diagnostics"].items())) or "none"))
    print("\n".join(out))

    if args.json:
        payload = {
            "label": args.label,
            "trxStart": iso(trx["start"]), "trxFinish": iso(trx["finish"]),
            "trxFirstTestStart": test_starts[0] if test_starts else None, "trxLastTestEnd": test_ends[-1] if test_ends else None,
            "reportStart": report["startText"] if report else None, "reportEnd": report["endText"] if report else None,
            "reportWritten": report is not None,
            "scenarioCount": len(report["scenarios"]) if report else 0,
            "scenarioResults": {f"{s['result']}{'' if s['seconds'] else ' (no duration)'}": sum(
                1 for x in report["scenarios"] if x["result"] == s["result"] and bool(x["seconds"]) == bool(s["seconds"]))
                for s in report["scenarios"]} if report else {},
            "earliestEndedAt": min((s["endedAt"] for s in report["scenarios"] if s["endedAt"]), default=None) if report else None,
            "rows": [{
                "test": r["test"]["name"], "fqn": r["test"]["fqn"], "outcome": r["test"]["outcome"], "seconds": r["test"]["seconds"],
                "path": r["test"]["path"], "testError": r["test"]["error"], "sharedThing": r["test"]["sharedThing"],
                "diService": r["test"]["diService"],
                "via": r["via"], "flags": r["flags"], "notes": r["notes"],
                "scenario": None if r["scenario"] is None else {k: r["scenario"][k] for k in ("id", "feature", "name", "result", "seconds", "error", "calls")},
            } for r in rows],
            "unmatchedScenarios": [{k: s[k] for k in ("feature", "name", "result", "seconds", "calls")} for s in unmatched],
            "background": report["background"] if report else [],
            "diagnostics": report["diagnostics"] if report else {},
        }
        pathlib.Path(args.json).write_text(json.dumps(payload, indent=1), encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main())
