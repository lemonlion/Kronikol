using System.Net;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

namespace Kronikol.Tests.Parties;

/// <summary>
/// #190 R1: <c>kronikol merge</c> joins the shards' parties blocks (plans/PARTY_IDENTITY_PLAN.md §5.6): what the shards
/// agree on is kept, what they disagree on is left out and said, and <c>serviceId</c> is kept only when every shard has
/// the same one.
/// </summary>
[Collection("DiagramsFetcher")]
public class MergePartiesTests
{
    private static readonly DateTime Start = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static RequestResponseLog[] Pair(string testId, string caller, string service, string url, AttributionSource source)
    {
        var trace = Guid.NewGuid();
        var pair = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Request, trace, pair, false)
                { Timestamp = DateTimeOffset.UtcNow, AttributionSource = source },
            new RequestResponseLog(testId, testId, HttpMethod.Get, null, new Uri(url), [], service, caller, RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK)
                { Timestamp = DateTimeOffset.UtcNow, AttributionSource = source }
        ];
    }

    private static RequestResponseLog[] Breakfast(string testId) =>
    [
        .. Pair(testId, "Caller", "Breakfast Provider", "http://localhost/pancakes", AttributionSource.TestContext),
        .. Pair(testId, "Breakfast Provider", "Cow Service", "http://localhost:5001/milk", AttributionSource.RequestHeader)
    ];

    private static RequestResponseLog[] Waffles(string testId) =>
    [
        .. Pair(testId, "Caller", "Waffle House", "http://localhost/waffles", AttributionSource.TestContext),
        .. Pair(testId, "Waffle House", "Cow Service", "http://localhost:5001/milk", AttributionSource.RequestHeader)
    ];

    private static MergeableReport Shard(string scenario, RequestResponseLog[] logs, string? serviceId, params PartyDescriptor[] declared)
    {
        var features = new[] { new Feature { DisplayName = "Breakfast " + scenario, Scenarios = [new Scenario { Id = scenario, DisplayName = "Order " + scenario, Result = ExecutionResult.Passed }] } };
        var parties = serviceId is null && declared.Length == 0
            ? null
            : PartyResolver.Resolve(PartiesReport.CallsOf(logs), new PartyDeclarations(serviceId, declared, [], []), PartyObservations.None);
        return MergeableReportReader.Parse(ReportGenerator.GenerateMergeableReportJson(features, Start, Start.AddMinutes(1), null, null, null, null,
            WholeTestFlowVisualization.None, null, trackedLogs: logs, parties: parties));
    }

    private static Party Row(MergeableReport report, string name) => report.Parties!.Single(p => p.Name == name);

    [Fact]
    public void A_shard_reads_back_its_block()
    {
        var shard = Shard("a", Breakfast("a"), "breakfast-provider", new PartyDescriptor("Cow Service") { Kind = PartyKind.Double, Note = "WireMock stub" });

        Assert.Equal("breakfast-provider", shard.ServiceId);
        Assert.Equal(new Party("Cow Service", null, PartyKind.Double, PartyKindSource.Declared, "WireMock stub"), Row(shard, "Cow Service"));
    }

    [Fact]
    public void Shards_that_agree_keep_every_field()
    {
        var cow = new PartyDescriptor("Cow Service") { Kind = PartyKind.Double, Note = "WireMock stub" };

        var merged = MergeableReportMerger.Merge([Shard("a", Breakfast("a"), "breakfast-provider", cow), Shard("b", Breakfast("b"), "breakfast-provider", cow)]);

        Assert.Equal("breakfast-provider", merged.ServiceId);
        Assert.Equal(new Party("Cow Service", null, PartyKind.Double, PartyKindSource.Declared, "WireMock stub"), Row(merged, "Cow Service"));
        Assert.Equal((PartyKind.UnderTest, "breakfast-provider"), (Row(merged, "Breakfast Provider").Kind, Row(merged, "Breakfast Provider").Id));
        Assert.DoesNotContain(merged.Diagnostics, d => d.Kind == DiagnosticKind.PartyDisagreement);
    }

    [Fact]
    public void Shards_that_disagree_on_a_kind_leave_it_unknown_and_name_them()
    {
        var merged = MergeableReportMerger.Merge([
            Shard("a", Breakfast("a"), null, new PartyDescriptor("Cow Service") { Kind = PartyKind.Double }),
            Shard("b", Breakfast("b"), null, new PartyDescriptor("Cow Service") { Kind = PartyKind.Real })]);

        Assert.Equal((PartyKind.Unknown, (PartyKindSource?)null), (Row(merged, "Cow Service").Kind, Row(merged, "Cow Service").KindSource));
        var said = Assert.Single(merged.Diagnostics, d => d.Kind == DiagnosticKind.PartyDisagreement);
        Assert.Contains("\"Cow Service\"", said.Message);
        Assert.Contains("report 1 say \"double\"", said.Message);
        Assert.Contains("report 2 say \"real\"", said.Message);
    }

    [Fact]
    public void Two_suites_keep_two_subjects_each_with_its_id_and_no_service_id()
    {
        var merged = MergeableReportMerger.Merge([Shard("a", Breakfast("a"), "breakfast-provider"), Shard("b", Waffles("b"), "waffle-house")]);

        Assert.Null(merged.ServiceId);
        Assert.Equal((PartyKind.UnderTest, "breakfast-provider"), (Row(merged, "Breakfast Provider").Kind, Row(merged, "Breakfast Provider").Id));
        Assert.Equal((PartyKind.UnderTest, "waffle-house"), (Row(merged, "Waffle House").Kind, Row(merged, "Waffle House").Id));
    }

    [Fact]
    public void A_merge_of_shards_that_declare_nothing_has_no_block()
    {
        var merged = MergeableReportMerger.Merge([Shard("a", Breakfast("a"), null), Shard("b", Breakfast("b"), null)]);

        Assert.Null(merged.Parties);
        Assert.Null(merged.ServiceId);
    }
}
