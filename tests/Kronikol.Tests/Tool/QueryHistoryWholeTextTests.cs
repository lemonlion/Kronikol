using System.Text.Json;
using Kronikol.History;
using Kronikol.Tool;
using Kronikol.Tool.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #82: <c>history s8</c> cut every stored error at a fixed 80 characters, which in practice is
/// "expected X" without "but found Y" - the rest was only in <c>--json</c>, which the guidance steers
/// readers away from. The rule these tests hold: the single-scenario view never cuts, and a list view that
/// cuts says so and names the view that does not. An ellipsis always has an address.
/// </summary>
public class QueryHistoryWholeTextTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-query-history-whole").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string PayId = "aaaabbbbccccdddd";
    private const string RefundId = "eeeeffff00001111";

    /// <summary>The shape of the message in #82: what identifies the culprit is at the END.</summary>
    private const string LongError = "The service bigquery has thrown an exception. HttpStatusCode is BadRequest. invalid character '\"' looking for beginning of value";

    private static readonly string[] Before = ["Api>db QUERY /orders 200", "Api>db QUERY /baskets 200", "Api>db QUERY /stock 200", "Api>db QUERY /prices 200", "Api>cache GET /k 200"];
    private static readonly string[] Now = ["Api>cache GET /k 200", "Api>ledger POST /entries 201", "Api>ledger POST /batches 201", "Api>mail POST /send 202", "Api>audit POST /events 201", "Api>audit POST /traces 201"];

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private (string Output, string Error, int Exit) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error, _ => null, workingDirectory: _dir);
        return (output.ToString(), error.ToString(), exit);
    }

    private static HistoryRoster Roster() =>
        HistoryRoster.Create("Suite", [new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null), new HistoryRosterEntry(RefundId, "Refund an order", "Checkout", null)]);

    private static HistoryRun RunLine(HistoryRoster roster, string id, int hour, string results, string error, HistoryShapes shapes, string[] calls) => new()
    {
        Id = id, Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(hour),
        Branch = "main", Commit = $"c{hour:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
        Results = results, Attempts = "--", Durations = [100, 50],
        Calls = [calls.Length, 1], ShapeSet = [InteractionShape.Hash8(string.Join("\n", calls)), "cccccccc"], ShapeOrdered = [InteractionShape.Hash8(string.Join("\n", calls)), "cccccccc"],
        ShapeVersion = InteractionShape.Version, ShapesHash = shapes.Hash,
        CallSets = [calls.Select(line => ((IList<string>)shapes.Calls).IndexOf(line)).OrderBy(i => i).ToArray(), []],
        Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
        ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = error } : new Dictionary<string, string>(),
        Deps = ["Test>orders"]
    };

    /// <summary>Three runs in the ledger - the last of them failed with <paramref name="storedError"/> - and a failing current run with its fragment, whose calls differ by more than the evidence names.</summary>
    private string AFailingRunWithHistory(string storedError, bool quarantined = false)
    {
        var roster = Roster();
        var before = HistoryShapes.Create(Before);
        HistoryLedgerWriter.Append(Ledger, roster, RunLine(roster, "gh:1:1", 0, "PP", storedError, before, Before), "3.9.0", shapes: before);
        HistoryLedgerWriter.Append(Ledger, roster, RunLine(roster, "gh:2:1", 1, "PP", storedError, before, Before), "3.9.0", shapes: before);
        HistoryLedgerWriter.Append(Ledger, roster, RunLine(roster, "gh:3:1", 2, "FP", storedError, before, Before), "3.9.0", shapes: before);

        if (quarantined)
        {
            var list = new HistoryQuarantineList();
            list.Add(PayId, "BigQuery emulator rejects the batch insert on the second page; tracked as ticket 4711 with the vendor, and the workaround (single-row inserts) is slower than the suite's budget allows, so it stays quarantined until the emulator ships a fix",
                "sam", new DateOnly(2026, 9, 1), null);
            list.Save(HistoryQuarantineList.PathBeside(Ledger));
        }

        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var now = HistoryShapes.Create(Now);
        var current = RunLine(roster, "gh:99:1", 100, "FP", storedError, now, Now);
        File.WriteAllText(Path.Combine(directory, HistoryFormat.FragmentFileName), HistoryFragment.Write(roster, current, "3.9.0", now));
        var report = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(report, ReportJson(storedError));
        return report;
    }

    /// <summary>A report of the two scenarios, the first failed with <paramref name="storedError"/>, on CI run 99.</summary>
    private static string ReportJson(string storedError) => $$"""
        {
          "kronikolVersion": "3.9.0", "formatVersion": 1, "suite": "Suite",
          "startTime": "2026-09-12T10:00:00Z", "endTime": "2026-09-12T10:05:00Z",
          "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "99", "runAttempt": "1" },
          "features": [ { "name": "Checkout", "labels": [], "scenarios": [
            { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "Failed", "durationSeconds": 0.1, "errorMessage": {{JsonSerializer.Serialize(storedError)}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [] },
            { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "Passed", "durationSeconds": 0.05, "labels": [], "categories": [], "steps": [], "httpInteractions": [] } ] } ]
        }
        """;

    [Fact]
    public void The_failures_verb_says_when_it_cut_the_history_evidence_and_names_the_view_that_does_not()
    {
        // Seen on a consumer (plan §9): a scenario that broke across a change of fingerprint rule carries
        // 178 characters of evidence, and the failures verb - a list - cut it at 160 with an ellipsis and
        // no address. The run view's rule applies: a list that cuts says so and names the view that does not.
        var roster = Roster();
        var before = HistoryShapes.Create(Before);
        const string earlier = "local:20260915T145915Z:14e947b5";
        HistoryLedgerWriter.Append(Ledger, roster, RunLine(roster, "local:20260915T145604Z:14e947b5", 0, "PP", LongError, before, Before) with { ShapeVersion = InteractionShape.Version - 1 }, "3.9.0", shapes: before);
        HistoryLedgerWriter.Append(Ledger, roster, RunLine(roster, earlier, 1, "PP", LongError, before, Before) with { ShapeVersion = InteractionShape.Version - 1 }, "3.9.0", shapes: before);
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var now = HistoryShapes.Create(Now);
        File.WriteAllText(Path.Combine(directory, HistoryFormat.FragmentFileName), HistoryFragment.Write(roster, RunLine(roster, "local:20260922T080715Z:14e947b5", 100, "FP", LongError, now, Now), "3.9.0", now));
        var report = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(report, ReportJson(LongError));

        var (output, error, exit) = Query("failures", report);

        Assert.True(exit == 0, error);
        Assert.Contains("history: broke — passed in " + earlier, output);
        Assert.Contains(" … ", output);
        Assert.Contains("… marks cut text — history s0 prints the evidence whole", output);
        var whole = Query("history", report, "s0");
        Assert.True(whole.Exit == 0, whole.Error);
        Assert.Contains("behaviour is compared from the next run", whole.Output);
    }

    [Fact]
    public void The_scenario_view_prints_a_stored_error_whole()
    {
        var report = AFailingRunWithHistory(LongError);

        var (output, error, exit) = Query("history", report, "s0");

        Assert.True(exit == 0, error);
        // On its own line under the row, so the row keeps its columns and the message keeps its end.
        Assert.Contains("\n       " + LongError + "\n", output.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("first line only", output);
    }

    [Fact]
    public void A_stored_error_that_is_itself_the_ledger_s_cut_says_where_the_rest_is()
    {
        // The ledger keeps the first line of a message, at most 199 characters and an ellipsis.
        var stored = new string('x', HistoryFormat.ErrorKeyLimit - 1) + "…";
        var report = AFailingRunWithHistory(stored);

        var (output, error, exit) = Query("history", report, "s0");

        Assert.True(exit == 0, error);
        Assert.Contains(stored, output);
        // Every cut row says where ITS message is (an ellipsis always has an address): the report on top for
        // this run, and for the earlier run whether it is kept under runs/ - here nothing is.
        var directory = Path.GetDirectoryName(report)!;
        Assert.Contains($"… first line only — the whole message is in this run's Failures.md: kronikol query failures {directory}", output);
        Assert.Contains($"… first line only — the whole message was in that run's Failures.md, and run gh:3:1 is not kept under {directory}", output);
        Assert.DoesNotContain("<reports-dir>", output);
    }

    [Fact]
    public void The_pointer_under_a_cut_error_opens_the_run_when_it_is_kept()
    {
        var stored = new string('x', HistoryFormat.ErrorKeyLimit - 1) + "…";
        var report = AFailingRunWithHistory(stored);
        var directory = Path.GetDirectoryName(report)!;
        // gh:3:1 kept under runs/ as the rotation keeps it: its manifest is all --run reads.
        new Kronikol.Reports.RunManifest { Run = "gh:3:1", At = new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero), Suite = "Suite", Scenarios = 2, Failed = 1 }
            .Write(Path.Combine(directory, "runs", "gh_3_1"));

        var (output, error, exit) = Query("history", report, "s0");

        Assert.True(exit == 0, error);
        Assert.Contains($"… first line only — the whole message is in that run's Failures.md: kronikol query failures {directory} --run gh:3:1", output);
    }

    [Fact]
    public void Every_free_text_member_of_the_json_row_is_in_the_text_of_the_same_query()
    {
        // The test that stops the class of bug rather than the instance: no text-visible field may be
        // complete only in --json. Scoped to free text - result, commit, at and series are abbreviated in
        // the text on purpose and lose nothing.
        var report = AFailingRunWithHistory(LongError, quarantined: true);

        var text = Query("history", report, "s0");
        var json = Query("history", report, "s0", "--json");

        Assert.True(text.Exit == 0, text.Error);
        Assert.True(json.Exit == 0, json.Error);
        var row = JsonDocument.Parse(json.Output).RootElement.GetProperty("items")[0];
        var freeText = new List<string> { row.GetProperty("evidence").GetString()!, row.GetProperty("quarantine").GetProperty("reason").GetString()! };
        freeText.AddRange(row.GetProperty("newCalls").EnumerateArray().Select(c => c.GetString()!));
        freeText.AddRange(row.GetProperty("goneCalls").EnumerateArray().Select(c => c.GetString()!));
        freeText.AddRange(row.GetProperty("runs").EnumerateArray().Select(r => r.GetProperty("error").GetString()).Where(e => e is not null).Select(e => e!));

        // The fixture has to be able to fail: more calls than the evidence names, and a long reason.
        Assert.True(row.GetProperty("newCalls").GetArrayLength() > 3 && row.GetProperty("goneCalls").GetArrayLength() > 3, json.Output);
        Assert.True(freeText.Count >= 12, json.Output);
        foreach (var member in freeText)
            Assert.True(text.Output.Contains(member, StringComparison.Ordinal), $"not in the text: {member}\n\n{text.Output}");
    }

    [Fact]
    public void The_run_view_says_when_it_cut_something_and_names_the_view_that_does_not()
    {
        var report = AFailingRunWithHistory(LongError);

        var (output, error, exit) = Query("history", report);

        Assert.True(exit == 0, error);
        // The evidence of a set change with four new and four gone calls is longer than the list's column.
        Assert.Contains(" … ", output);
        Assert.Contains("… marks cut text — history s0 prints it whole", output);
    }

    [Fact]
    public void The_run_view_carries_no_cut_notice_when_nothing_was_cut()
    {
        var roster = Roster();
        var shapes = HistoryShapes.Create(Before);
        HistoryLedgerWriter.Append(Ledger, roster, RunLine(roster, "gh:1:1", 0, "PP", "boom", shapes, Before), "3.9.0", shapes: shapes);
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, HistoryFormat.FragmentFileName), HistoryFragment.Write(roster, RunLine(roster, "gh:99:1", 100, "FP", "boom", shapes, Before), "3.9.0", shapes));
        var report = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(report, $$"""
            {
              "kronikolVersion": "3.9.0", "formatVersion": 1, "suite": "Suite",
              "startTime": "2026-09-12T10:00:00Z", "endTime": "2026-09-12T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "99", "runAttempt": "1" },
              "features": [ { "name": "Checkout", "labels": [], "scenarios": [
                { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "Failed", "durationSeconds": 0.1, "errorMessage": "boom", "labels": [], "categories": [], "steps": [], "httpInteractions": [] },
                { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "Passed", "durationSeconds": 0.05, "labels": [], "categories": [], "steps": [], "httpInteractions": [] } ] } ]
            }
            """);

        var (output, error, exit) = Query("history", report);

        Assert.True(exit == 0, error);
        Assert.Contains("broke", output);
        Assert.DoesNotContain("marks cut text", output);
    }

    // ─── Cut ───────────────────────────────────────────────────

    [Fact]
    public void Cut_is_the_identity_under_the_limit_and_records_nothing()
    {
        var writer = new QueryWriter(new StringWriter(), 6000, null, null);

        Assert.Equal("short enough", writer.Cut("short enough", 80));
        Assert.False(writer.HasCut);
    }

    [Fact]
    public void Cut_keeps_the_head_and_the_tail_because_an_exception_puts_the_signal_last()
    {
        var writer = new QueryWriter(new StringWriter(), 6000, null, null);

        var cut = writer.Cut(LongError, 80);

        Assert.True(cut.Length <= 80, cut);
        Assert.StartsWith("The service bigquery has thrown", cut);
        Assert.Contains(" … ", cut);
        Assert.EndsWith("looking for beginning of value", cut);
        Assert.True(writer.HasCut);
    }

    [Fact]
    public void Cut_never_splits_a_surrogate_pair_at_either_end()
    {
        var writer = new QueryWriter(new StringWriter(), 6000, null, null);
        // Every character is two code units, so any odd cut position lands inside one.
        var emoji = string.Concat(Enumerable.Repeat("😀", 100));

        foreach (var max in new[] { 40, 41, 42, 43 })
        {
            var cut = writer.Cut(emoji, max);

            Assert.True(cut.Length <= max, $"{max}: {cut.Length}");
            for (var i = 0; i < cut.Length; i++)
                if (char.IsHighSurrogate(cut[i]))
                    Assert.True(i + 1 < cut.Length && char.IsLowSurrogate(cut[i + 1]), $"lone high surrogate at {i} for max {max}");
                else if (char.IsLowSurrogate(cut[i]))
                    Assert.True(i > 0 && char.IsHighSurrogate(cut[i - 1]), $"lone low surrogate at {i} for max {max}");
        }
    }
}
