"""R2's mutations (F4, F5 and Q6's merger fix): each breaks one behaviour, and the facts that must turn red are listed.

Run from a scratch worktree holding R1's changes, never the release worktree: each mutation is applied to the
file's text and the file is written back from memory afterwards (no git checkout).

    python mutate.py <worktree> <results-file>
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
ATTR = "src/Kronikol/Ingestion/IngestAttribution.cs"
PIPE = "src/Kronikol/Ingestion/IngestPipeline.cs"
CLI = "src/Kronikol.Tool/IngestCommand.cs"
FILTER = ("FullyQualifiedName~IngestPhaseFromStepsTests|FullyQualifiedName~Ingestion.IngestAttributionTests"
          "|FullyQualifiedName~Tool.IngestCommandTests")
MERGER = "src/Kronikol/Ingestion/InteractionMerger.cs"
LINE = "if (attributeByWindow)\n                @out.WriteLine($\"--attribute-by-window: "

MUTATIONS = [
    ("N1 the window pass's success recorded as UnattributedInteractions again", "1, 3, 8", [
        (PIPE, "            (records, windowAttributedRecords, ambiguous) = IngestAttribution.AttributeByWindow(\n                records, windows, request.WindowAttribution, request.WindowAttributionFallbackId);\n",
               "            (records, windowAttributedRecords, ambiguous) = IngestAttribution.AttributeByWindow(\n                records, windows, request.WindowAttribution, request.WindowAttributionFallbackId);\n"
               "            if (windowAttributedRecords > 0)\n                diagnostics.Add(DiagnosticKind.UnattributedInteractions, $\"{windowAttributedRecords} interaction record(s) attributed to a test by time window.\");\n")]),
    ("N2 the claims pass's success recorded again", "2", [
        (PIPE, "var (claimedRecords, _, contested, contests) = IngestAttribution.AttributeByClaimsNamingContests(records, claimWindows, request.WindowAttributionFallbackId);\n            records = claimedRecords;\n",
               "var (claimedRecords, claimed, contested, contests) = IngestAttribution.AttributeByClaimsNamingContests(records, claimWindows, request.WindowAttributionFallbackId);\n            records = claimedRecords;\n"
               "            if (claimed > 0)\n                diagnostics.Add(DiagnosticKind.UnattributedInteractions, $\"{claimed} interaction record(s) attributed to a test by content claims.\");\n")]),
    ("N3 ExclusiveOnly recorded at zero again", "4", [
        (PIPE, "if (request.WindowAttribution == WindowAttributionMode.ExclusiveOnly && ambiguous > 0)",
               "if (request.WindowAttribution == WindowAttributionMode.ExclusiveOnly)")]),
    ("N4 ExclusiveOnly never recorded", "4", [
        (PIPE, "if (request.WindowAttribution == WindowAttributionMode.ExclusiveOnly && ambiguous > 0)",
               "if (request.WindowAttribution == WindowAttributionMode.ExclusiveOnly && ambiguous < 0)")]),
    ("N5 the window line dropped", "5, 6", [
        (CLI, LINE, LINE.replace("if (attributeByWindow)", "if (attributeByWindow && result.WindowAttributedRecords < 0)"))]),
    ("N6 the window line printed only above zero", "6", [
        (CLI, LINE, LINE.replace("if (attributeByWindow)", "if (attributeByWindow && result.WindowAttributedRecords > 0)"))]),
    ("N7 the window line printed without the flag", "7", [
        (CLI, LINE, LINE.replace("if (attributeByWindow)", "if (attributeByWindow || !attributeByWindow)"))]),
    ("N8 a merged call ignores its span twin's phase", "9", [
        (MERGER, "Phase = wire.ResolvedPhase == TestPhase.Unknown ? span.Phase ?? wire.Phase : wire.Phase,", "Phase = wire.Phase,")]),
    ("N9 a span twin's phase overwrites the wire half's own", "10", [
        (MERGER, "Phase = wire.ResolvedPhase == TestPhase.Unknown ? span.Phase ?? wire.Phase : wire.Phase,", "Phase = span.Phase ?? wire.Phase,")]),
    ("N10 the window count not handed to the result", "5, 8", [
        (PIPE, "                Diagnostics = diagnostics.Entries,\n                WindowAttributedRecords = windowAttributedRecords,\n",
               "                Diagnostics = diagnostics.Entries,\n"),
        (PIPE, "            Diagnostics = diagnostics.Entries,\n            WindowAttributedRecords = windowAttributedRecords,\n",
               "            Diagnostics = diagnostics.Entries,\n")]),
    ("N11 the ingest's ledger looked for from the tool's folder again (section 8.1)", "12", [
        (PIPE, "historyBaseDirectory: Directory.GetCurrentDirectory());", "historyBaseDirectory: null);")]),
]

FACTS = {
    "Window_attribution_records_no_diagnostic_for_the_records_it_attributed": 1,
    "Claims_attribution_records_no_diagnostic_for_the_records_it_attributed": 2,
    "Records_left_unattributed_are_the_only_unattributed_interactions_entry": 3,
    "The_exclusive_mode_records_its_count_only_when_records_were_left_ambiguous": 4,
    "Ingest_prints_the_window_attribution_count_as_a_line_after_replayed": 5,
    "Ingest_prints_a_window_attribution_count_of_zero": 6,
    "Ingest_without_attribute_by_window_prints_no_window_line": 7,
    "The_pipeline_wires_window_attribution_and_phases_end_to_end": 8,
    "A_merged_call_takes_its_span_twins_phase_when_its_wire_half_has_none": 9,
    "A_merged_call_keeps_the_phase_its_wire_half_carries": 10,
    "The_usage_says_window_attribution_prints_its_count": 11,
    "Ingest_finds_the_history_ledger_from_where_it_is_run_not_from_the_tools_folder": 12,
}
# Optional third argument: the mutation ids to run (N11, or N1,N3); the default runs them all.
ONLY = set(sys.argv[3].split(",")) if len(sys.argv) > 3 else None


def run_tests():
    proc = subprocess.run(["dotnet", "test", "tests/Kronikol.Tests", "--filter", FILTER], cwd=ROOT,
                          capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = proc.stdout + proc.stderr
    if "error CS" in text:
        return None, sorted(set(re.findall(r"error CS\d+: [^\r\n]*", text)))[:3]
    failed = sorted(set(re.findall(r"Kronikol\.Tests\.[\w.]+\.(\w+)(?:\([^)]*\))? \[FAIL\]", text)))
    totals = re.findall(r"Failed:\s+\d+, Passed:\s+\d+, Skipped:\s+\d+, Total:\s+\d+", text)
    return failed, totals


lines = []
failed, totals = run_tests()
lines.append(f"baseline (no mutation): failed {failed} {totals}")
for name, expected, edits in MUTATIONS:
    if ONLY is not None and name.split()[0] not in ONLY:
        continue
    originals = {}
    for path, old, new in edits:
        file = ROOT / path
        text = originals.setdefault(path, file.read_text(encoding="utf-8")) if path not in originals else file.read_text(encoding="utf-8")
        assert text.count(old) == 1, f"{name}: {old[:60]!r} found {text.count(old)} times"
        file.write_text(text.replace(old, new), encoding="utf-8", newline="")
    try:
        failed, totals = run_tests()
    finally:
        for path, text in originals.items():
            (ROOT / path).write_text(text, encoding="utf-8", newline="")
    if failed is None:
        lines.append(f"{name}: BUILD FAILED {totals}")
        continue
    red = sorted({FACTS[f] for f in failed if f in FACTS})
    others = [f for f in failed if f not in FACTS]
    want = {int(n) for n in expected.split(", ")}
    verdict = "OK" if want <= set(red) else "MISSED " + ", ".join(str(n) for n in sorted(want - set(red)))
    lines.append(f"{name}: expected red {expected}; red {red}; other facts red {others}; {verdict} {totals}")
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")

OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
print("\n".join(lines))
