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

    /// <summary>
    /// The merged HTML and the merged data file are the two halves of one answer, and a <c>#sid-</c> link
    /// crosses between them. They are written by different methods from the same model, and one of them
    /// treats a null suite as "the caller did not say" while the other treats it as "there is none" — so
    /// on the case a merge deliberately produces (shards that disagree, and every shard written before
    /// suites existed) the HTML scoped its ids to whatever process ran the merge while the JSON left them
    /// unscoped, and every link from the data file missed.
    ///
    /// <para>Asserted as an equality between the two artifacts rather than against a recomputed id,
    /// because both halves stay internally consistent while drifting apart: each is individually
    /// plausible and only the pairing is wrong.</para>
    /// </summary>
    [Theory]
    [InlineData("Alpha.Tests", "Alpha.Tests")]   // every shard agrees — the suite survives
    [InlineData("Alpha.Tests", "Beta.Tests")]    // they disagree — the merge keeps no suite at all
    [InlineData(null, null)]                     // shards written before suites existed
    public void A_merged_report_carries_one_set_of_ids_across_both_files(string? first, string? second)
    {
        var merged = MergeableReportMerger.Merge(
        [
            MergeableReportReader.Parse(Shard(first, "a1", "Place order")),
            MergeableReportReader.Parse(Shard(second, "b1", "Cancel order"))
        ]);

        var directory = Directory.CreateTempSubdirectory("kronikol-merge-ids").FullName;
        try
        {
            var html = File.ReadAllText(MergeableReportRenderer.Render(merged, Path.Combine(directory, "Merged.html")));
            var json = MergeableReportRenderer.Serialize(merged);

            var inHtml = System.Text.RegularExpressions.Regex.Matches(html, "data-stable-id=\"([0-9a-f]{16})\"")
                .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var inJson = System.Text.RegularExpressions.Regex.Matches(json, "\"stableId\": \"([0-9a-f]{16})\"")
                .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();

            Assert.NotEmpty(inJson);
            Assert.Equal(inJson, inHtml);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
