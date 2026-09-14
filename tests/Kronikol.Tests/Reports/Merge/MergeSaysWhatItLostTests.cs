using Kronikol.Tracking;
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

    /// <summary>
    /// A shard that does not say how a scenario ended is read as Passed, which is the compatible default
    /// and the right one — but it was applied in silence, so a scenario nobody had a verdict for was
    /// indistinguishable from a real pass. The reachable triggers are a third-party writer, a hand-edited
    /// file, and version skew between the Kronikol that wrote the shard and the one merging it.
    /// </summary>
    [Theory]
    [InlineData("""{ "id": "s1", "name": "Place order" }""")]
    [InlineData("""{ "id": "s1", "name": "Place order", "result": null }""")]
    [InlineData("""{ "id": "s1", "name": "Place order", "result": "Borked" }""")]
    [InlineData("""{ "id": "s1", "name": "Place order", "result": 2 }""")]
    public void A_shard_that_does_not_say_how_a_scenario_ended_says_the_result_was_defaulted(string scenario)
    {
        var report = MergeableReportReader.Parse($$"""
            { "mergeableFormatVersion": 1, "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:01:00Z",
              "features": [ { "name": "Orders", "scenarios": [ {{scenario}} ] } ] }
            """);

        var entry = Assert.Single(report.Diagnostics, d => d.Kind == DiagnosticKind.ResultDefaulted);
        Assert.Contains("Passed", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Place order", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shard_that_states_its_results_says_nothing()
    {
        var report = MergeableReportReader.Parse("""
            { "mergeableFormatVersion": 1, "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:01:00Z",
              "features": [ { "name": "Orders", "scenarios": [
                { "id": "s1", "name": "Place order", "result": "Failed" } ] } ] }
            """);

        Assert.DoesNotContain(report.Diagnostics, d => d.Kind == DiagnosticKind.ResultDefaulted);
    }

    /// <summary>Same defect, one level down: a step whose status this build cannot read was shown as passed.</summary>
    [Fact]
    public void A_step_status_this_build_does_not_understand_is_read_as_unrecorded_and_said()
    {
        var report = MergeableReportReader.Parse("""
            { "mergeableFormatVersion": 1, "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:01:00Z",
              "features": [ { "name": "Orders", "scenarios": [
                { "id": "s1", "name": "Place order", "result": "Failed", "steps": [
                    { "keyword": "Given", "text": "a cart", "status": "Passed" },
                    { "keyword": "When", "text": "it is priced", "status": "Exploded" } ] } ] } ] }
            """);

        var steps = report.Features[0].Scenarios[0].Steps!;
        Assert.Equal(ExecutionResult.Passed, steps[0].Status);
        Assert.Null(steps[1].Status);
        var entry = Assert.Single(report.Diagnostics, d => d.Kind == DiagnosticKind.ResultDefaulted);
        Assert.Contains("step", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Exploded", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_step_with_no_status_is_unrecorded_and_nothing_is_said()
    {
        var report = MergeableReportReader.Parse("""
            { "mergeableFormatVersion": 1, "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:01:00Z",
              "features": [ { "name": "Orders", "scenarios": [
                { "id": "s1", "name": "Place order", "result": "Passed", "steps": [ { "keyword": "Given", "text": "a cart" } ] } ] } ] }
            """);

        Assert.Null(report.Features[0].Scenarios[0].Steps![0].Status);
        Assert.Empty(report.Diagnostics);
    }

    /// <summary>Measured: an annotation written as <c>"Note"</c> became <c>Custom</c> with nothing said.</summary>
    [Fact]
    public void An_annotation_kind_this_build_does_not_understand_is_read_as_custom_and_said()
    {
        var report = MergeableReportReader.Parse("""
            { "mergeableFormatVersion": 1, "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:01:00Z",
              "features": [ { "name": "Orders", "scenarios": [
                { "id": "s1", "name": "Place order", "result": "Passed",
                  "httpInteractions": [],
                  "annotations": [ { "index": 0, "kind": "Note", "text": "hello" } ] } ] } ] }
            """);

        var annotation = Assert.Single(report.Annotations["s1"]);
        Assert.Equal(DiagramMarkerKind.Custom, annotation.Kind);
        var entry = Assert.Single(report.Diagnostics, d => d.Message.Contains("annotation", StringComparison.Ordinal));
        Assert.Contains("\"Note\"", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Custom", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>An interaction of a type this build cannot read is counted as a request, and that inflates every call count.</summary>
    [Fact]
    public void An_interaction_type_this_build_does_not_understand_is_said()
    {
        var report = MergeableReportReader.Parse("""
            { "mergeableFormatVersion": 1, "kronikolVersion": "3.5.1",
              "startTime": "2026-01-01T10:00:00Z", "endTime": "2026-01-01T10:01:00Z",
              "features": [ { "name": "Orders", "scenarios": [
                { "id": "s1", "name": "Place order", "result": "Passed",
                  "httpInteractions": [
                    { "type": "Reqponse", "metaType": "Default", "method": "GET", "uri": "https://pricing.test/q",
                      "serviceName": "Pricing", "callerName": "Tests", "requestResponseId": "1f3e0a4e-5d3c-4a5b-9c1d-2e3f4a5b6c7d" } ] } ] } ] }
            """);

        var entry = Assert.Single(report.Diagnostics, d => d.Message.Contains("interaction", StringComparison.Ordinal));
        Assert.Contains("\"Reqponse\"", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Request", entry.Message, StringComparison.Ordinal);
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
