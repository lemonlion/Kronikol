using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports.Merge;

/// <summary>
/// `merge` reads a shard's declared format version and refuses one it does not understand.
///
/// <para>It used to bind the key with <c>out _</c> — testing presence and discarding the value — and then
/// write its own constants into the merged output. An unknown version was accepted silently and
/// re-stamped as 1, so a file this build could not read became one that claimed it could, and every
/// reader downstream of the merge inherited the claim. The gate was added in 3.1.0; what it never had is
/// a test, which is what let it be written that way in the first place.</para>
///
/// <para>The re-stamping mechanism is unchanged and deliberately so — <c>MergeableReport</c> carries no
/// format version, because the merged file's shape is this build's, not the shard's. That makes the
/// input gate the only thing standing between a skewed shard and a laundered output, which is the reason
/// these tests exist rather than a reason to remove them.</para>
/// </summary>
public class MergeVersionGateTests
{
    private const string Body = """
          "kronikolVersion": "3.3.0",
          "startTime": "2026-01-01T10:00:00Z",
          "endTime": "2026-01-01T10:05:00Z",
          "features": [ { "name": "Orders", "scenarios": [
            { "id": "t0", "name": "Place an order", "result": "Passed" } ] } ]
        """;

    [Fact]
    public void Merge_refuses_an_unknown_mergeableFormatVersion()
    {
        var shard = $$"""{ "formatVersion": 1, "mergeableFormatVersion": 99, {{Body}} }""";

        var thrown = Assert.Throws<FormatException>(() => MergeableReportReader.Parse(shard));

        Assert.Contains("99", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_refuses_a_mergeableFormatVersion_that_is_not_a_number()
    {
        // The shape that used to read identically to "absent": a string, a float or a null all landed as
        // null once the value was discarded, and the gate one layer up then had nothing to refuse.
        var shard = $$"""{ "formatVersion": 1, "mergeableFormatVersion": "one", {{Body}} }""";

        Assert.Throws<FormatException>(() => MergeableReportReader.Parse(shard));
    }

    [Fact]
    public void Merge_refuses_an_unknown_formatVersion()
    {
        var shard = $$"""{ "formatVersion": 99, "mergeableFormatVersion": 1, {{Body}} }""";

        var thrown = Assert.Throws<FormatException>(() => MergeableReportReader.Parse(shard));

        Assert.Contains("99", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_reads_a_shard_this_build_understands()
    {
        var shard = $$"""
            { "formatVersion": {{Kronikol.Reports.ReportGenerator.ReportFormatVersion}},
              "mergeableFormatVersion": {{MergeableReportReader.MergeableFormatVersion}}, {{Body}} }
            """;

        var report = MergeableReportReader.Parse(shard);

        Assert.Single(report.Features);
    }

    /// <summary>
    /// The tool and the library have to agree on the number, and until this was public they each held
    /// their own literal — two copies of one contract, in two assemblies, with nothing comparing them.
    /// </summary>
    [Fact]
    public void The_mergeable_version_the_writer_stamps_is_the_one_the_reader_accepts()
    {
        var written = Kronikol.Reports.ReportGenerator.GenerateMergeableReportJson(
            [], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            Array.Empty<Kronikol.DefaultDiagramsFetcher.DiagramAsCode>().ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            componentRelationships: [], internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null);

        using var document = System.Text.Json.JsonDocument.Parse(written);

        Assert.Equal(MergeableReportReader.MergeableFormatVersion,
            document.RootElement.GetProperty("mergeableFormatVersion").GetInt32());
    }
}
