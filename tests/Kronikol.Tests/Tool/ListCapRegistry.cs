namespace Kronikol.Tests.Tool;

/// <summary>
/// Every list cap in <c>kronikol query</c> and the failures digest, and what answers for it (#161,
/// plans/LIST_CAPS_PLAN.md R2). A cap is checked by a row of <see cref="CapCensusTests"/>, which reads its cut line and
/// runs the view it names; or it is held by a fact of its own, named here, that reads its announcement; or it is not a
/// cut a reader could take for the whole list (a ranking, a pick of one), and the reason is written here.
/// <see cref="ListCapSourceTests"/> refuses a <c>Take</c> or <c>TakeLast</c> with a constant that no entry covers, so a
/// new cap is a decision someone writes down, and an entry whose code is gone fails as well.
/// </summary>
internal static class ListCapRegistry
{
    /// <summary>A cap.</summary>
    /// <param name="File">The file, under <c>src/Kronikol</c>.</param>
    /// <param name="Code">Code on the cap's line, found on exactly <paramref name="Lines"/> lines of the file.</param>
    /// <param name="Lines">How many lines carry <paramref name="Code"/>.</param>
    internal sealed record Cap(string File, string Code, int Lines = 1)
    {
        /// <summary>The <see cref="CapCensusTests"/> row that checks it.</summary>
        public string? Census { get; init; }

        /// <summary>The fact that holds its announcement, as <c>Class.Method</c>, for a cap announced before #161.</summary>
        public string? HeldBy { get; init; }

        /// <summary>What announces it, or why it needs no announcement: given when there is no census row.</summary>
        public string? Reason { get; init; }

        public override string ToString() => $"{File}: {Code}";
    }

    private const string Narrative = "Query/QueryCommand.Narrative.cs";
    private const string Overview = "Query/QueryCommand.Overview.cs";
    private const string Search = "Query/QueryCommand.Search.cs";
    private const string History = "Query/QueryCommand.History.cs";
    private const string Payloads = "Query/QueryCommand.Payloads.cs";
    private const string Diff = "Query/QueryCommand.Diff.cs";
    private const string Digest = "Reports/FailuresDigestGenerator.cs";

    internal static readonly Cap[] Caps =
    [
        // ─── Checked by the census (R2) ───────────────────────────
        new(Narrative, "QueryWriter.CutList(step.Parameters, MaxParametersShown, MaxParametersShown)") { Census = "steps parameters" },
        new(Narrative, "WriteAttachments(writer, index, step.Attachments, $\"{indent}      \"") { Census = "steps attachments" },
        new(Overview, ".Take(MaxStatusesShown)") { Census = "services statuses" },
        new("Query/QueryCommand.Sizes.cs", ".Take(LargestScenarios)") { Census = "sizes largest scenarios" },
        new(Search, "Math.Min(paths.Count, MaxPathsPerBody)") { Census = "grep --values paths" },
        new("Query/QueryCommand.NumberGrep.cs", "if (found > MaxPathsPerBody)") { Census = "grep --number paths" },
        new(Diff, "if (shown == MaxLinesListed)") { Census = "diff --body lines" },
        new(Search, "differing.Take(MaxDifferencesShown)") { Census = "compare differences" },
        new(Payloads, "carried.Occurrences.Take(MaxCarriersListed)") { Census = "http b: carriers" },
        new(Payloads, "entry.Occurrences.Take(MaxCarriersListed)") { Census = "body carriers" },
        new(Digest, ".TakeLast(StepsBeforeFailure)") { Census = "Failures.md steps before" },
        new(Digest, "attributedToFailing.Take(MaxCallsPerFailure)") { Census = "Failures.md calls" },
        new(Digest, "requests.Where(x => !IsError(x, responses)).TakeLast(MaxCallsPerFailure)") { Census = "Failures.md calls" },
        new(Digest, "errored.Concat(rest).Take(MaxCallsPerFailure)") { Census = "Failures.md calls" },

        // ─── Held by facts of their own ───────────────────────────
        new(History, "entry.Points.TakeLast(MaxRunsListed)")
        {
            HeldBy = "QueryHistoryTests.One_scenario_says_how_many_earlier_runs_it_did_not_list_and_names_the_json_that_has_them",
            Reason = "history sN: \"… N earlier run(s) not listed: history sN --json\"; the census report has no ledger"
        },
        new(Narrative, ".Take(MaxFailingStepCalls)", Lines: 2)
        {
            HeldBy = "QueryAttachmentListTests.Failures_names_the_view_for_a_steps_cut_calls",
            Reason = "failures: a failing step's calls, \"… N more call(s): flow sN/P\" in the text and callsTotal in the JSON (R1)"
        },
        new(Narrative, "WriteAttachments(writer, index, step.Attachments, $\"  {indent}  \"")
        {
            HeldBy = "QueryAttachmentListTests.Failures_cuts_a_long_list_at_whole_names_and_names_the_scenario_or_the_step",
            Reason = "failures: a failing step's attachments, the first and the last five around a line naming the rest (R1)"
        },
        new(Narrative, "WriteAttachments(writer, index, scenario.Attachments, \"  \"")
        {
            HeldBy = "QueryAttachmentListTests.Failures_keeps_both_ends_of_a_scenarios_attachments_and_names_what_it_cut",
            Reason = "failures: the scenario's own attachments, cut as a step's are (R1)"
        },
        new(Narrative, "QueryWriter.CutList(s.Step.Attachments, MaxAttachmentsShown, AttachmentsHead)")
        {
            HeldBy = "QueryAttachmentListTests.Failures_json_carries_the_texts_entries_and_every_lists_total",
            Reason = "failures --json: a failing step's attachments, with attachmentsTotal beside them (R1)"
        },
        new(Narrative, "QueryWriter.CutList(scenario.Attachments, MaxAttachmentsShown, AttachmentsHead)")
        {
            HeldBy = "QueryAttachmentListTests.Failures_json_carries_the_texts_entries_and_every_lists_total",
            Reason = "failures --json: the scenario's own attachments, with attachmentsTotal beside them (R1)"
        },
        new(Overview, "foreach (var scenario in tests.Where(s => s.Failed).Take(10))")
        {
            HeldBy = "QueryCommandTests.Summary_says_how_many_failed_scenarios_it_did_not_list_and_names_the_listing",
            Reason = "summary: \"… N more · scenarios --result Failed\""
        },
        new(Overview, "writer.Data(\"failed\", tests.Where(s => s.Failed).Take(10)")
        {
            HeldBy = "QueryCommandTests.Summary_says_how_many_failed_scenarios_it_did_not_list_and_names_the_listing",
            Reason = "summary --json: failedTotal beside the first ten"
        },
        new(History, "verdicts.Absent.Take(20)")
        {
            HeldBy = "QueryHistoryTests.A_run_says_how_many_absent_scenarios_it_did_not_list",
            Reason = "history (a run): \"… and N more\" under the absent scenarios; the JSON lists them all"
        },
        new(History, ".TakeLast(10).Reverse()")
        {
            HeldBy = "QueryHistoryLedgerOnlyTests.A_run_not_found_lists_the_newest_ten_and_counts_them_all",
            Reason = "history --run not found: the newest ten, under a line that counts every run"
        },
        new(Search, "goneRows.Take(10)")
        {
            HeldBy = "DiffFormsTests.A_cut_section_of_the_run_diff_says_where_the_rest_is",
            Reason = "diff of two runs: \"… N more (--json lists every row)\""
        },
        new(Search, "rows.Take(15)")
        {
            HeldBy = "DiffFormsTests.A_cut_section_of_the_run_diff_says_where_the_rest_is",
            Reason = "diff of two runs: \"… N more (--json lists every row)\""
        },
        new(Search, "trackingRows.Take(10)")
        {
            HeldBy = "BaselineDiffTests.A_long_tracking_section_says_how_many_rows_it_did_not_list",
            Reason = "diff of two runs: \"… and N more (--json lists every row)\" under Tracking"
        },
        new("Query/QueryCommand.cs", "found.Take(MaxReportsListed)")
        {
            HeldBy = "RetainedRunsTests.A_list_of_more_than_twenty_reports_says_how_many_it_leaves_out",
            Reason = "a directory holding several reports: \"… and N more\""
        },
        new(Digest, "cluster.Take(MaxClusterMembersListed)")
        {
            HeldBy = "DigestSuppressionCapTests.Every_cap_says_how_many_it_left_out",
            Reason = "Failures.md: a cluster's members, \"N more not listed\""
        },
        new(Digest, "detailed.Take(MaxDetailedFailures)")
        {
            HeldBy = "FailuresDigestGeneratorTests.Detailed_entries_are_capped_and_the_cap_is_announced",
            Reason = "Failures.md: the failures worked through, then \"## N further failures\""
        },
        new(Digest, "further.Take(MaxFurtherFailuresListed)")
        {
            HeldBy = "DigestSuppressionCapTests.Every_cap_says_how_many_it_left_out",
            Reason = "Failures.md: the further failures' table, \"N more not listed\""
        },

        // ─── Not a cut a reader could take for the whole list ─────
        new(Overview, ".ThenByDescending(s => s.DurationSeconds).Take(3)")
        {
            Reason = "summary: a ranking, labelled \"Slowest:\""
        },
        new("Query/RetainedRunResolver.cs", ".Take(1).ToList()", Lines: 2)
        {
            Reason = "a pick of one run, not a list"
        },
        new(Diff, "keys.Take(6)")
        {
            Reason = "the shape of an added or removed object, \"{a, b, …}\", in one row of the diff; body --keys lists its keys"
        },
    ];
}
