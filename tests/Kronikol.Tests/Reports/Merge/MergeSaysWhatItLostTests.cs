using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// A merge that cannot reconcile something says so, instead of picking one and moving on.
///
/// <para>3.4.x taught the merger to diagnose one disagreement — the run environment — and the pattern
/// stopped there. Two others were silently resolved by taking the first shard's answer, and both are
/// worse than the one that was covered.</para>
///
/// <para>Dropping the <c>suite</c> re-keys <b>every</b> scenario in the merged file:
/// <c>ScenarioStableId</c> folds the suite into the hash, so the ids match neither the shards the
/// report came from nor a baseline promoted from an earlier run, and <c>diff</c> then reports every
/// scenario as both new and gone. Taking the first shard's <c>ciMetadata</c> makes a merge of artifacts
/// from two different builds look like one build.</para>
/// </summary>
public class MergeSaysWhatItLostTests
{
    [Fact]
    public void Shards_from_different_suites_say_that_the_ids_are_no_longer_comparable()
    {
        var merged = MergeableReportMerger.Merge([Shard(suite: "Orders.Tests"), Shard(suite: "Billing.Tests")]);

        var entry = Assert.Single(merged.Diagnostics, d => d.Message.Contains("suites", StringComparison.Ordinal));
        Assert.Contains("stableId", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Orders.Tests", entry.Message, StringComparison.Ordinal);
        Assert.Null(merged.Suite);
    }

    [Fact]
    public void Shards_from_one_suite_say_nothing_and_keep_it()
    {
        var merged = MergeableReportMerger.Merge([Shard(suite: "Orders.Tests"), Shard(suite: "Orders.Tests", id: "s2")]);

        Assert.DoesNotContain(merged.Diagnostics, d => d.Message.Contains("suites", StringComparison.Ordinal));
        Assert.Equal("Orders.Tests", merged.Suite);
    }

    [Fact]
    public void Shards_from_different_builds_say_which_one_was_kept()
    {
        var merged = MergeableReportMerger.Merge(
        [
            Shard(commit: "aaaaaaa", branch: "main"),
            Shard(commit: "bbbbbbb", branch: "main", id: "s2")
        ]);

        var entry = Assert.Single(merged.Diagnostics, d => d.Message.Contains("CI runs", StringComparison.Ordinal));
        Assert.Contains("aaaaaaa", entry.Message, StringComparison.Ordinal);
        Assert.Contains("bbbbbbb", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Shards_from_one_build_say_nothing()
    {
        var merged = MergeableReportMerger.Merge(
        [
            Shard(commit: "aaaaaaa", branch: "main"),
            Shard(commit: "aaaaaaa", branch: "main", id: "s2")
        ]);

        Assert.DoesNotContain(merged.Diagnostics, d => d.Message.Contains("CI runs", StringComparison.Ordinal));
    }

    /// <summary>
    /// The per-scenario side tables and the per-scenario maps disagreed about which shard wins when a
    /// key collides — <c>TryAdd</c> in one and an indexer assignment in the other — so a scenario could
    /// take its verdict and steps from the first shard and its internal flow from the second.
    /// </summary>
    [Fact]
    public void Every_side_table_agrees_about_which_shard_wins()
    {
        var merged = MergeableReportMerger.Merge(
        [
            Shard(flow: "<i>A</i>"),
            Shard(flow: "<i>B</i>")
        ]);

        Assert.Equal("<i>A</i>", Assert.Single(merged.WholeTestFlow).Value.ActivityHtml);
    }

    private static MergeableReport Shard(string? suite = null, string id = "s1", string? commit = null,
        string? branch = null, string? flow = null) => new()
    {
        KronikolVersion = "3.5.1",
        Suite = suite,
        StartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        EndTime = new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc),
        Features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [new Scenario { Id = id, DisplayName = "Place order " + id, Result = ExecutionResult.Passed }]
            }
        ],
        CiMetadata = commit is null ? null : new CiMetadata(CiEnvironment.GitHubActions, null, branch, commit, null, null, null),
        WholeTestFlow = flow is null
            ? new Dictionary<string, WholeTestFlowFragment>()
            : new Dictionary<string, WholeTestFlowFragment> { [id] = new(flow, "", 1) }
    };
}
