"""R1's mutations (plan section 4.3): each breaks one behaviour, and the facts that must turn red are listed.

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
LINE = "if (phaseFromSteps)\n                @out.WriteLine($\"--phase-from-steps: "

MUTATIONS = [
    ("M1 each record looked up on its own timestamp again", "1, 2, 3, 10, 13", [
        (ATTR, "? pair.Written ?? FindPhase(byTest[record.TestId], pair.Start)",
               "? pair.Written ?? FindPhase(byTest[record.TestId], record.Timestamp)")]),
    ("M2 a pair judged on its latest record", "1, 3", [
        (ATTR, "(pair.Start is null || at < pair.Start)", "(pair.Start is null || at > pair.Start)")]),
    ("M3 a capturer's phase on one half ignored by the other half", "4", [
        (ATTR, "? pair.Written ?? FindPhase(byTest[record.TestId], pair.Start)",
               "? FindPhase(byTest[record.TestId], pair.Start)")]),
    ("M4 a capturer-phased half overwritten by the pair's phase", "5", [
        (ATTR, "if (record.IsMarker || record.ResolvedPhase != TestPhase.Unknown)\n", "if (record.IsMarker)\n")]),
    ("M5 the literal \"Unknown\" comparison restored", "6", [
        (ATTR, "if (record.IsMarker || record.ResolvedPhase != TestPhase.Unknown)\n",
               "if (record.IsMarker || !string.IsNullOrWhiteSpace(record.Phase) && !string.Equals(record.Phase, "
               "nameof(TestPhase.Unknown), StringComparison.OrdinalIgnoreCase))\n")]),
    ("M6 the diagnostic recorded again", "9, 11, 12", [
        (PIPE, "(records, phasedRecords) = IngestAttribution.ApplyPhaseFromSteps(records, stepWindows);\n",
               "(records, phasedRecords) = IngestAttribution.ApplyPhaseFromSteps(records, stepWindows);\n"
               "            if (phasedRecords > 0)\n"
               "                diagnostics.Add(DiagnosticKind.Other, $\"{phasedRecords} interaction record(s) took their "
               "phase from the step they happened during.\");\n")]),
    ("M7a the line printed only above zero", "15", [
        (CLI, LINE, LINE.replace("if (phaseFromSteps)", "if (phaseFromSteps && result.PhasedRecords > 0)"))]),
    ("M7b the line dropped", "14, 15", [
        (CLI, LINE, LINE.replace("if (phaseFromSteps)", "if (phaseFromSteps && result.PhasedRecords < 0)"))]),
    ("M8 the line printed without the flag", "16", [
        (CLI, LINE, LINE.replace("if (phaseFromSteps)", "if (phaseFromSteps || !phaseFromSteps)"))]),
    ("M9 Tagged counting calls", "8", [
        (ATTR, "            tagged++;\n            result.Add(record with { Phase = phase.Value.ToString() });",
               "            if (!IsResponse(record) || record.RequestResponseId is not { Length: > 0 })\n"
               "                tagged++;\n            result.Add(record with { Phase = phase.Value.ToString() });")]),
    ("M10 records with no pair id keyed together as one pair", "7", [
        (ATTR, "if (record.IsMarker || record.RequestResponseId is not { Length: > 0 } id)\n                continue;",
               "if (record.IsMarker)\n                continue;\n            var id = record.RequestResponseId ?? \"\";"),
        (ATTR, "var phase = record.RequestResponseId is { Length: > 0 } id && pairs.TryGetValue(id, out var pair)",
               "var phase = (record.RequestResponseId ?? \"\") is { } id && pairs.TryGetValue(id, out var pair)")]),
]

FACTS = {
    "A_response_after_its_step_ended_takes_its_requests_phase": 1,
    "A_response_that_lands_in_a_later_step_keeps_its_requests_phase": 2,
    "A_response_read_before_its_request_is_judged_on_the_pairs_start": 3,
    "A_capturers_phase_on_either_half_is_the_pairs": 4,
    "Halves_the_capturer_phased_each_keep_their_own": 5,
    "A_phase_that_names_no_member_is_taken_from_the_steps": 6,
    "A_record_without_a_pair_id_is_phased_on_its_own_timestamp": 7,
    "Tagged_counts_every_record_given_a_phase": 8,
    "A_healthy_phase_from_steps_ingest_records_no_diagnostic": 9,
    "Both_halves_of_a_late_call_carry_one_phase_in_the_data_file": 10,
    "A_healthy_phase_from_steps_run_writes_no_labs_page_with_history_off": 11,
    "A_phase_from_steps_report_heads_no_query_answer": 12,
    "The_response_halfs_address_shows_its_calls_phase": 13,
    "Ingest_prints_the_phase_count_as_a_line_after_replayed": 14,
    "Ingest_prints_a_phase_count_of_zero": 15,
    "Ingest_without_phase_from_steps_prints_no_phase_line": 16,
    "The_usage_says_a_response_takes_its_requests_phase": 17,
}


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
