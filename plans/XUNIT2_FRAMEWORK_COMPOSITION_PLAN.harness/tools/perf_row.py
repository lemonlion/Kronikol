"""One row of results/perf/perf.tsv from one perf run (perf.ps1 calls it after each run).

Columns:
  wall_s           wall time of `dotnet test --no-build` (perf.ps1's stopwatch): build evaluation, test host,
                   discovery, the run, the report, shutdown
  xunit_s          the xUnit adapter's own stamps, "[xUnit.net hh:mm:ss.ff] Finished:" minus "Starting:".
                   C writes its report before forwarding ITestAssemblyFinished, so for C this includes the
                   report; A writes it after, so for A it does not
  vstest_total_s   VSTest's "Total time" line
  window_s         first test start to last test end in the TRX (the adapter stamps a result when it gets it)
  assembly_s       ITestAssemblyFinished.ExecutionTime, xUnit's own time for the assembly (C only: the sink logs it)
  report_ms        how long XUnit2ReportGenerator.CreateStandardReportsWithDiagrams took (C only)
  scenarios        Run.json's scenario count; passed/failed/total from the TRX's counters

Usage: python tools/perf_row.py --header | --lane L --round R --exit E --wall S --console F --trx F --timing F --reports DIR
"""
import argparse
import datetime as dt
import json
import pathlib
import re
import xml.etree.ElementTree as ET

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
COLUMNS = ["lane", "round", "exit", "wall_s", "xunit_s", "vstest_total_s", "window_s", "assembly_s", "report_ms",
           "scenarios", "passed", "failed", "total"]


def stamp(text):
    h, m, s = text.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def when(text):
    m = re.match(r"^(.*?\.\d{1,6})\d*([+-]\d\d:\d\d|Z)?$", text)
    text = m.group(1) + (m.group(2) or "") if m else text
    return dt.datetime.fromisoformat(text.replace("Z", "+00:00"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--header", action="store_true")
    for name in ("lane", "round", "exit", "wall", "console", "trx", "timing", "reports"):
        ap.add_argument(f"--{name}")
    a = ap.parse_args()
    if a.header:
        print("\t".join(COLUMNS))
        return

    row = dict.fromkeys(COLUMNS, "")
    row.update(lane=a.lane, round=a.round, exit=a.exit, wall_s=f"{float(a.wall):.3f}")

    console = pathlib.Path(a.console).read_text(encoding="utf-8", errors="replace") if a.console and pathlib.Path(a.console).exists() else ""
    start = re.search(r"\[xUnit\.net (\d\d:\d\d:\d\d\.\d+)\]\s+Starting:", console)
    finish = re.search(r"\[xUnit\.net (\d\d:\d\d:\d\d\.\d+)\]\s+Finished:", console)
    if start and finish:
        row["xunit_s"] = f"{stamp(finish.group(1)) - stamp(start.group(1)):.3f}"
    total = re.search(r"Total time: ([\d.]+) (Seconds|Minutes)", console)
    if total:
        row["vstest_total_s"] = f"{float(total.group(1)) * (60 if total.group(2) == 'Minutes' else 1):.3f}"

    if a.trx and pathlib.Path(a.trx).exists():
        root = ET.parse(a.trx).getroot()
        counters = root.find(f"{NS}ResultSummary/{NS}Counters")
        if counters is not None:
            row.update(passed=counters.get("passed"), failed=counters.get("failed"), total=counters.get("total"))
        starts, ends = [], []
        for r in root.iter(f"{NS}UnitTestResult"):
            if r.get("startTime") and r.get("endTime"):
                starts.append(when(r.get("startTime")))
                ends.append(when(r.get("endTime")))
        if starts:
            row["window_s"] = f"{(max(ends) - min(starts)).total_seconds():.3f}"

    if a.timing and pathlib.Path(a.timing).exists():
        text = pathlib.Path(a.timing).read_text(encoding="utf-8")
        m = re.search(r"assemblyExecutionSeconds=([\d.]+) reportMilliseconds=(\d+)", text)
        if m:
            row.update(assembly_s=m.group(1), report_ms=m.group(2))

    run = pathlib.Path(a.reports or "") / "Run.json"
    if a.reports and run.exists():
        row["scenarios"] = json.load(open(run, encoding="utf-8-sig")).get("scenarios", "")

    print("\t".join(str(row[c]) for c in COLUMNS))


if __name__ == "__main__":
    main()
