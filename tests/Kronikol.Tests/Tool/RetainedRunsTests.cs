using System.Text;
using Kronikol.History;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tests.Compression;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The tool's half of retained runs (plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md §6, #80): <c>--run</c> opens a
/// run kept under <c>runs/</c> on any verb, the directory sweeps leave <c>runs/</c> alone, and
/// <c>merge</c> stops choking on the files Kronikol itself writes beside a report (F11).
/// </summary>
public class RetainedRunsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-retained").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string PayId = "aaaabbbbccccdddd";
    private const string RefundId = "eeeeffff00001111";

    private string Reports => Path.Combine(_dir, "proj", "Reports");

    private (string Output, string Error, int Exit) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error, _ => null, workingDirectory: _dir);
        return (output.ToString(), error.ToString(), exit);
    }

    private static string Report(string pay, string runId, bool attachment = false, string? body = null) => $$"""
        {
          "kronikolVersion": "3.9.0", "formatVersion": 1, "suite": "Suite",
          "startTime": "2026-09-12T10:00:00Z", "endTime": "2026-09-12T10:05:00Z",
          "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "{{runId}}", "runAttempt": "1" },
          "features": [ { "name": "Checkout", "labels": [], "scenarios": [
            { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "{{pay}}", "durationSeconds": 0.1, "errorMessage": {{(pay == "Failed" ? "\"Expected 200 but got 500\"" : "null")}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [{{(body is null ? "" : $$"""{ "type": "Request", "method": "POST", "uri": "https://pay/charge", "serviceName": "pay", "callerName": "test", "headers": [], "content": {{System.Text.Json.JsonSerializer.Serialize(body)}}, "requestResponseId": "{{Guid.NewGuid()}}" }""")}}],
              "attachments": [ {{(attachment ? "{ \"name\": \"checkout.png\", \"relativePath\": \"attachments/checkout.png\", \"mediaType\": \"image/png\" }" : "")}} ] },
            { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "Passed", "durationSeconds": 0.05, "labels": [], "categories": [], "steps": [], "httpInteractions": [] } ] } ]
        }
        """;

    /// <summary>
    /// One run's files in <paramref name="directory"/>: the report, as JSON or, as 5.0.0 writes it, compressed
    /// (<paramref name="krkl"/>), its manifest, and - for a run that kept them - its fragment and an attachment.
    /// </summary>
    private static void WriteRun(string directory, string runId, string pay, DateTimeOffset at, bool withFragmentAndAttachment = false, string? body = null, string name = "TestRunReport", bool krkl = false)
    {
        Directory.CreateDirectory(directory);
        var report = Report(pay, runId, withFragmentAndAttachment, body);
        var data = name + (krkl ? ".krkl" : ".json");
        if (krkl)
            File.WriteAllBytes(Path.Combine(directory, data), ReportFileFormatTests.Krkl(Encoding.UTF8.GetBytes(report)));
        else
            File.WriteAllText(Path.Combine(directory, data), report);
        File.WriteAllText(Path.Combine(directory, name + ".html"), "<html></html>");
        var files = new List<string> { data, name + ".html" };
        var attachments = new List<string>();
        if (withFragmentAndAttachment)
        {
            var roster = HistoryRoster.Create("Suite", [new HistoryRosterEntry(PayId, "Pay by card", "Checkout", null), new HistoryRosterEntry(RefundId, "Refund an order", "Checkout", null)]);
            var run = new HistoryRun
            {
                Id = $"gh:{runId}:1", Suite = "Suite", Partial = null, At = at, Branch = "main", Commit = "abc1234", Provider = "GitHubActions", Url = null, Shards = 1,
                RosterHash = roster.Hash, Results = pay == "Failed" ? "FP" : "PP", Attempts = "--", Durations = [100, 50], Calls = [3, 1],
                ShapeSet = ["11111111", "22222222"], ShapeOrdered = ["11111111", "22222222"], Errors = pay == "Failed" ? ["e1", null] : [null, null],
                ErrorText = pay == "Failed" ? new Dictionary<string, string> { ["e1"] = "Expected 200 but got 500" } : new Dictionary<string, string>()
            };
            File.WriteAllText(Path.Combine(directory, HistoryFormat.FragmentFileName), HistoryFragment.Write(roster, run, "3.9.0"));
            files.Add(HistoryFormat.FragmentFileName);
            Directory.CreateDirectory(Path.Combine(directory, "attachments"));
            File.WriteAllBytes(Path.Combine(directory, "attachments", "checkout.png"), [1, 2, 3]);
            attachments.Add("attachments/checkout.png");
        }

        new RunManifest
        {
            Run = $"gh:{runId}:1", At = at, Suite = "Suite", Scenarios = 2, Failed = pay == "Failed" ? 1 : 0, KronikolVersion = "3.9.0",
            Files = files, Attachments = attachments
        }.Write(directory);
    }

    private static readonly DateTimeOffset Noon = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The issue's directory after S4: the green re-run on top, the failing run it replaced kept beneath.</summary>
    private void TheIssue()
    {
        WriteRun(Path.Combine(Reports, "runs", "gh_7_1"), "7", "Failed", Noon.AddMinutes(-20), withFragmentAndAttachment: true);
        WriteRun(Path.Combine(Reports, "runs", "gh_8_1"), "8", "Passed", Noon.AddMinutes(-10));
        WriteRun(Reports, "9", "Passed", Noon);
    }

    [Fact]
    public void Run_opens_a_retained_run_on_any_verb_and_its_siblings_are_found_beside_it()
    {
        TheIssue();

        var failures = Query("failures", Reports, "--run", "last-failed");
        var summary = Query("summary", Reports, "--run", "gh:7:1");
        var history = Query("history", Reports, "--run", "last-failed", "--history", Path.Combine(_dir, "none.jsonl"));

        Assert.True(failures.Exit == 0, failures.Error);
        Assert.True(failures.Output.Contains("Expected 200 but got 500"), failures.Output);
        // The attachment is the retained run's own, under runs/, and it is there.
        var attachment = Path.Combine(Reports, "runs", "gh_7_1", "attachments", "checkout.png");
        Assert.Contains(attachment, failures.Output);
        Assert.True(File.Exists(attachment));
        // The deep link resolves against the retained HTML, not the newest run's.
        Assert.Contains("TestRunReport.html#sid-", failures.Output);
        Assert.True(summary.Exit == 0, summary.Error);
        Assert.Contains("1 failed", summary.Output, StringComparison.OrdinalIgnoreCase);
        // history reads the retained report WITH its fragment: no "no History.run.json" banner, run id the fragment's.
        Assert.True(history.Exit == 0, history.Error);
        Assert.Contains("run gh:7:1", history.Output);
        Assert.DoesNotContain("no History.run.json beside the report", history.Output);
    }

    [Fact]
    public void Run_opens_a_retained_run_while_the_run_on_top_is_still_being_written()
    {
        // A run in progress has moved the previous report to runs/ and not yet written its own: for as
        // long as the suite runs there is no report and no Run.json on top (plan §9 step 4 deletes the
        // report and asks the same). The failing run kept beneath is exactly what a person waiting on a
        // long suite asks for, and it is there to be read.
        TheIssue();
        File.Delete(Path.Combine(Reports, "TestRunReport.json"));
        File.Delete(Path.Combine(Reports, "TestRunReport.html"));
        File.Delete(Path.Combine(Reports, RunManifest.FileName));

        var failures = Query("failures", Reports, "--run", "last-failed");
        var previous = Query("summary", Reports, "--run", "previous");
        var unknown = Query("failures", Reports, "--run", "gh:99:1");
        var plain = Query("failures", Reports);

        Assert.True(failures.Exit == 0, failures.Error);
        Assert.Contains("Expected 200 but got 500", failures.Output);
        Assert.True(previous.Exit == 0, previous.Error);
        Assert.Contains("0 failed", previous.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, unknown.Exit);
        Assert.Contains("not retained", unknown.Error);
        // Without --run the directory is what it was: no report yet.
        Assert.Equal(2, plain.Exit);
    }

    [Fact]
    public void Previous_is_the_newest_retained_run_and_the_top_level_run_answers_to_its_own_id()
    {
        TheIssue();

        var previous = Query("scenarios", Reports, "--run", "previous", "--json");
        var own = Query("scenarios", Reports, "--run", "gh:9:1", "--json");

        Assert.True(previous.Exit == 0, previous.Error);
        Assert.Equal(Path.Combine(Reports, "runs", "gh_8_1", "TestRunReport.json"), System.Text.Json.JsonDocument.Parse(previous.Output).RootElement.GetProperty("report").GetString());
        Assert.True(own.Exit == 0, own.Error);
        Assert.Equal(Path.Combine(Reports, "TestRunReport.json"), System.Text.Json.JsonDocument.Parse(own.Output).RootElement.GetProperty("report").GetString());
    }

    [Fact]
    public void A_run_that_is_not_retained_is_refused_with_what_is_and_the_ledger_named_as_the_way_on()
    {
        TheIssue();

        var (_, error, exit) = Query("failures", Reports, "--run", "gh:3:1");

        Assert.Equal(2, exit);
        Assert.Contains("gh:3:1 is not retained under", error);
        Assert.Contains("gh:7:1", error);
        Assert.Contains("gh:8:1", error);
        Assert.Contains("history --run gh:3:1", error);
    }

    [Fact]
    public void Two_retained_runs_with_one_ci_id_are_refused_by_time_and_failures_and_a_staging_folder_is_never_offered()
    {
        // F9: two steps of one workflow run share an id, so the directory names differ and the id does not.
        WriteRun(Path.Combine(Reports, "runs", "gh_1_1"), "1", "Failed", Noon.AddMinutes(-20));
        WriteRun(Path.Combine(Reports, "runs", "gh_1_1-2"), "1", "Passed", Noon.AddMinutes(-10));
        WriteRun(Path.Combine(Reports, "runs", ".incoming-gh_1_1-3"), "1", "Failed", Noon.AddMinutes(-5));
        WriteRun(Reports, "2", "Passed", Noon);

        var (_, error, exit) = Query("failures", Reports, "--run", "gh:1:1");
        var byFolder = Query("failures", Reports, "--run", "gh_1_1-2");

        Assert.Equal(2, exit);
        Assert.Contains("gh_1_1 ", error);
        Assert.Contains("gh_1_1-2", error);
        Assert.Contains("1 failed", error);
        Assert.DoesNotContain(".incoming", error);
        // The folder name is what tells them apart, and it is accepted.
        Assert.True(byFolder.Exit == 0, byFolder.Error);
    }

    [Fact]
    public void A_directory_lookup_is_not_made_ambiguous_by_the_runs_beneath_it()
    {
        TheIssue();

        // On main: "Several reports under …" - three of them, two being the retained runs.
        var (output, error, exit) = Query("summary", Path.Combine(_dir, "proj"));

        Assert.True(exit == 0, error);
        Assert.Contains("0 failed", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A download of a matrix's or a sharded run's artifacts holds a report per leg, under artifact names that need not
    /// say which leg is which, and the list of several gave bare paths (plans/CI_SUMMARY_AUDIENCE_PLAN.md F12, Q12). Each
    /// line now carries the report's size and, from the Run.json its run wrote beside it, its suite and failures; a merge
    /// writes no Run.json, and a Run.json that does not list the report is another run's.
    /// </summary>
    [Fact]
    public void Several_reports_are_listed_with_their_size_suite_and_failures()
    {
        var download = Path.Combine(_dir, "kronikol-7");
        WriteRun(Path.Combine(download, "leg-a"), "7", "Failed", Noon);
        WriteRun(Path.Combine(download, "leg-b"), "7", "Passed", Noon);
        Directory.CreateDirectory(Path.Combine(download, "merged"));
        File.WriteAllText(Path.Combine(download, "merged", "TestRunReport.json"), Report("Failed", "7"));
        Directory.CreateDirectory(Path.Combine(download, "stale"));
        File.WriteAllText(Path.Combine(download, "stale", "TestRunReport.json"), Report("Passed", "6"));
        new RunManifest { Run = "gh:6:1", At = Noon, Suite = "Other", Scenarios = 9, Failed = 9, Files = ["Checkout.json"] }.Write(Path.Combine(download, "stale"));

        var (output, error, exit) = Query("failures", download);

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        var lines = error.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        Assert.Equal($"Several reports under {download} — name the one you mean:", lines[0]);
        Assert.Equal(5, lines.Length);
        string Line(string leg) =>
            $"  {Path.Combine(download, leg, "TestRunReport.json")}  {RunSummaryConsoleWriter.Size(new FileInfo(Path.Combine(download, leg, "TestRunReport.json")).Length)}";
        Assert.Contains(Line("leg-a") + " · suite Suite · 1 of 2 failed", lines);
        Assert.Contains(Line("leg-b") + " · suite Suite · 0 of 2 failed", lines);
        Assert.Contains(Line("merged"), lines);
        Assert.Contains(Line("stale"), lines);
    }

    /// <summary>
    /// The list stopped at twenty without a word, so a download of a sharded run read as if it held twenty reports; the
    /// section that sends a reader there says the query lists them.
    /// </summary>
    [Fact]
    public void A_list_of_more_than_twenty_reports_says_how_many_it_leaves_out()
    {
        var download = Path.Combine(_dir, "kronikol-7");
        for (var leg = 0; leg < 24; leg++)
            WriteRun(Path.Combine(download, $"shard-{leg:00}"), "7", "Passed", Noon);

        var (output, error, exit) = Query("failures", download);

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        var lines = error.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        Assert.Equal(20, lines.Count(l => l.Contains("TestRunReport.json", StringComparison.Ordinal)));
        Assert.Equal("  … and 4 more", lines[^1]);
    }

    /// <summary>
    /// A kept run was looked up as a folder, which finds a <c>TestRunReport.json</c> and nothing else: with its own
    /// report name every <c>--run</c> answered "No TestRunReport.json under …runs/gh_7_1", the previous-run line the
    /// CI text prints for such a run included.
    /// </summary>
    [Fact]
    public void Run_opens_a_kept_run_whose_report_has_its_own_name()
    {
        WriteRun(Path.Combine(Reports, "runs", "gh_7_1"), "7", "Failed", Noon.AddMinutes(-10), name: "Checkout");
        WriteRun(Reports, "8", "Passed", Noon, name: "Checkout");

        foreach (var wanted in new[] { "gh_7_1", "gh:7:1", "last-failed", "previous" })
        {
            var (output, error, exit) = Query("failures", Path.Combine(Reports, "Checkout.json"), "--run", wanted);

            Assert.True(exit == 0, wanted + ": " + error);
            Assert.Contains("Expected 200 but got 500", output);
        }
    }

    /// <summary>
    /// A run kept by 4.x under its own report name wrote <c>Checkout.json</c>, and the run on top since 5.0.0 writes
    /// <c>Checkout.krkl</c>: the kept report was looked for as <c>Checkout.krkl</c> and then as a folder, which finds no
    /// JSON but a <c>TestRunReport.json</c>, so every <c>--run</c> and <c>--baseline-run</c> answered "No TestRunReport.json
    /// or TestRunReport.krkl under …runs/gh_7_1", the commands the run prints for its previous run included (5.2.7).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_opens_a_kept_run_whose_own_named_report_has_the_other_extension(bool keptAsJson)
    {
        WriteRun(Path.Combine(Reports, "runs", "gh_7_1"), "7", "Failed", Noon.AddMinutes(-10), name: "Checkout", krkl: !keptAsJson);
        WriteRun(Reports, "8", "Passed", Noon, name: "Checkout", krkl: keptAsJson);
        var top = Path.Combine(Reports, keptAsJson ? "Checkout.krkl" : "Checkout.json");

        foreach (var wanted in new[] { "gh_7_1", "gh:7:1", "last-failed", "previous" })
        {
            var (output, error, exit) = Query("failures", top, "--run", wanted);

            Assert.True(exit == 0, wanted + ": " + error);
            Assert.Contains("Expected 200 but got 500", output);
        }
        var diff = Query("diff", top, "--baseline-run", "last-failed");
        Assert.True(diff.Exit == 0, diff.Error);
        Assert.Contains("fixed s0 Pay by card", diff.Output);
    }

    [Fact]
    public void Merge_sweeps_neither_the_retained_runs_nor_the_files_kronikol_writes_beside_a_report()
    {
        var shard = Path.Combine(_dir, "shards", "Reports");
        Directory.CreateDirectory(shard);
        var fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "Reports", "all-passing.mergeable.json");
        File.Copy(fixture, Path.Combine(shard, "TestRunReport.json"));
        File.WriteAllText(Path.Combine(shard, HistoryFormat.FragmentFileName), "{\"historyFormatVersion\":1}");
        File.WriteAllText(Path.Combine(shard, "ctrf-report.json"), "{\"results\":{}}");
        new RunManifest { Run = "gh:9:1", At = Noon, Suite = "Suite", Scenarios = 11, Failed = 0, Files = ["TestRunReport.json"] }.Write(shard);
        // A retained run that DIFFERS: on main it is merged in as a shard, and a green report gains its failure (F6).
        var retained = Path.Combine(shard, "runs", "gh_8_1");
        Directory.CreateDirectory(retained);
        File.WriteAllText(Path.Combine(retained, "TestRunReport.json"), File.ReadAllText(fixture).Replace("\"Passed\"", "\"Failed\"", StringComparison.Ordinal));

        var error = new StringWriter();
        var swept = MergeCommand.ResolveInputFiles([shard], error);
        var named = MergeCommand.ResolveInputFiles([Path.Combine(shard, HistoryFormat.FragmentFileName)], error);

        Assert.Equal([Path.Combine(shard, "TestRunReport.json")], swept);
        Assert.Contains("skipped 3 files Kronikol writes beside a report", error.ToString());
        // Found versus given: a file named on the command line is read, and refused for what it is.
        Assert.Single(named);
    }

    // plans/EVIDENCE_SURVIVES_A_RERUN_PLAN.md open question 6: "what differs between the failing run and the
    // green re-run" is the flaky-debugging question, and the old side of diff could be named only by path.
    // --baseline-run takes what --run takes and resolves it against the same directory.
    [Fact]
    public void Diff_baseline_run_compares_the_report_against_a_kept_run()
    {
        TheIssue();

        var (output, error, exit) = Query("diff", Reports, "--baseline-run", "last-failed");

        Assert.True(exit == 0, error);
        // The kept run is the old side and the report the new one; both files are TestRunReport.json, so
        // the labels are the paths relative to the directory they share.
        Assert.Contains("- " + Path.Combine("runs", "gh_7_1", "TestRunReport.json"), output);
        Assert.Contains("+ TestRunReport.json", output);
        Assert.Contains("Fixed (1):", output);
        Assert.Contains("fixed s0 Pay by card", output);
    }

    [Fact]
    public void Diff_baseline_run_takes_a_folder_name_and_composes_with_run()
    {
        TheIssue();

        // Two kept runs against each other: the subject moved by --run, the old side by --baseline-run.
        var (output, error, exit) = Query("diff", Reports, "--run", "previous", "--baseline-run", "gh_7_1");

        Assert.True(exit == 0, error);
        Assert.Contains("- " + Path.Combine("gh_7_1", "TestRunReport.json"), output);
        Assert.Contains("+ " + Path.Combine("gh_8_1", "TestRunReport.json"), output);
        Assert.Contains("Fixed (1):", output);
    }

    [Fact]
    public void Diff_baseline_run_with_a_body_labels_the_two_sides_apart()
    {
        // Both files are TestRunReport.json, and the body form labelled each side with its file name alone.
        WriteRun(Path.Combine(Reports, "runs", "gh_7_1"), "7", "Failed", Noon.AddMinutes(-20), body: "{\"amount\":1}");
        WriteRun(Reports, "9", "Passed", Noon, body: "{\"amount\":2}");

        var (output, error, exit) = Query("diff", Reports, "--baseline-run", "gh_7_1", "--body", "s0/i0");

        Assert.True(exit == 0, error);
        Assert.Contains("- " + Path.Combine("runs", "gh_7_1", "TestRunReport.json") + " s0/i0", output);
        Assert.Contains("+ TestRunReport.json s0/i0", output);
        Assert.Contains("$.amount: 1 → 2", output);
    }

    [Fact]
    public void Diff_baseline_run_refuses_a_run_that_is_not_kept_the_run_on_top_and_a_second_old_side()
    {
        TheIssue();

        var notKept = Query("diff", Reports, "--baseline-run", "gh:5:1");
        var onTop = Query("diff", Reports, "--baseline-run", "gh:9:1");
        var twoFlags = Query("diff", Reports, "--baseline", "--baseline-run", "previous");
        var twoReports = Query("diff", Reports, Path.Combine(Reports, "runs", "gh_8_1", "TestRunReport.json"), "--baseline-run", "previous");

        Assert.Equal(2, notKept.Exit);
        Assert.Contains("run gh:5:1 is not retained under", notKept.Error);
        Assert.Contains("history --run gh:5:1", notKept.Error);
        Assert.Equal(2, onTop.Exit);
        Assert.Contains("gh:9:1 is the run the report already is", onTop.Error);
        Assert.Equal(2, twoFlags.Exit);
        Assert.Contains("one of --baseline and --baseline-run", twoFlags.Error);
        Assert.Equal(2, twoReports.Exit);
        Assert.Contains("--baseline-run names the old side itself", twoReports.Error);
    }

    [Fact]
    public void Diff_with_nothing_to_compare_against_names_baseline_run_as_a_way()
    {
        TheIssue();

        var (_, error, exit) = Query("diff", Reports);

        Assert.Equal(2, exit);
        Assert.Contains("--baseline-run last-failed", error);
    }
}
