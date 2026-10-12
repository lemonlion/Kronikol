"""The audit's proofs (2026-10-12): the new facts on 4.14.6's code, then one mutation per behaviour the audit's patch
adds, each listing the facts that must turn red.

Run from a scratch worktree holding the patch, never the release worktree: each mutation is applied to the file's
text and the file is written back from memory afterwards (no git checkout).

    python mutate_audit.py <worktree> <results-file> [<old-commit>]

With <old-commit> (4.14.6's, d59843fa), the first line runs the facts on that commit's source files.
"""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
OLD = sys.argv[3] if len(sys.argv) > 3 else None
ATTR = "src/Kronikol/Ingestion/IngestAttribution.cs"
RECORD = "src/Kronikol/Ingestion/InteractionRecord.cs"
OVERVIEW = "src/Kronikol/Query/QueryCommand.Overview.cs"
FILTER = ("FullyQualifiedName~Ingestion.IngestCallPairTests|FullyQualifiedName~Ingestion.IngestAttributionTests"
          "|FullyQualifiedName~Ingestion.InteractionRecordTests"
          "|FullyQualifiedName~Tool.QueryCommandTests.Scenarios_counts_each_call_once_as_interactions_does")

WINDOW_LOOP = ("var byRequestResponseId = new Dictionary<string, string?>(StringComparer.Ordinal);\n"
               "        var decided = new InteractionRecord[records.Count];\n"
               "        var attributed = 0;\n        var ambiguous = 0;\n\n        foreach (var index in RequestsFirst(records))")
CLAIMS_LOOP = ("var decided = new InteractionRecord[records.Count];\n"
               "        var byRequestResponseId = new Dictionary<string, string>(StringComparer.Ordinal);\n"
               "        var attributed = 0;\n        var ambiguous = 0;\n\n        foreach (var index in RequestsFirst(records))")
UNPLACED = ("                if (record.RequestResponseId is { Length: > 0 } unplaced && !IsResponse(record))\n"
            "                    byRequestResponseId.TryAdd(unplaced, null);\n")
FOLLOW = "byRequestResponseId.TryGetValue(id, out var requestTest))\n            {\n                testId = requestTest;"
CLAIM_OWN = "            if (testId is null && record.Timestamp is { } when)"
WRITTEN = "(pair.Written is null || (!pair.WrittenOnRequest && !IsResponse(record)))"

MUTATIONS = [
    ("A1 an empty pair id hashed into one pair again (F13)", "1, 2", [
        (RECORD, "ToGuid(RequestResponseId is { Length: > 0 } pairId ? pairId : $\"{TestId}|{Uri}|{Timestamp:O}\")",
                 "ToGuid(RequestResponseId ?? $\"{TestId}|{Uri}|{Timestamp:O}\")")]),
    ("A2 the window pass judges records in file order again (F11)", "3, 4, 5, 6", [
        (ATTR, WINDOW_LOOP, WINDOW_LOOP.replace("RequestsFirst(records)", "Enumerable.Range(0, records.Count)"))]),
    ("A3 the claims pass judges records in file order again (F11)", "7", [
        (ATTR, CLAIMS_LOOP, CLAIMS_LOOP.replace("RequestsFirst(records)", "Enumerable.Range(0, records.Count)"))]),
    ("A4 a response whose request no window placed is judged on its own timestamp again (F12)", "6, 8, 9", [
        (ATTR, UNPLACED, "")]),
    ("A5 a response with no request in the records is left unattributed too", "10", [
        (ATTR, FOLLOW, "true)\n            {\n                testId = byRequestResponseId.GetValueOrDefault(id);")]),
    ("A6 the claims pass leaves a response with its unclaimed request", "11", [
        (ATTR, CLAIM_OWN, "            if (testId is null && !IsResponse(record) && record.Timestamp is { } when)")]),
    ("A7 the first phase written on a pair is the pair's (section 3.2, rule 2)", "12", [
        (ATTR, WRITTEN, "(pair.Written is null)")]),
    ("A8 a response's written phase beats its request's (section 3.2, rule 2)", "12", [
        (ATTR, WRITTEN, "(pair.Written is null || IsResponse(record))")]),
    ("A9 query scenarios counts every record as a call again (F14)", "13", [
        (OVERVIEW, "{calls,4} calls  ", "{scenario.Interactions.Count,4} calls  ")]),
]

FACTS = {
    "An_empty_pair_id_pairs_nothing_as_a_missing_one": 1,
    "Calls_with_an_empty_pair_id_are_read_as_calls_with_none": 2,
    "A_response_read_before_its_request_follows_it_to_its_test": 3,
    "A_response_read_before_a_request_that_names_its_test_follows_it": 4,
    "A_response_read_before_its_request_is_attributed_and_phased_with_it": 5,
    "A_response_whose_request_is_in_no_window_stays_with_it": 6,
    "A_response_read_before_its_request_follows_the_test_that_claimed_the_request": 7,
    "Under_exclusive_only_a_response_whose_request_two_windows_held_stays_with_it": 8,
    "A_call_made_between_two_tests_is_not_given_half_to_the_second": 9,
    "A_response_with_no_request_in_the_capture_is_judged_on_its_own_timestamp": 10,
    "A_response_whose_request_matched_no_claim_is_judged_on_its_own_content": 11,
    "When_both_halves_carry_a_phase_the_requests_is_the_pairs": 12,
    "Scenarios_counts_each_call_once_as_interactions_does": 13,
}
# Optional fourth argument: the mutation ids to run (A4, or A1,A3); the default runs them all.
ONLY = set(sys.argv[4].split(",")) if len(sys.argv) > 4 else None


def run_tests():
    proc = subprocess.run(["dotnet", "test", "tests/Kronikol.Tests", "--filter", FILTER], cwd=ROOT,
                          capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = proc.stdout + proc.stderr
    if "error CS" in text:
        return None, sorted(set(re.findall(r"error CS\d+: [^\r\n]*", text)))[:3]
    failed = set(re.findall(r"Kronikol\.Tests\.[\w.]+\.(\w+)(?:\([^)]*\))? \[FAIL\]", text))
    failed |= set(re.findall(r"Failed Kronikol\.Tests\.[\w.]+\.(\w+)(?:\([^)]*\))? \[", text))
    totals = re.findall(r"Failed:\s+\d+, Passed:\s+\d+, Skipped:\s+\d+, Total:\s+\d+", text)
    return sorted(failed), totals


def describe(failed):
    return sorted({FACTS[f] for f in failed if f in FACTS}), [f for f in failed if f not in FACTS]


lines = []
if OLD is not None and ONLY is None:
    current = {path: (ROOT / path).read_text(encoding="utf-8") for path in (ATTR, RECORD, OVERVIEW)}
    try:
        for path in current:
            old = subprocess.run(["git", "show", f"{OLD}:{path}"], cwd=ROOT, capture_output=True, check=True).stdout
            (ROOT / path).write_bytes(old)
        failed, totals = run_tests()
    finally:
        for path, text in current.items():
            (ROOT / path).write_text(text, encoding="utf-8", newline="")
    red, others = describe(failed or [])
    lines.append(f"on {OLD} (4.14.6's source, the new facts): red {red}; other facts red {others}; expected red 1-9 and 13, "
                 f"guards 10-12 green {totals}")

failed, totals = run_tests()
lines.append(f"baseline (the patch, no mutation): failed {failed} {totals}")
for name, expected, edits in MUTATIONS:
    if ONLY is not None and name.split()[0] not in ONLY:
        continue
    originals = {}
    for path, old, new in edits:
        file = ROOT / path
        text = file.read_text(encoding="utf-8")
        originals.setdefault(path, text)
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
    red, others = describe(failed)
    want = {int(n) for n in expected.split(", ")}
    verdict = "OK" if want <= set(red) else "MISSED " + ", ".join(str(n) for n in sorted(want - set(red)))
    lines.append(f"{name}: expected red {expected}; red {red}; other facts red {others}; {verdict} {totals}")
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")

OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
print("\n".join(lines))
