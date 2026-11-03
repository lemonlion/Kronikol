using System.Net;
using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// A shard that can see the ledger holds a part of the suite, so it reads itself as partial, and the fold made the whole run
/// partial if any shard said so. The history action's flow does this, as it runs <c>read</c> in each test job before the tests.
/// So a sharded run's line was partial, nothing was then reported absent, and a merged run, which is not partial, read past that
/// line: it compared with an older run and raised a false <c>behaviour-changed</c>, or stayed silent on a real change
/// (plans/CALLS_AND_EVENTS_PLAN.md F20). Whether a whole run is partial is judged when its line is appended, as
/// <c>HistoryLedgerWriter</c> and the wiki have always said.
/// </summary>
[Collection("DiagramsFetcher")]
public class ShardPartialTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] AllScenarios = ["a1", "a2", "b1", "b2"];

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-shard-partial").FullName;

    private string Ledger => Path.Combine(_dir, ".kronikol", "history.jsonl");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static Feature[] Features(IEnumerable<string> ids) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = ids.Select(id => new Scenario { Id = id, DisplayName = "Scenario " + id, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }).ToArray()
        }
    ];

    private static RequestResponseLog[] Calls(IEnumerable<string> ids, DateTimeOffset at, string label) => ids.SelectMany(id =>
    {
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://svc/orders");
        return new RequestResponseLog[]
        {
            new("Scenario " + id, id, label, null, uri, [], "Svc", "Caller", RequestResponseType.Request, trace, pair, false) { Timestamp = at },
            new("Scenario " + id, id, label, null, uri, [], "Svc", "Caller", RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK) { Timestamp = at.AddMilliseconds(5) }
        };
    }).ToArray();

    private static CiMetadata Ci(int run, string branch = "main") => new(CiEnvironment.GitHubActions, run.ToString(System.Globalization.CultureInfo.InvariantCulture), branch, "abc1234", null, "o/r", run.ToString(System.Globalization.CultureInfo.InvariantCulture), "1");

    /// <summary>One shard of run <paramref name="run"/>: the fragment and data file it writes, having read the ledger as a test job does.</summary>
    private (string Fragment, string Data, HistoryVerdicts? Verdicts) Shard(int run, string[] ids, string label, bool? hostPartial = null, string branch = "main", double? threshold = null)
    {
        var at = T0.AddHours(run);
        var logs = Calls(ids, at, label);
        var options = new ReportConfigurationOptions { HistoryFilePath = Ledger, HistoryBranch = branch, HistoryPartialRun = hostPartial };
        if (threshold is { } set)
            options.HistoryPartialThreshold = set;
        var context = HistoryRunContext.Create(Features(ids), logs, null, Ci(run, branch), at, options, _dir, "5.27.13", _ => null, _dir);
        Assert.NotNull(context);
        var start = at.UtcDateTime;
        var data = ReportGenerator.GenerateMergeableReportJson(Features(ids), start, start.AddMinutes(1), null, null, null, null,
            WholeTestFlowVisualization.None, Ci(run), trackedLogs: logs);
        return (context.Fragment(), data, context.Verdicts);
    }

    /// <summary>A whole run of two shards in CI's order: each reads the ledger and writes, the merge reads, and only then is the run recorded.</summary>
    private string RunThrough(int run, string label, string[]? scenarios = null, bool? hostPartial = null, bool? secondHostPartial = null, double? threshold = null)
    {
        var folder = Path.Combine(_dir, "run" + run);
        var ids = scenarios ?? AllScenarios;
        var half = ids.Length / 2;
        var (a, b) = (Shard(run, ids[..half], label, hostPartial, threshold: threshold), Shard(run, ids[half..], label, secondHostPartial ?? hostPartial, threshold: threshold));
        foreach (var (name, shard) in new[] { ("a", a), ("b", b) })
        {
            var at = Path.Combine(folder, name);
            Directory.CreateDirectory(at);
            File.WriteAllText(Path.Combine(at, HistoryFormat.FragmentFileName), shard.Fragment);
            File.WriteAllText(Path.Combine(at, "report.json"), shard.Data);
        }

        var merged = "no ledger yet";
        var (output, error) = (new StringWriter(), new StringWriter());
        if (File.Exists(Ledger))
        {
            var exit = MergeCommand.Run([folder, "-o", Path.Combine(folder, "out", "Combined.html"), "--history", Ledger], output, error, _ => null);
            Assert.True(exit == 0, error.ToString());
            merged = output.ToString();
        }

        error = new StringWriter();
        Assert.True(HistoryCommand.Run(["record", folder, "--history", Ledger], new StringWriter(), error, _ => null) == 0, error.ToString());
        return merged;
    }

    private bool?[] PartialOfEachLine() => File.ReadLines(Ledger)
        .Select(line => JsonDocument.Parse(line).RootElement)
        .Where(line => line.TryGetProperty("results", out _))
        .Select(line => line.TryGetProperty("partial", out var partial) ? (bool?)partial.GetBoolean() : null)
        .ToArray();

    /// <summary>One test job's run, recorded as the action records it, with the ledger in sight.</summary>
    private void Recorded(int run, string[] ids, string branch, double? threshold = null)
    {
        var folder = Path.Combine(_dir, "job" + run);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, HistoryFormat.FragmentFileName), Shard(run, ids, "Delete", branch: branch, threshold: threshold).Fragment);
        var error = new StringWriter();
        Assert.True(HistoryCommand.Run(["record", folder, "--history", Ledger], new StringWriter(), error, _ => null) == 0, error.ToString());
    }

    private static string[] Numbered(int count) => Enumerable.Range(1, count).Select(n => "s" + n).ToArray();

    /// <summary>The action's own workflow records a push of every branch: a branch with two more scenarios says nothing about main's next run.</summary>
    [Fact]
    public void A_line_is_read_against_its_own_branch()
    {
        Recorded(1, Numbered(10), "main");
        Recorded(2, Numbered(12), "feature/x");
        Recorded(3, Numbered(10), "main");
        Recorded(4, Numbered(10), "main");

        Assert.Equal([false, false, false, false], PartialOfEachLine());
    }

    [Fact]
    public void A_branchs_first_line_is_not_partial_for_lacking_what_another_branch_has()
    {
        Recorded(1, Numbered(10), "main");
        Recorded(2, Numbered(2), "feature/x");

        Assert.Equal([false, false], PartialOfEachLine());
    }

    [Fact]
    public void A_branch_that_lost_most_of_its_own_scenarios_is_partial()
    {
        Recorded(1, Numbered(10), "main");
        Recorded(2, Numbered(10), "feature/x");
        Recorded(3, Numbered(2), "feature/x");

        Assert.Equal([false, false, true], PartialOfEachLine());
    }

    /// <summary><c>HistoryPartialRun</c> set to false on every shard is heard through the fold, as a shard's own reading no longer reaches it.</summary>
    [Fact]
    public void A_host_that_says_no_run_is_partial_is_heard_through_the_fold()
    {
        RunThrough(1, "Delete");
        RunThrough(2, "Delete", ["a1", "b1"], hostPartial: false);

        Assert.Equal([false, false], PartialOfEachLine());
    }

    [Fact]
    public void A_shard_that_says_no_beside_one_that_says_nothing_leaves_it_to_the_ledger()
    {
        RunThrough(1, "Delete");
        RunThrough(2, "Delete", ["a1", "b1"], hostPartial: null, secondHostPartial: false);

        Assert.Equal([false, true], PartialOfEachLine());
    }

    /// <summary>
    /// What a sweep finds under a folder Kronikol keeps other things in is no run of this one: a fragment attached to a report, or last-green's.
    /// <c>runs/</c> is not among them, as a retry's earlier attempts are kept there (HistoryCommandTests.Record_reads_a_retained_attempt...).
    /// </summary>
    [Theory]
    [InlineData("attachments")]
    [InlineData("baseline")]
    public void Recording_a_folder_leaves_what_a_reserved_folder_holds(string reserved)
    {
        var folder = Path.Combine(_dir, "job");
        Directory.CreateDirectory(Path.Combine(folder, reserved));
        File.WriteAllText(Path.Combine(folder, HistoryFormat.FragmentFileName), Shard(2, AllScenarios, "Delete").Fragment);
        File.WriteAllText(Path.Combine(folder, reserved, HistoryFormat.FragmentFileName), Shard(1, AllScenarios, "Delete").Fragment);

        var error = new StringWriter();
        Assert.True(HistoryCommand.Run(["record", folder, "--history", Ledger], new StringWriter(), error, _ => null) == 0, error.ToString());

        Assert.Single(PartialOfEachLine());
    }

    /// <summary><c>HistoryPartialThreshold</c> reaches the line, as the fragment no longer carries the reading made under it.</summary>
    [Theory]
    [InlineData(0.5, 7, false)]
    [InlineData(0.02, 19, true)]
    [InlineData(null, 8, true)]
    public void The_line_is_judged_under_the_threshold_the_run_set(double? threshold, int kept, bool partial)
    {
        var all = Numbered(kept is 19 ? 20 : 10);
        Recorded(1, all, "main", threshold);
        Recorded(2, all[..kept], "main", threshold);

        Assert.Equal([false, partial], PartialOfEachLine());
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(null, true)]
    public void Two_shards_are_judged_under_the_threshold_they_set(double? threshold, bool partial)
    {
        RunThrough(1, "Delete");
        RunThrough(2, "Delete", ["a1", "a2", "b1"], threshold: threshold);

        Assert.Equal([false, partial], PartialOfEachLine());
    }

    /// <summary><c>history gate</c> and <c>query history</c> read a fragment through the analyzer, so a run's threshold holds there too.</summary>
    [Theory]
    [InlineData(0.5, false)]
    [InlineData(null, true)]
    public void The_analyzer_reads_a_fragment_under_the_threshold_it_carries(double? threshold, bool partial)
    {
        var all = Numbered(10);
        Recorded(1, all, "main");
        var fragment = HistoryFragment.Parse(Shard(2, all[..7], "Delete", threshold: threshold).Fragment);

        var verdicts = HistoryAnalyzer.Analyse(HistoryLedgerReader.Read(Ledger, 50).Ledger!, fragment.Roster, fragment.Run, new HistoryAnalysisOptions());

        Assert.Equal(partial, verdicts.Partial);
    }

    [Fact]
    public void A_threshold_at_its_default_changes_no_fragment()
    {
        Assert.DoesNotContain("artialThreshold", Shard(1, Numbered(2), "Delete").Fragment);
    }

    [Fact]
    public void A_shard_that_reads_itself_as_partial_leaves_the_question_to_the_fold()
    {
        for (var run = 1; run <= 3; run++)
            RunThrough(run, "Delete");

        var shard = Shard(4, ["a1", "a2"], "Delete");

        Assert.True(shard.Verdicts!.Partial, "the shard's own reading is what this fact is about");
        Assert.Null(HistoryFragment.Parse(shard.Fragment).Run.Partial);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void What_the_host_said_stays_in_the_fragment(bool hostPartial)
    {
        var shard = Shard(1, ["a1", "a2"], "Delete", hostPartial);

        Assert.Equal(hostPartial, HistoryFragment.Parse(shard.Fragment).Run.Partial);
    }

    /// <summary>The fold keeps a <c>true</c> a host really said.</summary>
    [Fact]
    public void A_host_that_says_a_shard_is_partial_makes_the_run_partial()
    {
        var folder = Path.Combine(_dir, "said");
        Directory.CreateDirectory(Path.Combine(folder, "a"));
        Directory.CreateDirectory(Path.Combine(folder, "b"));
        File.WriteAllText(Path.Combine(folder, "a", HistoryFormat.FragmentFileName), Shard(1, ["a1", "a2"], "Delete", hostPartial: true).Fragment);
        File.WriteAllText(Path.Combine(folder, "b", HistoryFormat.FragmentFileName), Shard(1, ["b1", "b2"], "Delete").Fragment);

        var error = new StringWriter();
        Assert.True(HistoryCommand.Run(["record", folder, "--history", Ledger], new StringWriter(), error, _ => null) == 0, error.ToString());

        Assert.True(PartialOfEachLine().Single());
    }

    [Fact]
    public void The_shards_of_a_whole_run_fold_into_a_line_that_is_not_partial()
    {
        for (var run = 1; run <= 4; run++)
            RunThrough(run, "Delete");

        Assert.All(PartialOfEachLine(), partial => Assert.NotEqual(true, partial));
    }

    /// <summary>A run that really lost a shard is still judged partial, when its line is appended.</summary>
    [Fact]
    public void A_run_that_lost_a_shard_is_still_partial_when_it_is_appended()
    {
        for (var run = 1; run <= 3; run++)
            RunThrough(run, "Delete");
        var folder = Path.Combine(_dir, "lost");
        Directory.CreateDirectory(Path.Combine(folder, "a"));
        File.WriteAllText(Path.Combine(folder, "a", HistoryFormat.FragmentFileName), Shard(4, ["a1"], "Delete").Fragment);

        var error = new StringWriter();
        Assert.True(HistoryCommand.Run(["record", folder, "--history", Ledger], new StringWriter(), error, _ => null) == 0, error.ToString());

        Assert.True(PartialOfEachLine().Last());
    }

    /// <summary>The plan's C1: a real change in run four, none in run five. Run five reads against run four, not past it.</summary>
    [Fact]
    public void A_merge_compares_with_the_run_before_it_and_raises_no_false_change()
    {
        for (var run = 1; run <= 3; run++)
            RunThrough(run, "Delete");
        RunThrough(4, "Patch");

        var fifth = RunThrough(5, "Patch");

        Assert.True(!fifth.Contains("behaviour changed", StringComparison.Ordinal), fifth);
    }

    /// <summary>
    /// The plan's C2: run four changes the calls and run five changes them back. With run four read past, run five read as nothing
    /// at all. It is a return to a set the scenario held, which the analyzer calls alternating.
    /// </summary>
    [Fact]
    public void A_merge_reads_a_change_and_back_as_alternating_where_it_read_nothing()
    {
        for (var run = 1; run <= 3; run++)
            RunThrough(run, "Delete");
        RunThrough(4, "Patch");

        RunThrough(5, "Delete");

        var page = ReportHtml.Read(Path.Combine(_dir, "run5", "out", "Combined.labs.html"));
        Assert.Contains("<span class=\"history-verdict history-verdict-alternating\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scenario gone since the run before it is read against that run. With each sharded line partial, a merge read back to the
    /// last line that was not, which had never held the scenario, so it was reported absent from nothing.
    /// </summary>
    [Fact]
    public void A_merge_reports_a_scenario_gone_since_the_run_before_it()
    {
        var everyone = Enumerable.Range(1, 20).Select(n => "s" + n).ToArray();
        string[] withLater = [.. everyone, "later"];
        RunThrough(1, "Delete", everyone);
        RunThrough(2, "Delete", withLater);
        RunThrough(3, "Delete", withLater);

        var fourth = RunThrough(4, "Delete", everyone);

        Assert.True(fourth.Contains("1 absent", StringComparison.Ordinal), fourth);
    }

    /// <summary>A change run five really makes, against run four, is named as one.</summary>
    [Fact]
    public void A_merge_names_a_change_against_the_run_before_it()
    {
        for (var run = 1; run <= 3; run++)
            RunThrough(run, "Delete");
        RunThrough(4, "Patch");

        var fifth = RunThrough(5, "Refund");

        Assert.True(fifth.Contains("4 behaviour changed", StringComparison.Ordinal), fifth);
        var page = ReportHtml.Read(Path.Combine(_dir, "run5", "out", "Combined.labs.html"));
        Assert.Contains("the same number of calls as gh:4:1", page, StringComparison.Ordinal);
    }
}
