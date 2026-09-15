using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The report-time boundary between a scenario's calls and the host's (BACKGROUND_ATTRIBUTION_PLAN, option
/// D). A host built inside a test keeps resolving the test's identity from its hosted services for as long
/// as it lives, so a call that inherited the identity after the scenario ended is the host's, not the
/// scenario's. A stated identity is trusted; a pair follows its request.
/// </summary>
public class BackgroundAttributionTests
{
    private static readonly DateTimeOffset Ended = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static Feature[] Scenario(string id = "s1", DateTimeOffset? endedAt = null) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = id, DisplayName = "Pay", Result = ExecutionResult.Passed, EndedAt = endedAt ?? Ended }]
        }
    ];

    private static RequestResponseLog Log(string testId, AttributionSource? source, DateTimeOffset? at,
        RequestResponseType type = RequestResponseType.Request, Guid? pair = null) =>
        new("Pay", testId, HttpMethod.Get, null, new Uri("http://orders/api/orders"), [], "orders", "Test",
            type, Guid.NewGuid(), pair ?? Guid.NewGuid(), false, HttpStatusCode.OK)
        {
            Timestamp = at,
            AttributionSource = source
        };

    [Fact]
    public void A_call_that_inherited_the_scenario_after_it_ended_is_expired()
    {
        var log = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(1));

        var expired = Assert.Single(BackgroundAttribution.Expire(Scenario(), [log]));

        Assert.Equal(TestIdentityScope.UnknownTestId, expired.TestId);
        Assert.Equal(TestIdentityScope.UnknownTestName, expired.TestName);
        Assert.Equal(AttributionSource.Expired, expired.AttributionSource);
        Assert.Equal("s1", expired.ExpiredFromTestId);
        // Everything else travels: the call is still the same call.
        Assert.Equal(log.RequestResponseId, expired.RequestResponseId);
        Assert.Equal(log.Timestamp, expired.Timestamp);
    }

    [Fact]
    public void A_call_made_before_the_end_stays_the_scenarios()
    {
        var log = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(-1));

        var kept = Assert.Single(BackgroundAttribution.Expire(Scenario(), [log]));

        Assert.Same(log, kept);
    }

    [Fact]
    public void A_call_at_the_very_instant_of_the_end_stays_the_scenarios()
    {
        var log = Log("s1", AttributionSource.TestContext, Ended);

        Assert.Same(log, Assert.Single(BackgroundAttribution.Expire(Scenario(), [log])));
    }

    [Theory]
    [InlineData(AttributionSource.RequestHeader)]
    [InlineData(AttributionSource.Scope)]
    public void A_stated_identity_is_trusted_after_the_end(AttributionSource source)
    {
        var log = Log("s1", source, Ended.AddSeconds(5));

        Assert.Same(log, Assert.Single(BackgroundAttribution.Expire(Scenario(), [log])));
    }

    [Fact]
    public void A_capture_with_no_recorded_provenance_is_trusted()
    {
        var log = Log("s1", null, Ended.AddSeconds(5));

        Assert.Same(log, Assert.Single(BackgroundAttribution.Expire(Scenario(), [log])));
    }

    [Fact]
    public void The_global_fallback_is_inherited_too()
    {
        var log = Log("s1", AttributionSource.GlobalFallback, Ended.AddSeconds(1));

        Assert.Equal(AttributionSource.Expired, Assert.Single(BackgroundAttribution.Expire(Scenario(), [log])).AttributionSource);
    }

    [Fact]
    public void A_response_that_lands_after_the_end_stays_with_the_request_the_scenario_made()
    {
        var pair = Guid.NewGuid();
        var request = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(-1), RequestResponseType.Request, pair);
        var response = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(3), RequestResponseType.Response, pair);

        var result = BackgroundAttribution.Expire(Scenario(), [request, response]);

        Assert.Same(request, result[0]);
        Assert.Same(response, result[1]);
    }

    [Fact]
    public void A_request_made_after_the_end_takes_its_response_with_it()
    {
        var pair = Guid.NewGuid();
        var request = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(1), RequestResponseType.Request, pair);
        var response = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(2), RequestResponseType.Response, pair);

        var result = BackgroundAttribution.Expire(Scenario(), [request, response]);

        Assert.All(result, l => Assert.Equal(AttributionSource.Expired, l.AttributionSource));
        Assert.All(result, l => Assert.Equal("s1", l.ExpiredFromTestId));
    }

    [Fact]
    public void A_scenario_without_a_recorded_end_never_loses_a_call()
    {
        var features = Scenario();
        features[0].Scenarios[0].EndedAt = null;
        var log = Log("s1", AttributionSource.TestContext, Ended.AddHours(1));

        Assert.Same(log, Assert.Single(BackgroundAttribution.Expire(features, [log])));
    }

    [Fact]
    public void A_call_with_no_timestamp_is_left_alone()
    {
        var log = Log("s1", AttributionSource.TestContext, null);

        Assert.Same(log, Assert.Single(BackgroundAttribution.Expire(Scenario(), [log])));
    }

    [Fact]
    public void A_call_of_a_scenario_the_report_does_not_know_is_left_alone()
    {
        var log = Log("elsewhere", AttributionSource.TestContext, Ended.AddSeconds(1));

        Assert.Same(log, Assert.Single(BackgroundAttribution.Expire(Scenario(), [log])));
    }

    [Fact]
    public void A_diagram_marker_is_never_expired()
    {
        var marker = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(1));
        marker.IsOverrideStart = true;

        Assert.Same(marker, Assert.Single(BackgroundAttribution.Expire(Scenario(), [marker])));
    }

    [Fact]
    public void Expiring_twice_changes_nothing()
    {
        var once = BackgroundAttribution.Expire(Scenario(), [Log("s1", AttributionSource.TestContext, Ended.AddSeconds(1))]);

        var twice = BackgroundAttribution.Expire(Scenario(), once);

        Assert.Same(once[0], twice[0]);
        Assert.Equal("s1", twice[0].ExpiredFromTestId);
    }

    [Fact]
    public void Order_and_length_are_preserved_and_nulls_pass_through()
    {
        var before = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(-1));
        var after = Log("s1", AttributionSource.TestContext, Ended.AddSeconds(1));

        var result = BackgroundAttribution.Expire(Scenario(), [before, null!, after]);

        Assert.Equal(3, result.Length);
        Assert.Same(before, result[0]);
        Assert.Null(result[1]);
        Assert.Equal(AttributionSource.Expired, result[2].AttributionSource);
    }

    [Fact]
    public void The_summary_counts_a_pair_once_and_names_the_scenario()
    {
        var pair = Guid.NewGuid();
        var logs = BackgroundAttribution.Expire(Scenario(),
        [
            Log("s1", AttributionSource.TestContext, Ended.AddSeconds(-1)),
            Log("s1", AttributionSource.TestContext, Ended.AddSeconds(1), RequestResponseType.Request, pair),
            Log("s1", AttributionSource.TestContext, Ended.AddSeconds(2), RequestResponseType.Response, pair),
        ]);

        var summary = BackgroundAttribution.Summarise(logs, Scenario());

        Assert.Equal(1, summary.Calls);
        var group = Assert.Single(summary.AfterScenarioEnd);
        Assert.Equal("s1", group.ScenarioId);
        Assert.Equal("Pay", group.ScenarioName);
        Assert.Equal(1, group.Calls);
        Assert.Equal(Ended.AddSeconds(2), group.LastAt);
        Assert.Equal(2, summary.Interactions.Count);
    }

    [Fact]
    public void The_summary_of_a_run_with_nothing_in_the_background_is_empty()
    {
        var logs = new[] { Log("s1", AttributionSource.TestContext, Ended.AddSeconds(-1)) };

        Assert.Same(BackgroundCalls.None, BackgroundAttribution.Summarise(logs, Scenario()));
        Assert.Same(BackgroundCalls.None, BackgroundAttribution.Summarise(null, Scenario()));
    }

    [Fact]
    public void A_call_captured_with_no_identity_is_background_but_was_taken_from_no_scenario()
    {
        // CaptureBackground on: the resolver hands back the unknown identity with its reason.
        var detached = Log(TestIdentityScope.UnknownTestId, AttributionSource.Detached, Ended.AddSeconds(1));

        var summary = BackgroundAttribution.Summarise([detached], Scenario());

        Assert.Equal(1, summary.Calls);
        Assert.Empty(summary.AfterScenarioEnd);
        Assert.Same(detached, Assert.Single(summary.Interactions));
    }
}
