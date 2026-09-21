using Kronikol.History;
using Kronikol.Reports;
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

    private static string Report(string pay, string runId, bool attachment = false) => $$"""
        {
          "kronikolVersion": "3.9.0", "formatVersion": 1, "suite": "Suite",
          "startTime": "2026-09-12T10:00:00Z", "endTime": "2026-09-12T10:05:00Z",
          "ciMetadata": { "provider": "GitHubActions", "buildNumber": "42", "branch": "main", "commitSha": "abc1234", "pipelineUrl": null, "repository": "o/r", "runId": "{{runId}}", "runAttempt": "1" },
          "features": [ { "name": "Checkout", "labels": [], "scenarios": [
            { "id": "t0", "stableId": "{{PayId}}", "name": "Pay by card", "result": "{{pay}}", "durationSeconds": 0.1, "errorMessage": {{(pay == "Failed" ? "\"Expected 200 but got 500\"" : "null")}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [],
              "attachments": [ {{(attachment ? "{ \"name\": \"checkout.png\", \"relativePath\": \"attachments/checkout.png\", \"mediaType\": \"image/png\" }" : "")}} ] },
            { "id": "t1", "stableId": "{{RefundId}}", "name": "Refund an order", "result": "Passed", "durationSeconds": 0.05, "labels": [], "categories": [], "steps": [], "httpInteractions": [] } ] } ]
        }
        """;

    /// <summary>One run's files in <paramref name="directory"/>: the report, its manifest, and - for a run that kept them - its fragment and an attachment.</summary>
    private static void WriteRun(string directory, string runId, string pay, DateTimeOffset at, bool withFragmentAndAttachment = false)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "TestRunReport.json"), Report(pay, runId, withFragmentAndAttachment));
        File.WriteAllText(Path.Combine(directory, "TestRunReport.html"), "<html></html>");
        var files = new List<string> { "TestRunReport.json", "TestRunReport.html" };
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
}
