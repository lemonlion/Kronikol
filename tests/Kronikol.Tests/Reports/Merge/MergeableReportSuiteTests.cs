using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// The suite has to survive a merge, because a merge <b>recomputes</b> every <c>stableId</c> rather than
/// copying it.
///
/// <para>Without this, a merged report written from shards that each resolved a suite carried the
/// unscoped, pre-3.1.0 ids - so its ids matched neither its own shards nor a baseline promoted from it,
/// and the documented workflow (promote the merged file to <c>baseline/</c>, then
/// <c>kronikol query diff --baseline</c>) paired nothing at all: every scenario read as gone and new.</para>
/// </summary>
public class MergeableReportSuiteTests
{
    private static string Shard(string? suite, string scenarioId, string scenarioName) =>
        ReportGenerator.GenerateMergeableReportJson(
            [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = scenarioId, DisplayName = scenarioName, Result = ExecutionResult.Passed }] }],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            diagramLookup: null,
            componentRelationships: null,
            internalFlowSegmentData: null,
            wholeTestFlow: null,
            WholeTestFlowVisualization.None,
            ciMetadata: null,
            suite: suite);

    [Fact]
    public void A_shard_carries_its_suite_through_read_and_merge()
    {
        var merged = MergeableReportMerger.Merge(
        [
            MergeableReportReader.Parse(Shard("Alpha.Tests", "a1", "Place order")),
            MergeableReportReader.Parse(Shard("Alpha.Tests", "a2", "Cancel order"))
        ]);

        Assert.Equal("Alpha.Tests", merged.Suite);

        // And the ids the merged file writes are the ones the shards wrote, which is the property that
        // makes a merged report a valid baseline for the runs it was merged from.
        var reread = MergeableReportReader.Parse(MergeableReportRenderer.Serialize(merged));
        Assert.Equal("Alpha.Tests", reread.Suite);
        Assert.Contains(ScenarioStableId.Compute("Alpha.Tests", "Orders", "Place order"),
            MergeableReportRenderer.Serialize(merged));
    }

    [Fact]
    public void Shards_from_different_suites_merge_to_no_suite_rather_than_to_one_of_them()
    {
        // There is no single right answer here, and picking one would re-key every scenario in the merged
        // file to a suite half of them never belonged to. Null is honest, and it is also the only id
        // scheme the two suites ever shared.
        var merged = MergeableReportMerger.Merge(
        [
            MergeableReportReader.Parse(Shard("Alpha.Tests", "a1", "Place order")),
            MergeableReportReader.Parse(Shard("Beta.Tests", "b1", "Cancel order"))
        ]);

        Assert.Null(merged.Suite);
    }

    [Fact]
    public void A_shard_written_before_suites_existed_reads_back_as_no_suite()
    {
        var merged = MergeableReportMerger.Merge([MergeableReportReader.Parse(Shard(null, "a1", "Place order"))]);

        Assert.Null(merged.Suite);
        Assert.Contains(ScenarioStableId.Compute(null, "Orders", "Place order"),
            MergeableReportRenderer.Serialize(merged));
    }
}
