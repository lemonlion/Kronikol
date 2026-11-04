"""#129's forced read, as #223's prototype/apply_r7.py has it, on a tree holding R2: the attribution runs before the one copy of the diagnostics every output reads. Not part of any release here. It measures which healthy shapes would still be flagged.

Usage: apply_forced_read.py <worktree>
"""
import pathlib
import sys

source = pathlib.Path(sys.argv[1]) / "src/Kronikol/Reports/ReportGenerator.cs"
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
print("forced read applied")
