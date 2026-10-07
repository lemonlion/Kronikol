"""Mutation checks for CUCUMBER_BYPASS_PLAN.md (#105): each mutation is applied alone to a clean checkout, the test
project is built, the named facts are run, and the mutation must turn at least one of them red.

Usage (from the root of a worktree holding the release's commit, never the shared checkout):
    python plans/CUCUMBER_BYPASS_PLAN.harness/mutations/mutate.py [r1|r2] > results.txt
"""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path.cwd()
SYNTH = "src/Kronikol/Ingestion/Cucumber/CucumberFeatureSynthesizer.cs"
COLLECT = "src/Kronikol/Ingestion/Cucumber/CucumberTestsFileBypass.cs"
FEATURE = "src/Kronikol/Ingestion/FeatureSynthesizer.cs"
PIPELINE = "src/Kronikol/Ingestion/IngestPipeline.cs"
OVERVIEW = "src/Kronikol/Query/QueryCommand.Overview.cs"
NARRATIVE = "src/Kronikol/Query/QueryCommand.Narrative.cs"
CI = "src/Kronikol/Reports/CiSummaryGenerator.cs"
REPORT = "src/Kronikol/Reports/ReportGenerator.cs"
READER = "src/Kronikol/Ingestion/Cucumber/CucumberMessagesReader.cs"
FILTER = ("FullyQualifiedName~CucumberBypass|FullyQualifiedName~FeatureSynthesizerBypass"
          "|FullyQualifiedName~QuerySummaryCounts|FullyQualifiedName~CiSummaryCounts"
          "|FullyQualifiedName~StepBypassReason|FullyQualifiedName~CucumberMessagesReaderTests")

# (name, [(file, old, new)], the fact expected to catch it)
R1 = [
    ("hook counts as a step that ran",
     [(SYNTH, "if (plan[i].Id is { Length: > 0 } id && plan[i].HookId is not { Length: > 0 }\n"
              "                && plan[i].PickleStepId is { Length: > 0 } psid && pickleSteps.ContainsKey(psid)\n",
       "if (plan[i].Id is { Length: > 0 } id\n")],
     "A_hook_that_ran_after_a_skipped_step_is_not_a_step_that_ran"),
    ("UNDEFINED counts as a step that ran",
     [(SYNTH, 'is "PASSED" or "FAILED")', 'is "PASSED" or "FAILED" or "UNDEFINED")')],
     "A_later_step_that_did_not_run_is_not_a_step_that_ran"),
    ("the first attempt is read, not the last",
     [(SYNTH, "RawStatus(stepFinishes.GetValueOrDefault(Key(attemptId, id)!)",
       "RawStatus(stepFinishes.GetValueOrDefault(Key(attempts[0].Id, id)!)")],
     "A_bypass_in_the_last_attempt_is_read_after_an_earlier_attempt_failed"),
    ("'later' dropped: any step that ran anywhere",
     [(SYNTH, "position < lastRan, fromTestsFile", "lastRan >= 0, fromTestsFile")],
     "Steps_skipped_after_a_failure_stay_skipped_and_the_scenario_failed"),
    ("Bypassed ranked above Skipped",
     [(SYNTH, "ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => 2,\n        ExecutionResult.Bypassed => 1,",
       "ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => 1,\n        ExecutionResult.Bypassed => 2,")],
     "A_bypassed_step_before_a_step_that_skipped_the_rest_leaves_the_scenario_skipped"),
    ("a failed tests-file step overturns a passed end",
     [(FEATURE, "result == ExecutionResult.Passed && (AnyBypassed(steps) || AnyBypassed(backgroundSteps)) ? ExecutionResult.Bypassed : result;",
       "result == ExecutionResult.Passed && (steps ?? []).Any(s => s.Status == ExecutionResult.Failed) ? ExecutionResult.Failed\n"
       "        : result == ExecutionResult.Passed && (AnyBypassed(steps) || AnyBypassed(backgroundSteps)) ? ExecutionResult.Bypassed : result;")],
     "A_failed_step_does_not_overturn_a_passed_end"),
    ("a skipped end rolls up too",
     [(FEATURE, "result == ExecutionResult.Passed && (AnyBypassed", "result != ExecutionResult.Failed && (AnyBypassed")],
     "Any_other_end_stands_beside_a_bypassed_step"),
    ("a scenario with no end rolls up too",
     [(FEATURE, "Result = acc.HasEnd ? RollUpBypass(MapStatus(acc.Status), steps, backgroundSteps) : resultWhenUnknown,",
       "Result = RollUpBypass(acc.HasEnd ? MapStatus(acc.Status) : resultWhenUnknown, steps, backgroundSteps),")],
     "A_scenario_with_no_end_keeps_its_defaulted_verdict"),
    ("only top-level steps roll up",
     [(FEATURE, "s.Status == ExecutionResult.Bypassed || AnyBypassed(s.SubSteps)", "s.Status == ExecutionResult.Bypassed")],
     "A_bypassed_sub_step_makes_a_passed_scenario_bypassed"),
    ("join by position, not text",
     [(COLLECT, "seen[text] = seen.GetValueOrDefault(text) + 1", 'seen[""] = seen.GetValueOrDefault("") + 1'),
      (SYNTH, "var occurrence = textOccurrences[text.Trim()] = textOccurrences.GetValueOrDefault(text.Trim()) + 1;\n"
              "            var fromTestsFile = bypassByText.GetValueOrDefault((text.Trim(), occurrence));",
       'var occurrence = textOccurrences[""] = textOccurrences.GetValueOrDefault("") + 1;\n'
       "            var fromTestsFile = bypasses.FirstOrDefault(b => b.Occurrence == occurrence);")],
     "A_tests_file_step_finds_its_gherkin_step_by_text_in_order"),
    ("the message kept as a comment as well as the reason",
     [(SYNTH, "Comments = message is null || (bypassed && message == bypassReason) ? null : [message],",
       "Comments = message is null ? null : [message],")],
     "A_skipped_step_that_a_later_step_ran_after_is_bypassed_with_its_message_as_the_reason"),
    ("the reason written as the marker's error",
     [(SYNTH, "stepStatus, bypassed ? null : message,", "stepStatus, message,")],
     "A_skipped_step_that_a_later_step_ran_after_is_bypassed_with_its_message_as_the_reason"),
    ("SkippedAfterFailure counted as passed in query summary",
     [(OVERVIEW, 'var featurePassed = feature.Count(s => s.Result.Equals("Passed", StringComparison.OrdinalIgnoreCase));',
       'var featurePassed = feature.Count(s => s.Result.Equals("Passed", StringComparison.OrdinalIgnoreCase)'
       ' || s.Result.Equals("SkippedAfterFailure", StringComparison.OrdinalIgnoreCase));')],
     "Summary_counts_skipped_and_bypassed_scenarios_apart_from_passed_ones"),
    ("CI summary: SkippedAfterFailure out of Skipped",
     [(CI, "s.Result is ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure", "s.Result is ExecutionResult.Skipped")],
     "A_scenario_skipped_after_a_failure_counts_as_skipped"),
    ("CI summary: a Bypassed row on every run",
     [(CI, "if (bypassed > 0)\n            sb.AppendLine", "if (bypassed >= 0)\n            sb.AppendLine")],
     "A_run_with_no_bypass_writes_the_rows_it_always_wrote"),
    ("failures footer: a bypassed scenario 'did not run'",
     [(NARRATIVE, "var notRun = index.Scenarios.Count - passed - bypassed;", "var notRun = index.Scenarios.Count - passed;")],
     "Failures_with_nothing_failed_does_not_say_a_bypassed_scenario_did_not_run"),
    ("the synthesis warnings dropped",
     [(PIPELINE, "foreach (var warning in cucumber.Warnings)\n                diagnostics.Add(DiagnosticKind.Other, warning);\n", "")],
     "The_synthesis_warnings_reach_the_ingest_diagnostics"),
    ("the unmatched-records diagnostic dropped",
     [(PIPELINE, "if (cucumber.UnmatchedTestsFileBypasses > 0)", "if (cucumber.UnmatchedTestsFileBypasses > 999)")],
     "A_bypassed_record_that_matches_no_gherkin_step_changes_nothing_and_is_counted_once"),
    ("a nested record matches a Gherkin step",
     [(COLLECT, "var occurrence = step.Level is > 0 ? 0 : seen[text]", "var occurrence = seen[text]")],
     "A_bypassed_record_that_matches_no_gherkin_step_changes_nothing_and_is_counted_once"),
    ("every attempt of the tests file is read",
     [(COLLECT, "            {\n                stepsByTest[record.TestId] = [];\n            }",
       "            {\n                stepsByTest.TryAdd(record.TestId, []);\n            }")],
     "Only_the_tests_files_last_attempt_is_read"),
    ("a tests-file bypass overturns a failed step",
     [(SYNTH, "        if (status == ExecutionResult.Failed)\n            return (status, null);\n        if (fromTestsFile is not null)",
       "        if (fromTestsFile is not null)")],
     "A_tests_file_bypass_never_overturns_a_failed_gherkin_step"),
    ("PENDING read as skipped over",
     [(SYNTH, 'if (rawStatus == "SKIPPED" && aLaterStepRan)', "if (status == ExecutionResult.Skipped && aLaterStepRan)")],
     "A_pending_step_that_a_later_step_ran_after_stays_skipped"),
    ("the bypass reason not drawn",
     [(REPORT, "if (step.Status == ExecutionResult.Bypassed && !string.IsNullOrWhiteSpace(step.BypassReason))",
       "if (step.Status == ExecutionResult.Bypassed && step.BypassReason == \"never\")")],
     "A_bypassed_step_draws_its_reason_where_a_comment_is_drawn"),
    ("the bypass reason drawn as markup",
     [(REPORT, "Bypassed: {System.Net.WebUtility.HtmlEncode(step.BypassReason)}", "Bypassed: {step.BypassReason}")],
     "The_reason_is_drawn_as_text"),
    ("a reason drawn for a step that was not bypassed",
     [(REPORT, "if (step.Status == ExecutionResult.Bypassed && !string.IsNullOrWhiteSpace(step.BypassReason))",
       "if (!string.IsNullOrWhiteSpace(step.BypassReason))")],
     "Only_a_bypassed_step_with_a_reason_draws_one"),
    ("the envelopes every producer writes warned about",
     [(READER, "if (!ProtocolEnvelopesNotRead.Contains(property.Name))", "if (true)")],
     "Counts_source_envelopes_as_unknown_rather_than_failing"),
]

R2 = [
    ("the attachment ignored",
     [(SYNTH, "            if (IsBypassAttachment(attachment))\n",
       "            if (IsBypassAttachment(attachment) && attachment.FileName == \"never\")\n")],
     "A_step_that_attaches_kronikol_bypass_is_bypassed_with_the_body_as_its_reason"),
    ("the attachment overrides a failed step",
     [(SYNTH, "        if (status == ExecutionResult.Failed)\n            return (status, null);\n        if (attached)",
       "        if (attached)")],
     "A_failed_step_stays_failed_and_keeps_the_body_as_a_comment"),
    ("the attachment written out as a file",
     [(SYNTH, "                    strayBypassAttachments++;\n                continue;\n            }",
       "                    strayBypassAttachments++;\n            }")],
     "The_attachment_is_never_written_out_as_a_file"),
    ("a failed step drops the body",
     [(SYNTH, "            if (stepStatus == ExecutionResult.Failed && attachedReason is not null)\n                comments.Add(attachedReason);\n", "")],
     "A_failed_step_stays_failed_and_keeps_the_body_as_a_comment"),
    ("no warning for an attachment that bypasses nothing",
     [(SYNTH, "if (strayBypassAttachments > 0)", "if (strayBypassAttachments > 999)")],
     "On_a_hook_the_attachment_bypasses_nothing_and_says_so"),
    ("the last body is the reason, not the first",
     [(SYNTH, "bypassAttachments.GetValueOrDefault(bypassedStepId) ?? NullIfBlank(DecodeText(attachment)?.Trim())",
       "NullIfBlank(DecodeText(attachment)?.Trim()) ?? bypassAttachments.GetValueOrDefault(bypassedStepId)")],
     "A_base64_body_is_decoded_and_the_first_body_with_text_is_the_reason"),
    ("the name matched with its case",
     [(SYNTH, "string.Equals(attachment.FileName, BypassAttachmentName, StringComparison.OrdinalIgnoreCase)",
       "string.Equals(attachment.FileName, BypassAttachmentName, StringComparison.Ordinal)")],
     "A_base64_body_is_decoded_and_the_first_body_with_text_is_the_reason"),
    ("a BASE64 body not decoded",
     [(SYNTH, "?? NullIfBlank(DecodeText(attachment)?.Trim());", "?? NullIfBlank(attachment.Body?.Trim());")],
     "A_base64_body_is_decoded_and_the_first_body_with_text_is_the_reason"),
    ("summary JSON without the skipped count",
     [(OVERVIEW, "                skipped = featureSkipped,\n", "")],
     "Summary_json_says_passed_for_the_scenarios_that_passed"),
]

def run(cmd):
    return subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", shell=True)

def main():
    which = (sys.argv[1:] or ["r1"])[0]
    mutations = R1 if which == "r1" else R1[-4:] if which == "r1-late" else R2
    caught = 0
    for name, edits, expected in mutations:
        originals = {}
        try:
            for path, old, new in edits:
                p = ROOT / path
                text = originals.setdefault(path, p.read_text(encoding="utf-8")) if path not in originals else p.read_text(encoding="utf-8")
                if text.count(old) != 1:
                    raise SystemExit(f"mutation '{name}': the text to replace occurs {text.count(old)} times in {path}")
                p.write_text(text.replace(old, new), encoding="utf-8", newline="\n")
            build = run("dotnet build tests/Kronikol.Tests/Kronikol.Tests.csproj -c Debug -v q -nologo")
            if build.returncode != 0:
                print(f"BUILD FAILED  {name}\n{build.stdout[-1500:]}")
                continue
            test = run(f'dotnet test tests/Kronikol.Tests/Kronikol.Tests.csproj --no-build --filter "{FILTER}"')
            failed = [l.strip() for l in test.stdout.splitlines() if l.strip().startswith("Failed ")]
            hit = any(expected in l for l in failed)
            caught += hit
            print(f"{'CAUGHT ' if hit else 'MISSED '} {name}: {len(failed)} fact(s) red, expected {expected}"
                  + ("" if hit else f"; red: {failed}"))
            for l in failed:
                print(f"           {l[:160]}")
            sys.stdout.flush()
        finally:
            for path, text in originals.items():
                (ROOT / path).write_text(text, encoding="utf-8", newline="\n")
    print(f"{caught} of {len(mutations)} mutations caught by the fact named")

if __name__ == "__main__":
    main()
