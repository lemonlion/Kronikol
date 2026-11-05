"""R7: #129's order, which no earlier stage held. python -I apply_r7.py <checkout> [--fact-only]

The fact comes first, so it can be seen red: --fact-only writes it alone. Then run again without the flag.
"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
fact_only = "--fact-only" in sys.argv

tests = root / "tests/Kronikol.Tests/Reports/ReportAttributionTests.cs"
t = tests.read_text(encoding="utf-8")
if "A_mismatched_step_marker_reaches_the_report_it_happened_in" not in t:
    start = t.index("    [Fact]\n    public void A_mismatched_step_marker_is_reported_once_per_run_not_once_per_output()")
    end = t.index("    }\n", t.index('Directory.Delete(directory, true)', start))
    end = t.index("    }\n", end + 1) + len("    }\n")
    fact = t[start:end]
    copy = fact.replace("A_mismatched_step_marker_is_reported_once_per_run_not_once_per_output", "A_mismatched_step_marker_reaches_the_report_it_happened_in")
    old = "            Assert.Single(collector.Entries, e => e.Kind == DiagnosticKind.StepAttributionMismatch);\n"
    assert copy.count(old) == 1
    copy = copy.replace(old, (
        "            // #129: every output reads one copy of the diagnostics, which was taken before the attribution that records this one\n"
        '            using var json = JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(directory, "TestRunReport.json")));\n'
        '            Assert.Single(ReportFile.Items(json.RootElement, "diagnostics"), d => d.GetProperty("kind").GetString() == nameof(DiagnosticKind.StepAttributionMismatch));\n'))
    t = t[:end] + "\n" + copy + t[end:]
    tests.write_text(t, encoding="utf-8", newline="\n")

if not fact_only:
    source = root / "src/Kronikol/Reports/ReportGenerator.cs"
    s = source.read_bytes().decode("utf-8")
    newline = "\r\n" if "\r\n" in s else "\n"
    s = s.replace("\r\n", "\n")
    copy_line = "        var reportDiagnostics = ReportDiagnosticsScope.Current?.Entries ?? [];\n\n"
    assert s.count(copy_line) == 1
    s = s.replace(copy_line, "")
    anchor = "            () => AttributeInteractionsToSteps(dataLogs, features));\n\n"
    assert s.count(anchor) == 1
    s = s.replace(anchor, (
        "            () => AttributeInteractionsToSteps(dataLogs, features));\n\n"
        "        // Before the one copy of the diagnostics every output reads, or a mismatch reaches none of them (#129).\n"
        "        if (options.GenerateTestRunReportData || options.GenerateFailuresDigest)\n"
        "            _ = attribution.Value;\n\n"
        "        var reportDiagnostics = ReportDiagnosticsScope.Current?.Entries ?? [];\n\n"))
    source.write_bytes(s.replace("\n", newline).encode("utf-8"))
print("ok")
