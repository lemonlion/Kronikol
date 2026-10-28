using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// An edit of the consumer's <c>HistoryShapeTemplates</c> (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8). A fingerprint is
/// comparable only with one the same rules made, so until #192 R1 an edit cost every scenario one quiet run and restarted
/// its memory. A run line now records its patterns beside their hash, and a run reads its ledger before it is built, so
/// it fingerprints its own calls under the window's earlier patterns as well, and is compared through that.
/// </summary>
public class HistoryPatternBridgeTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-pattern-bridge").FullName;
    private string Ledger => Path.Combine(_dir, "history.jsonl");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Cache",
            Scenarios = [new Scenario { Id = "t1", DisplayName = "Read the menu", Result = ExecutionResult.Passed }]
        }
    ];

    /// <summary>The scenario reads a cache key holding a per-run part only the consumer's pattern folds.</summary>
    private static RequestResponseLog[] Calls(int run, string path = "menu")
    {
        var pair = Guid.NewGuid();
        var uri = new Uri($"http://cache/{path}/key-tenant{run}x");
        return
        [
            new RequestResponseLog("t", "t1", HttpMethod.Get, null, uri, [], "Cache", "Caller", RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false),
            new RequestResponseLog("t", "t1", HttpMethod.Get, null, uri, [], "Cache", "Caller", RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: HttpStatusCode.OK)
        ];
    }

    private HistoryRunContext Record(int n, HistoryShapeTemplate[] templates, string path = "menu")
    {
        var options = new ReportConfigurationOptions { HistoryFilePath = Ledger, HistoryBranch = "", HistoryMinRuns = 2 };
        foreach (var template in templates)
            options.HistoryShapeTemplates.Add(template);
        var context = HistoryRunContext.Create(Features(), Calls(n, path), "Suite", null, At.AddHours(n), options, _dir, "5.17.0", _ => null, _dir)!;
        Assert.Equal(HistoryAppendOutcome.Appended, context.Append()!.Outcome);
        return context;
    }

    /// <summary>A run on <paramref name="branch"/>'s stream, read with a window of <paramref name="window"/> runs.</summary>
    private HistoryRunContext Record(int n, HistoryShapeTemplate[] templates, string branch, int window)
    {
        var options = new ReportConfigurationOptions { HistoryFilePath = Ledger, HistoryBranch = "", HistoryMinRuns = 2, HistoryWindow = window };
        foreach (var template in templates)
            options.HistoryShapeTemplates.Add(template);
        var id = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ci = new CiMetadata(CiEnvironment.GitHubActions, id, branch, "abc" + id, null, "o/r", id, "1");
        var context = HistoryRunContext.Create(Features(), Calls(n), "Suite", ci, At.AddHours(n), options, _dir, "5.17.0", _ => null, _dir)!;
        Assert.Equal(HistoryAppendOutcome.Appended, context.Append()!.Outcome);
        return context;
    }

    private static ScenarioHistory Scenario(HistoryRunContext context) => Assert.Single(context.Verdicts!.Scenarios);

    [Fact]
    public void An_edit_is_bridged_for_a_stream_the_suites_last_lines_crowd_out()
    {
        // Main's runs under the narrow pattern, then more of a branch's runs under the wide one than the window holds: the
        // suite's last lines are all the branch's, but main's next run is read against main's own window (#95), and the
        // patterns it bridges are that window's.
        for (var n = 1; n <= 3; n++)
            Record(n, [Narrow], "main", 3);
        for (var n = 4; n <= 7; n++)
            Record(n, [Wide], "feature/menu", 3);

        var edited = Scenario(Record(8, [Wide], "main", 3));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, edited.Verdicts);
        Assert.DoesNotContain("earlier rule", edited.Evidence, StringComparison.Ordinal);
        Assert.NotNull(edited.PreviousShapeSet);
    }

    // The per-run part is "tenant{n}x": the first pattern folds it, the edit folds it more widely, and a third does not.
    private static readonly HistoryShapeTemplate Narrow = new(@"tenant\d+x", "{tenant}");
    private static readonly HistoryShapeTemplate Wide = new(@"key-tenant\d+x", "key-{tenant}");

    [Fact]
    public void Editing_a_consumer_rule_keeps_every_verdict()
    {
        for (var n = 1; n <= 3; n++)
            Record(n, [Narrow]);

        var edited = Scenario(Record(4, [Wide]));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, edited.Verdicts);
        Assert.DoesNotContain("earlier rule", edited.Evidence, StringComparison.Ordinal);
        Assert.NotNull(edited.PreviousShapeSet);
    }

    [Fact]
    public void Removing_a_consumer_rule_keeps_every_verdict()
    {
        for (var n = 1; n <= 3; n++)
            Record(n, [Narrow]);

        // Without its rule the per-run part splits the call on every run from now on: the first run says nothing it
        // cannot stand behind, through the bridge, and the next reads the difference as the rule's absence would.
        var removed = Scenario(Record(4, []));

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, removed.Verdicts);
        Assert.DoesNotContain("earlier rule", removed.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_change_across_an_edit_still_reads_as_one()
    {
        for (var n = 1; n <= 3; n++)
            Record(n, [Narrow]);

        var changed = Scenario(Record(4, [Wide], path: "orders"));

        Assert.Contains(HistoryVerdictKind.BehaviourChanged, changed.Verdicts);
    }
}
