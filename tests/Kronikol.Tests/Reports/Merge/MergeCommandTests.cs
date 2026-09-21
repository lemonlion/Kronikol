using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Tool;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

public class MergeCommandTests
{
    private static string WriteMergeableJson(string dir, string name, Feature[] features, ComponentRelationship[] rels, DateTime start, DateTime end)
    {
        var json = ReportGenerator.GenerateMergeableReportJson(
            features, start, end,
            features.SelectMany(f => f.Scenarios)
                .Select(s => new DiagramAsCode(s.Id, "", $"@startuml\n{s.Id}->X\n@enduml"))
                .ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            rels, internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Merge_command_combines_directory_of_reports_into_html()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WriteMergeableJson(dir, "runner1.json",
                [ new Feature { DisplayName = "Orders", Scenarios = [ new Scenario { Id = "r1s1", DisplayName = "Place order", Result = ExecutionResult.Passed } ] } ],
                [ new ComponentRelationship("Test", "OrdersApi", "HTTP", new HashSet<string> { "POST /orders" }, 1, 1, "http") ],
                new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 2, 0, DateTimeKind.Utc));

            WriteMergeableJson(dir, "runner2.json",
                [ new Feature { DisplayName = "Inventory", Scenarios = [ new Scenario { Id = "r2s1", DisplayName = "Adjust stock", Result = ExecutionResult.Failed, ErrorMessage = "oops" } ] } ],
                [ new ComponentRelationship("OrdersApi", "InventoryDb", "SQL", new HashSet<string> { "SELECT" }, 3, 1, "sql") ],
                new DateTime(2026, 1, 1, 9, 55, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc));

            // A schema file in the same directory must be ignored.
            File.WriteAllText(Path.Combine(dir, "TestRunReport.schema.json"), "{}");

            var output = Path.Combine(dir, "Combined.html");
            var outWriter = new StringWriter();
            var errWriter = new StringWriter();

            var exit = MergeCommand.Run([dir, "-o", output, "-t", "Combined CLI"], outWriter, errWriter);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output), "Combined HTML should be written. Stderr: " + errWriter);
            var html = File.ReadAllText(output);
            Assert.Contains("Combined CLI", html);
            Assert.Contains("Orders", html);
            Assert.Contains("Inventory", html);
            Assert.Contains("Place order", html);
            Assert.Contains("Adjust stock", html);
            // Both runners' reports were picked up (schema file excluded).
            Assert.Contains("runner1.json", outWriter.ToString());
            Assert.Contains("runner2.json", outWriter.ToString());
            Assert.DoesNotContain("schema.json", outWriter.ToString());

            // The REAL merge command's output carries a rebuilt deep-search index over the
            // merged corpus (not just the GenerateHtmlReport-parameter proxy the §9.2 unit
            // tests exercise): each runner's diagram text maps to its own doc.
            var blob = SearchIndexReportTests.ExtractBlobBase64(html);
            Assert.NotNull(blob);
            var decoded = SearchIndexReportTests.DecodeBlob(blob!);
            Assert.Equal(2, decoded.DocAnchors.Length);
            var placeOrderDoc = Array.IndexOf(decoded.DocAnchors, "scenario-place-order");
            Assert.True(placeOrderDoc >= 0, "doc table: " + string.Join(", ", decoded.DocAnchors));
            Assert.Contains(placeOrderDoc, decoded.Candidates("r1s1"));
            Assert.DoesNotContain(1 - placeOrderDoc, decoded.Candidates("r1s1"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Merge_command_errors_when_no_inputs()
    {
        var exit = MergeCommand.Run([], new StringWriter(), new StringWriter());
        Assert.Equal(2, exit);
    }

    [Fact]
    public void Merge_command_errors_on_non_mergeable_input()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A standard (non-mergeable) report should be rejected with a clear error.
            var standardPath = Path.Combine(dir, "standard.json");
            var written = ReportGenerator.GenerateTestRunReportData(
                [ new Feature { DisplayName = "F", Scenarios = [ new Scenario { Id = "x", DisplayName = "s", Result = ExecutionResult.Passed } ] } ],
                new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc),
                "standard.json", DataFormat.Json);
            File.Copy(written, standardPath, overwrite: true);

            var err = new StringWriter();
            var exit = MergeCommand.Run([standardPath, "-o", Path.Combine(dir, "out.html")], new StringWriter(), err);

            Assert.Equal(1, exit);
            Assert.Contains("GenerateMergeableData", err.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Every history-enabled run has written History.run.json beside its report since 3.9.0, and
    // ctrf-report.json is there whenever CTRF is on. Neither is a report, and the reader fails the whole
    // merge on the first one - so `kronikol merge <reports-dir>` was exit 1 for any such directory.

    private static (string Dir, Feature[] Features, DateTime Start, DateTime End) AReportsDirectoryWithWhatARunWritesBesideIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Feature[] features = [ new Feature { DisplayName = "Orders", Scenarios = [ new Scenario { Id = "r1s1", DisplayName = "Place order", Result = ExecutionResult.Passed } ] } ];
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 10, 2, 0, DateTimeKind.Utc);
        WriteMergeableJson(dir, "TestRunReport.json", features, [], start, end);

        // Real ones, written by the code a run writes them with - not stand-ins.
        var roster = Kronikol.History.HistoryRoster.Create("Suite", [ new Kronikol.History.HistoryRosterEntry("id1", "Place order", "Orders", null) ]);
        var run = new Kronikol.History.HistoryRun
        {
            Id = "gh:7:1", Suite = "Suite", Partial = null, At = new DateTimeOffset(end, TimeSpan.Zero), Branch = "main", Commit = null, Provider = null, Url = null, Shards = 1,
            RosterHash = roster.Hash, Results = "P", Attempts = "-", ErrorText = new Dictionary<string, string>()
        };
        File.WriteAllText(Path.Combine(dir, Kronikol.History.HistoryFormat.FragmentFileName), Kronikol.History.HistoryFragment.Write(roster, run, "3.9.0"));
        File.WriteAllText(Path.Combine(dir, CtrfReportGenerator.FileName), CtrfReportGenerator.Generate(features, start, end, null, "3.9.0"));
        return (dir, features, start, end);
    }

    [Fact]
    public void Merge_of_a_reports_directory_skips_the_files_a_run_writes_beside_its_report_and_says_so()
    {
        var (dir, _, _, _) = AReportsDirectoryWithWhatARunWritesBesideIt();
        try
        {
            var output = Path.Combine(dir, "out", "Combined.html");
            var outWriter = new StringWriter();
            var errWriter = new StringWriter();

            var exit = MergeCommand.Run([dir, "-o", output, "--no-json"], outWriter, errWriter);

            Assert.True(exit == 0, errWriter.ToString());
            Assert.Contains("Place order", File.ReadAllText(output));
            Assert.Contains("skipped 2 files Kronikol writes beside a report (History.run.json, ctrf-report.json)", errWriter.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Merge_still_refuses_such_a_file_when_it_is_named_rather_than_found()
    {
        // Found versus given: a sweep skips it, a file named on the command line is read and refused for
        // what it is - skipping "anything without features" would drop a truncated shard without a word.
        var (dir, _, _, _) = AReportsDirectoryWithWhatARunWritesBesideIt();
        try
        {
            var errWriter = new StringWriter();

            var exit = MergeCommand.Run([Path.Combine(dir, Kronikol.History.HistoryFormat.FragmentFileName), "-o", Path.Combine(dir, "out", "Combined.html"), "--no-json"], new StringWriter(), errWriter);

            Assert.Equal(1, exit);
            Assert.Contains(Kronikol.History.HistoryFormat.FragmentFileName, errWriter.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Merge_with_a_ledger_renders_history_and_never_writes_to_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var start = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            var shards = Path.Combine(dir, "shards");
            Directory.CreateDirectory(shards);
            WriteMergeableJson(shards, "runner1.json",
                [ new Feature { DisplayName = "Orders", Scenarios = [ new Scenario { Id = "h1s1", DisplayName = "Place order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(50) } ] } ],
                [], start, start.AddMinutes(2));
            WriteMergeableJson(Path.Combine(dir, "shards"), "runner2.json",
                [ new Feature { DisplayName = "Inventory", Scenarios = [ new Scenario { Id = "h2s1", DisplayName = "Adjust stock", Result = ExecutionResult.Failed, ErrorMessage = "oops", Duration = TimeSpan.FromMilliseconds(80) } ] } ],
                [], start, start.AddMinutes(3));

            // Three earlier runs in which both scenarios passed, over the roster the merged run itself builds.
            var merged = Kronikol.Reports.Merge.MergeableReportRenderer.MergeFiles(Directory.GetFiles(Path.Combine(dir, "shards")));
            var (roster, run) = Kronikol.History.HistoryRunBuilder.Build(merged.Features, [], merged.Suite, null, new DateTimeOffset(start), new Kronikol.History.HistoryBuildOptions());
            var ledger = Path.Combine(dir, ".kronikol", "history.jsonl");
            for (var i = 1; i <= 3; i++)
                Kronikol.History.HistoryLedgerWriter.Append(ledger, roster, run with
                {
                    Id = $"gh:{i}:1", At = new DateTimeOffset(start).AddHours(i - 4), Results = new string('P', roster.Count),
                    Attempts = new string('-', roster.Count), Errors = new string?[roster.Count], ErrorText = new Dictionary<string, string>()
                }, "3.11.0");
            var lengthBefore = new FileInfo(ledger).Length;

            var output = Path.Combine(dir, "Combined.html");
            var outWriter = new StringWriter();
            var errWriter = new StringWriter();
            // The environment is named, because on a pull request build a merge reads against the branch it targets.
            var exit = MergeCommand.Run([Path.Combine(dir, "shards"), "-o", output, "--history", ledger], outWriter, errWriter, _ => null);

            Assert.True(exit == 0, errWriter.ToString());
            var html = File.ReadAllText(output);
            Assert.Contains("<details id=\"history-section\"", html);
            Assert.Contains("data-history-verdicts=\"broke\"", html);
            Assert.Contains("history-sparkline", html);
            var digest = File.ReadAllText(Path.Combine(dir, "Failures.md"));
            Assert.Contains("**History:**", digest);
            Assert.Contains("**broke**", digest);
            Assert.Contains("history: 1 broke", outWriter.ToString());
            Assert.Equal(lengthBefore, new FileInfo(ledger).Length); // a merge is a render, never a record

            var plain = Path.Combine(dir, "Plain.html");
            Assert.Equal(0, MergeCommand.Run([Path.Combine(dir, "shards"), "-o", plain], new StringWriter(), new StringWriter()));
            Assert.DoesNotContain("<details id=\"history-section\"", File.ReadAllText(plain));

            // On a pull request build the merge reads against the branch the pull request targets, as the
            // shards' runs did: a stream with nothing on it, here, so the reading is a cold start on it.
            var pullRequestOut = new StringWriter();
            string? PullRequestEnv(string key) => key switch { "GITHUB_ACTIONS" => "true", "GITHUB_BASE_REF" => "release", _ => null };
            Assert.Equal(0, MergeCommand.Run([Path.Combine(dir, "shards"), "-o", Path.Combine(dir, "PullRequest.html"), "--history", ledger], pullRequestOut, new StringWriter(), PullRequestEnv));
            Assert.Contains("0 runs recorded in the release stream", pullRequestOut.ToString());

            var missing = new StringWriter();
            Assert.Equal(2, MergeCommand.Run([Path.Combine(dir, "shards"), "-o", Path.Combine(dir, "Missing.html"), "--history", Path.Combine(dir, "nowhere.jsonl")], new StringWriter(), missing));
            Assert.Contains("No ledger at", missing.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

