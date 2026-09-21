using System.Text.Json;
using Kronikol.History;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query history</c> (plans/CROSS_RUN_HISTORY_PLAN.md §6.4): a report read against the ledger
/// as it stands now — so a downloaded artifact answers "has this been flaky?" without the run that wrote
/// it — and against the runs recorded before it, not the ones since (#95), plus the <c>history:</c> line
/// <c>failures</c> gains when a ledger is there to be read.
/// </summary>
public class QueryHistoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-query-history").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string PayId = "aaaabbbbccccdddd";
    private const string RefundId = "eeeeffff00001111";

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    /// <summary>Runs from the temp root, never from the test's own directory - which sits inside this repository, whose ledger the walk would otherwise find.</summary>
    private (string Output, string Error, int Exit) Query(Func<string, string?>? env, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error, env ?? (_ => null), workingDirectory: _dir);
        return (output.ToString(), error.ToString(), exit);
    }

    private static HistoryRoster Roster() =>
        HistoryRoster.Create("Suite", [new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null), new HistoryRosterEntry(RefundId, "Refund an order", "Checkout", null)]);

    private void Seed(params string[] results)
    {
        var roster = Roster();
        for (var i = 0; i < results.Length; i++)
        {
            var run = new HistoryRun
            {
                Id = $"gh:{i + 1}:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                Branch = "main", Commit = $"c{i:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
                Results = results[i], Attempts = "--", Durations = [100, 50], Calls = null, ShapeSet = null, ShapeOrdered = null,
                Errors = results[i].Select(r => r == 'F' ? "e1" : null).ToArray(),
                ErrorText = results[i].Contains('F') ? new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" } : new Dictionary<string, string>(),
                Deps = ["Test>orders"]
            };
            HistoryLedgerWriter.Append(Ledger, roster, run, "3.9.0");
        }
    }

    /// <summary>A current report in <c>reports/</c> under the temp root, so the ledger above it is found by the walk.</summary>
    private string WriteReport(string pay, string refund = "Passed", string runId = "99", string branch = "main")
    {
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.9.0",
              "formatVersion": 1,
              "suite": "Suite",
              "startTime": "2026-09-12T10:00:00Z",
              "endTime": "2026-09-12T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "{{branch}}", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "{{runId}}", "runAttempt": "1" },
              "features": [
                {
                  "name": "Checkout",
                  "labels": [],
                  "scenarios": [
                    { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "{{pay}}", "durationSeconds": 0.1, "errorMessage": {{(pay == "Failed" ? "\"Expected 200 but got 500\"" : "null")}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [] },
                    { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "{{refund}}", "durationSeconds": 0.05, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
                  ]
                }
              ]
            }
            """);
        return path;
    }

    [Fact]
    public void The_run_view_leads_with_the_summary_and_lists_the_scenarios_with_a_verdict()
    {
        Seed("PP", "PP", "PP", "PP", "PP");
        var report = WriteReport(pay: "Failed");

        var (output, error, exit) = Query(null, "history", report);

        Assert.True(exit == 0, error);
        Assert.Contains("history: 1 broke", output);
        Assert.Contains("against 5 earlier runs on main", output);
        Assert.Contains("s0  Checkout › Pay by card", output);
        Assert.Contains("broke", output);
        Assert.Contains("PPPPPF", output);
        Assert.Contains("passed in gh:5:1", output);
        Assert.DoesNotContain("s1  Checkout › Refund", output);
        Assert.Contains("1 scenario(s) with a verdict", output);
    }

    [Fact]
    public void A_report_that_is_not_the_newest_run_reads_against_the_runs_recorded_before_it()
    {
        // #95: run 3 passed, and runs 4 and 5, recorded after it, failed. Read again now, run 3 is not
        // "fixed" from two failures that had not happened yet.
        Seed("PP", "PP", "PP", "FP", "FP");
        var report = WriteReport(pay: "Passed", runId: "3");

        var (output, error, exit) = Query(null, "history", report);

        Assert.True(exit == 0, error);
        Assert.Contains("2 scenarios read against 2 earlier run(s)", output);
        Assert.DoesNotContain("fixed", output);
    }

    [Fact]
    public void A_report_older_than_the_window_still_reads_its_own_past()
    {
        // #95: sixty runs and a window of fifty. Run 5's own line is outside the last fifty, and it used to
        // read against runs 11 to 60, every one of them later than itself. This is the path that reads the
        // ledger file a second time, for the lines the window let go.
        Seed(Enumerable.Range(1, 60).Select(n => n <= 5 ? "PP" : "FP").ToArray());
        var report = WriteReport(pay: "Passed", runId: "5");

        var (output, error, exit) = Query(null, "history", report);

        Assert.True(exit == 0, error);
        Assert.Contains("2 scenarios read against 4 earlier run(s)", output);
        Assert.DoesNotContain("fixed", output);
    }

    [Fact]
    public void The_ledger_above_the_report_is_found_without_being_named()
    {
        Seed("PP");
        var report = WriteReport(pay: "Passed");

        var (output, error, exit) = Query(null, "history", report);

        Assert.True(exit == 0, error);
        Assert.Contains(Ledger, output);
    }

    [Fact]
    public void Filters_select_verdicts_and_the_count_form_counts_them()
    {
        Seed("PP", "FP", "PP", "FP", "PP", "FP", "PP");
        var report = WriteReport(pay: "Failed");

        var flaky = Query(null, "history", report, "--flaky");
        var regressed = Query(null, "history", report, "--regressed");
        var newOnes = Query(null, "history", report, "--new");
        var count = Query(null, "history", report, "--flaky", "--count");

        Assert.True(flaky.Exit == 0, flaky.Error);
        Assert.Contains("s0", flaky.Output);
        Assert.Contains("flaky", flaky.Output);
        // Flaky leads; broke is still there as the secondary verdict, so --regressed finds it too.
        Assert.Contains("s0", regressed.Output);
        Assert.Contains("no scenario with a verdict (new)", newOnes.Output);
        Assert.Equal("1", count.Output.Trim());
    }

    [Fact]
    public void One_scenario_is_answered_in_full_with_its_runs()
    {
        Seed("PP", "FP", "FP");
        var report = WriteReport(pay: "Failed");

        var (output, error, exit) = Query(null, "history", report, "s0");

        Assert.True(exit == 0, error);
        Assert.Contains("verdict: failing", output);
        Assert.Contains("failing since: gh:2:1", output);
        Assert.Contains("runs seen: 3", output);
        Assert.Contains("flip rate", output);
        Assert.Contains("gh:1:1", output);
        Assert.Contains("gh:3:1", output);
        Assert.Contains("Expected 200 but got 500", output);

        var byId = Query(null, "history", report, "sid:" + PayId);
        Assert.True(byId.Exit == 0, byId.Error);
        Assert.Contains("verdict: failing", byId.Output);
    }

    private void SeedPartial(int n, HistoryRoster roster, string results, int?[] durations)
    {
        HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
        {
            Id = $"gh:{n}:1", Suite = "Suite", Partial = true, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
            Branch = "main", Commit = $"c{n:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
            Results = results, Attempts = new string('-', results.Length), Durations = durations, Calls = null, ShapeSet = null, ShapeOrdered = null,
            Errors = new string?[results.Length], ErrorText = new Dictionary<string, string>(), Deps = ["Test>orders"]
        }, "3.9.0");
    }

    [Fact]
    public void The_bar_says_what_it_is_and_a_partial_row_says_so()
    {
        // #75 section 3c: the bar is scaled to this run's speed, so it can stand over every raw reading
        // under it, and "p95 of earlier runs" read as a bug. A partial run is in no bar, and its row says
        // which one it is.
        Seed("PP", "PP", "PP");
        SeedPartial(9, HistoryRoster.Create("Suite", [new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null)]), "P", [900]);
        var report = WriteReport(pay: "Passed");

        var (output, error, exit) = Query(null, "history", report, "s0");

        Assert.True(exit == 0, error);
        Assert.Contains("p95 of earlier full runs, at this run's speed", output);
        Assert.DoesNotContain("p95 of earlier runs", output);
        Assert.Contains("(partial)", output.Split('\n').Single(line => line.Contains("gh:9:1")));
        Assert.DoesNotContain("(partial)", output.Split('\n').Single(line => line.Contains("gh:3:1")));

        var json = Query(null, "history", report, "s0", "--json");
        Assert.True(json.Exit == 0, json.Error);
        using var document = System.Text.Json.JsonDocument.Parse(json.Output);
        var runs = document.RootElement.GetProperty("items")[0].GetProperty("runs").EnumerateArray().ToArray();
        Assert.True(runs.Single(r => r.GetProperty("runId").GetString() == "gh:9:1").GetProperty("partial").GetBoolean());
        Assert.False(runs.Single(r => r.GetProperty("runId").GetString() == "gh:3:1").GetProperty("partial").GetBoolean());
        Assert.False(document.RootElement.GetProperty("items")[0].GetProperty("durationP95IsRaw").GetBoolean());
    }

    [Fact]
    public void A_partial_run_reads_its_duration_against_the_raw_bar_of_full_runs()
    {
        // The report's two scenarios against a ledger of ten: partial by the heuristic, so nothing is scaled
        // to its speed and the label does not say that anything was.
        var wide = HistoryRoster.Create("Suite",
        [
            new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null), new HistoryRosterEntry(RefundId, "Refund an order", "Checkout", null),
            .. Enumerable.Range(1, 8).Select(i => new HistoryRosterEntry($"9999{i:D12}", "Other " + i, "Other", null)),
        ]);
        for (var n = 1; n <= 3; n++)
            HistoryLedgerWriter.Append(Ledger, wide, new HistoryRun
            {
                Id = $"gh:{n}:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
                Branch = "main", Commit = $"c{n:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = wide.Hash,
                Results = new string('P', 10), Attempts = new string('-', 10), Durations = [.. Enumerable.Repeat<int?>(120 + n, 10)], Calls = null, ShapeSet = null, ShapeOrdered = null,
                Errors = new string?[10], ErrorText = new Dictionary<string, string>(), Deps = ["Test>orders"]
            }, "3.9.0");
        var report = WriteReport(pay: "Passed");

        var (output, error, exit) = Query(null, "history", report, "s0");

        Assert.True(exit == 0, error);
        Assert.Contains("duration: 100 ms · p95 of earlier full runs 123 ms (this run is partial, so nothing is scaled to its speed)", output);
    }

    [Fact]
    public void Calls_prints_what_the_templater_made_of_a_scenarios_calls()
    {
        // A wrong {id} is silent: two routes collapse into one line and a real change disappears. --calls
        // shows the reader what the templater took, so /assets/app.{id}.js can be seen and objected to.
        Seed("PP", "PP");
        var report = WriteReport(pay: "Passed");
        var roster = Roster();
        var shapes = HistoryShapes.Create(["Test>orders GET /assets/app.{id}.js 200", "Test>orders POST /orders/{n}/pay 201"]);
        var run = new HistoryRun
        {
            Id = "gh:99:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 12, 10, 5, 0, TimeSpan.Zero),
            Branch = "main", Commit = "abc1234", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
            Results = "PP", Attempts = "--", Durations = [100, 50], Calls = [2, 0], ShapeSet = ["aaaaaaaa", "bbbbbbbb"], ShapeOrdered = ["aaaaaaaa", "bbbbbbbb"],
            ShapeVersion = InteractionShape.Version, ShapesHash = shapes.Hash, CallSets = [[0, 1], []],
            Errors = new string?[2], ErrorText = new Dictionary<string, string>(), Deps = ["Test>orders"]
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(report)!, HistoryFormat.FragmentFileName), HistoryFragment.Write(roster, run, "3.25.0", shapes));

        var (output, error, exit) = Query(null, "history", report, "s0", "--calls");
        Assert.True(exit == 0, error);
        Assert.Contains("distinct calls, as templated (2):", output);
        Assert.Contains("  Test>orders GET /assets/app.{id}.js 200", output);
        Assert.Contains("  Test>orders POST /orders/{n}/pay 201", output);

        var without = Query(null, "history", report, "s0");
        Assert.DoesNotContain("as templated", without.Output);

        using var document = System.Text.Json.JsonDocument.Parse(Query(null, "history", report, "s0", "--calls", "--json").Output);
        Assert.Equal(2, document.RootElement.GetProperty("items")[0].GetProperty("calls").GetArrayLength());

        // No call list to print: the report was read without the run's own line.
        File.Delete(Path.Combine(Path.GetDirectoryName(report)!, HistoryFormat.FragmentFileName));
        var bare = Query(null, "history", report, "s0", "--calls");
        Assert.Contains("no call list for this run", bare.Output);
    }

    [Fact]
    public void Calls_are_printed_for_the_scenario_that_collects_the_traffic_no_test_was_given()
    {
        // It is not a test, so no verdict is read for it, but its calls went through the templater like
        // any other and that is what --calls shows. It answered "no call list for this run: it needs the
        // History.run.json" with the file sitting beside the report.
        Seed("PP", "PP");
        var report = WriteReport(pay: "Passed");
        var roster = Roster();
        var shapes = HistoryShapes.Create(["Test>orders POST /orders/{n}/pay 201", "orders>ledger GET /entries/{id} 200"]);
        var run = new HistoryRun
        {
            Id = "gh:99:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 12, 10, 5, 0, TimeSpan.Zero),
            Branch = "main", Commit = "abc1234", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
            Results = "PN", Attempts = "--", Durations = [100, null], Calls = [1, 1], ShapeSet = ["aaaaaaaa", "bbbbbbbb"], ShapeOrdered = ["aaaaaaaa", "bbbbbbbb"],
            ShapeVersion = InteractionShape.Version, ShapesHash = shapes.Hash, CallSets = [[0], [1]],
            Errors = new string?[2], ErrorText = new Dictionary<string, string>(), Deps = ["Test>orders"]
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(report)!, HistoryFormat.FragmentFileName), HistoryFragment.Write(roster, run, "3.25.0", shapes));

        var (output, error, exit) = Query(null, "history", report, "s1", "--calls");

        Assert.True(exit == 0, error);
        Assert.Contains("distinct calls, as templated (1):", output);
        Assert.Contains("  orders>ledger GET /entries/{id} 200", output);
        Assert.DoesNotContain("Test>orders POST", output[output.IndexOf("as templated", StringComparison.Ordinal)..]);

        using var document = System.Text.Json.JsonDocument.Parse(Query(null, "history", report, "s1", "--calls", "--json").Output);
        Assert.Equal("orders>ledger GET /entries/{id} 200", document.RootElement.GetProperty("items")[0].GetProperty("calls")[0].GetString());
    }

    // ─── Degraded runs (#83) ───────────────────────────────────

    private static readonly string[] WideIds = [PayId, RefundId, .. Enumerable.Range(1, 8).Select(i => $"7777{i:D12}")];

    private static HistoryRoster WideRoster() =>
        HistoryRoster.Create("Suite", WideIds.Select((id, i) => new HistoryRosterEntry(id, i == 0 ? "Pay by card" : i == 1 ? "Refund an order" : "Other " + (i - 1), "Checkout", null)).ToArray());

    private void SeedWide(int n, int payMs, int othersMs, bool payFailed = false)
    {
        var roster = WideRoster();
        var results = (payFailed ? "F" : "P") + new string('P', 9);
        HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
        {
            Id = $"gh:{n}:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
            Branch = "main", Commit = $"c{n:D6}", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
            Results = results, Attempts = new string('-', 10), Durations = [payMs, .. Enumerable.Repeat<int?>(othersMs, 9)], Calls = null, ShapeSet = null, ShapeOrdered = null,
            Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
            ErrorText = payFailed ? new Dictionary<string, string> { ["e1"] = "The service bigquery has thrown" } : new Dictionary<string, string>(),
            Deps = ["Test>orders"]
        }, "3.9.0");
    }

    /// <summary>A current report listing all ten scenarios of the wide roster, every one passing in <paramref name="ms"/>.</summary>
    private string WriteWideReport(int ms)
    {
        var directory = Path.Combine(_dir, "reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TestRunReport.json");
        var roster = WideRoster();
        var scenarios = string.Join(",\n", Enumerable.Range(0, roster.Count).Select(i =>
            $$"""{ "id": "t{{i}}", "stableId": "{{roster.Ids[i]}}", "name": "{{roster.Names[i]}}", "result": "Passed", "durationSeconds": {{(ms / 1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }"""));
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.9.0",
              "formatVersion": 1,
              "suite": "Suite",
              "startTime": "2026-09-12T10:00:00Z",
              "endTime": "2026-09-12T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "99", "runAttempt": "1" },
              "features": [ { "name": "Checkout", "labels": [], "scenarios": [ {{scenarios}} ] } ]
            }
            """);
        return path;
    }

    [Fact]
    public void A_failure_in_a_degraded_run_says_so_on_its_row_and_in_the_statistics()
    {
        // #83: what read as "unreliable test" was the statistics line, flip rate 0.50 and fail rate 0.20, with
        // nothing to say that the one failure happened in a run where everything was slow.
        for (var n = 1; n <= 6; n++) SeedWide(n, payMs: 12_768, othersMs: 1000);
        SeedWide(7, payMs: 82_215, othersMs: 2300, payFailed: true);
        SeedWide(8, payMs: 12_768, othersMs: 1000);
        var report = WriteWideReport(1000);

        var (output, error, exit) = Query(null, "history", report, "s0");

        Assert.True(exit == 0, error);
        Assert.Contains("failed 1 (1 in a degraded run)", output);
        var row = output.Split('\n').Single(line => line.Contains("gh:7:1"));
        Assert.Contains("82215 ms (6.4× usual)  [run degraded: passing scenarios took 2.3× their usual]  The service bigquery has thrown", row);
        // A row that is neither over its usual nor in a degraded run says nothing.
        var healthy = output.Split('\n').Single(line => line.Contains("gh:6:1"));
        Assert.DoesNotContain("usual", healthy);
        Assert.DoesNotContain("degraded", healthy);

        var json = Query(null, "history", report, "s0", "--json");
        Assert.True(json.Exit == 0, json.Error);
        using var document = System.Text.Json.JsonDocument.Parse(json.Output);
        var item = document.RootElement.GetProperty("items")[0];
        Assert.Equal(1, item.GetProperty("failuresInDegradedRuns").GetInt32());
        var failed = item.GetProperty("runs").EnumerateArray().Single(r => r.GetProperty("runId").GetString() == "gh:7:1");
        Assert.Equal(6.44, failed.GetProperty("timesUsual").GetDouble(), 2);
        Assert.True(failed.GetProperty("overUsual").GetBoolean());
        Assert.True(failed.GetProperty("runDegraded").GetBoolean());
        Assert.False(item.GetProperty("runs").EnumerateArray().First().GetProperty("runDegraded").GetBoolean());
    }

    [Fact]
    public void A_degraded_run_is_labelled_once_at_run_level_and_the_threshold_is_a_flag()
    {
        // On a surface that lists a whole run the run's label speaks, not the rows: measured, 72 to 88 of 203
        // rows cleared the row note's test in a contended run.
        for (var n = 1; n <= 6; n++) SeedWide(n, payMs: 1000, othersMs: 1000);
        var report = WriteWideReport(3000);

        var (output, error, exit) = Query(null, "history", report);
        Assert.True(exit == 0, error);
        Assert.Contains("degraded: passing scenarios took 3.0× their usual, so no scenario is read slower in this run", output);

        var json = Query(null, "history", report, "--json");
        using (var document = System.Text.Json.JsonDocument.Parse(json.Output))
        {
            var history = document.RootElement.GetProperty("history");
            Assert.Equal(3.0, history.GetProperty("pace").GetDouble(), 2);
            Assert.True(history.GetProperty("degraded").GetBoolean());
        }

        var lenient = Query(null, "history", report, "--degraded-by", "4");
        Assert.True(lenient.Exit == 0, lenient.Error);
        Assert.DoesNotContain("degraded", lenient.Output);

        var bad = Query(null, "history", report, "--degraded-by", "fast");
        Assert.Equal(2, bad.Exit);
        Assert.Contains("--degraded-by takes a factor", bad.Error);
    }

    [Fact]
    public void A_healthy_run_says_nothing_about_pace_in_text_and_carries_it_in_json()
    {
        for (var n = 1; n <= 6; n++) SeedWide(n, payMs: 1000, othersMs: 1000);
        var report = WriteWideReport(1100);

        var (output, _, _) = Query(null, "history", report);
        Assert.DoesNotContain("degraded", output);

        using var document = System.Text.Json.JsonDocument.Parse(Query(null, "history", report, "--json").Output);
        Assert.Equal(1.1, document.RootElement.GetProperty("history").GetProperty("pace").GetDouble(), 2);
        Assert.False(document.RootElement.GetProperty("history").GetProperty("degraded").GetBoolean());
    }

    [Fact]
    public void Json_carries_the_rows_and_the_run_level_member()
    {
        Seed("PP", "PP");
        var report = WriteReport(pay: "Failed");

        var (output, error, exit) = Query(null, "history", report, "--json");

        Assert.True(exit == 0, error);
        var envelope = JsonDocument.Parse(output).RootElement;
        var item = Assert.Single(envelope.GetProperty("items").EnumerateArray());
        Assert.Equal("broke", item.GetProperty("primary").GetString());
        Assert.Equal("s0", item.GetProperty("address").GetString());
        Assert.Equal("PPF", item.GetProperty("series").GetString());
        var history = envelope.GetProperty("history");
        Assert.Equal("main", history.GetProperty("stream").GetString());
        Assert.Equal(2, history.GetProperty("runsRecorded").GetInt32());
        Assert.Equal(1, history.GetProperty("counts").GetProperty("broke").GetInt32());
        Assert.False(history.GetProperty("behaviourVerdicts").GetBoolean());
    }

    [Fact]
    public void A_compare_branch_adds_a_second_reading()
    {
        Seed("PP", "PP");
        var report = WriteReport(pay: "Failed");
        var roster = Roster();
        HistoryLedgerWriter.Append(Ledger, roster, new HistoryRun
        {
            Id = "gh:50:1", Suite = "Suite", Partial = false, At = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), Branch = "feature/x", Commit = "fff",
            Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash, Results = "FP", Attempts = "--", Errors = ["e1", null],
            ErrorText = new Dictionary<string, string> { ["e1"] = "boom" }
        }, "3.9.0");

        var (output, error, exit) = Query(null, "history", report, "--branch", "feature/x", "--compare-branch", "main");

        Assert.True(exit == 0, error);
        Assert.Contains("stream feature/x", output);
        Assert.Contains("on main: 1 broke", output);
    }

    [Fact]
    public void Without_a_fragment_beside_the_report_behaviour_verdicts_are_declared_off()
    {
        Seed("PP");
        var report = WriteReport(pay: "Passed");

        var (output, _, _) = Query(null, "history", report);

        Assert.Contains("! no History.run.json beside the report", output);
    }

    [Fact]
    public void The_fragment_beside_the_report_is_the_run_line_when_it_matches()
    {
        Seed("PP");
        var report = WriteReport(pay: "Passed");
        var roster = Roster();
        var fragmentRun = new HistoryRun
        {
            Id = "gh:99:1", Suite = "Suite", Partial = null, At = new DateTimeOffset(2026, 9, 12, 10, 5, 0, TimeSpan.Zero), Branch = "main", Commit = "abc1234",
            Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash, Results = "PP", Attempts = "--",
            Durations = [100, 50], Calls = [3, 1], ShapeSet = ["11111111", "22222222"], ShapeOrdered = ["11111111", "22222222"], Errors = [null, null]
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(report)!, HistoryFormat.FragmentFileName), HistoryFragment.Write(roster, fragmentRun, "3.9.0"));

        var (output, error, exit) = Query(null, "history", report, "--json");

        Assert.True(exit == 0, error);
        Assert.DoesNotContain("no History.run.json", output);
        Assert.True(JsonDocument.Parse(output).RootElement.GetProperty("history").GetProperty("behaviourVerdicts").GetBoolean());
    }

    [Fact]
    public void No_ledger_anywhere_is_a_usage_error_that_names_every_way_to_get_one()
    {
        var report = WriteReport(pay: "Failed");

        var (_, error, exit) = Query(null, "history", report);

        Assert.Equal(2, exit);
        Assert.Contains("--history", error);
        Assert.Contains("KRONIKOL_HISTORY", error);
        Assert.Contains("kronikol history init", error);
    }

    [Fact]
    public void History_off_in_the_environment_is_refused_unless_a_ledger_is_named()
    {
        Seed("PP");
        var report = WriteReport(pay: "Failed");

        var refused = Query(name => name == "KRONIKOL_HISTORY" ? "off" : null, "history", report);
        var named = Query(name => name == "KRONIKOL_HISTORY" ? "off" : null, "history", report, "--history", Ledger);

        Assert.Equal(2, refused.Exit);
        Assert.Contains("--history", refused.Error);
        Assert.Equal(0, named.Exit);
    }

    [Fact]
    public void A_report_without_stable_ids_cannot_be_read_against_history()
    {
        Seed("PP");
        var report = WriteReport(pay: "Failed").Replace("TestRunReport.json", "Old.json", StringComparison.Ordinal);
        File.WriteAllText(report, File.ReadAllText(Path.Combine(_dir, "reports", "TestRunReport.json")).Replace("\"stableId\": \"" + PayId + "\", ", "", StringComparison.Ordinal));
        File.Delete(Path.Combine(_dir, "reports", "TestRunReport.json"));

        var (_, error, exit) = Query(null, "history", report);

        Assert.Equal(2, exit);
        Assert.Contains("stableIds", error);
    }

    [Fact]
    public void Failures_gains_a_history_line_when_a_ledger_is_there_and_stays_silent_when_not()
    {
        var report = WriteReport(pay: "Failed");
        var silent = Query(null, "failures", report);
        Assert.True(silent.Exit == 0, silent.Error);
        Assert.DoesNotContain("history:", silent.Output);

        Seed("PP", "PP");
        var (output, error, exit) = Query(null, "failures", report);

        Assert.True(exit == 0, error);
        Assert.Contains("history: broke — passed in gh:2:1", output);
        Assert.Contains("PPF", output);
    }

    [Fact]
    public void The_verb_is_described_and_its_flags_are_refused_elsewhere()
    {
        var describe = Query(null, "--describe");
        Assert.Contains("\"history\"", describe.Output);

        Seed("PP");
        var report = WriteReport(pay: "Failed");
        var refused = Query(null, "failures", report, "--flaky");
        Assert.Equal(2, refused.Exit);
        Assert.Contains("--flaky", refused.Error);
    }

    [Fact]
    public void A_pull_request_build_reads_against_the_branch_it_targets()
    {
        // On a pull request build the run read against GITHUB_BASE_REF; queried in the same job, the report
        // reads the same way without --branch, which still names any other stream.
        Seed("PP", "PP", "PP");
        var report = WriteReport(pay: "Failed", branch: "42/merge");
        string? PullRequest(string key) => key switch { "GITHUB_ACTIONS" => "true", "GITHUB_BASE_REF" => "main", _ => null };

        var pullRequest = Query(PullRequest, "history", report);

        Assert.True(pullRequest.Exit == 0, pullRequest.Error);
        Assert.Contains("stream main", pullRequest.Output);
        Assert.Contains("1 broke", pullRequest.Output);
        Assert.Contains("stream 42/merge", Query(null, "history", report).Output);
        Assert.Contains("stream feature/x", Query(PullRequest, "history", report, "--branch", "feature/x").Output);
    }

    [Fact]
    public void Min_runs_is_the_bar_the_report_used()
    {
        // The report takes HistoryMinRuns and the gate takes --min-runs; the query reads the same run the
        // same way when given the same bar, instead of calling it a cold start.
        Seed("PP", "FP", "PP");
        var report = WriteReport(pay: "Failed");

        var five = Query(null, "history", report);
        var three = Query(null, "history", report, "--min-runs", "3");

        Assert.Contains("flakiness and duration verdicts need 5", five.Output);
        Assert.Contains("against 3 earlier runs on main", three.Output);
        Assert.Contains("flaky", three.Output);
        Assert.Equal(2, Query(null, "history", report, "--min-runs", "0").Exit);
        Assert.Equal(2, Query(null, "history", report, "--min-runs", "three").Exit);
    }

    [Fact]
    public void Alternating_runs_is_taken_and_checked()
    {
        Seed("PP", "PP", "PP");
        var report = WriteReport(pay: "Passed");

        Assert.Equal(0, Query(null, "history", report, "--alternating-runs", "3").Exit);
        Assert.Equal(2, Query(null, "history", report, "--alternating-runs", "0").Exit);
        Assert.Equal(2, Query(null, "history", report, "--alternating-runs", "ten").Exit);
    }

    [Fact]
    public void Count_runs_is_taken_and_checked()
    {
        Seed("PP", "PP", "PP");
        var report = WriteReport(pay: "Passed");

        Assert.Equal(0, Query(null, "history", report, "--count-runs", "3").Exit);
        Assert.Equal(2, Query(null, "history", report, "--count-runs", "0").Exit);
        Assert.Equal(2, Query(null, "history", report, "--count-runs", "ten").Exit);
    }
}
