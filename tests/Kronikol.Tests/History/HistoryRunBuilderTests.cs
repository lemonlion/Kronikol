using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// Turning a finished run into a ledger line (plans/CROSS_RUN_HISTORY_PLAN.md §3.3, §5.11, §9.1): one
/// roster position per scenario in report order, a result character each, and the identity that makes
/// eight shards one run and a re-run a different one.
/// </summary>
public class HistoryRunBuilderTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 12, 10, 4, 11, TimeSpan.Zero);

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario { Id = "t1", DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(1234.6), SourceFile = "Features/Checkout.feature", SourceLine = 12 },
                new Scenario { Id = "t2", DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed, ErrorMessage = "Expected 200 but got 500\n   at Checkout.Pay()", Duration = TimeSpan.FromMilliseconds(80), Attempt = 2 }
            ]
        },
        new Feature
        {
            DisplayName = "Refunds",
            Scenarios =
            [
                new Scenario { Id = "t3", DisplayName = "Refund a paid order", Result = ExecutionResult.Skipped }
            ]
        }
    ];

    private static RequestResponseLog[] Logs()
    {
        var pair = Guid.NewGuid();
        return
        [
            new RequestResponseLog("Pay with a valid card", "t1", HttpMethod.Post, null, new Uri("http://payments/charge/4711"), [], "payments", "Test",
                RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false),
            new RequestResponseLog("Pay with a valid card", "t1", HttpMethod.Post, null, new Uri("http://payments/charge/4711"), [], "payments", "Test",
                RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: HttpStatusCode.OK)
        ];
    }

    private static CiMetadata GitHub(string runId = "18273645", string? attempt = "2") =>
        new(CiEnvironment.GitHubActions, "42", "feature/x", "abc1234", "https://github.com/o/r/actions/runs/" + runId, "o/r", runId, attempt);

    [Fact]
    public void The_roster_lists_every_scenario_in_report_order_under_its_stable_id()
    {
        var (roster, run) = HistoryRunBuilder.Build(Features(), Logs(), "Suite", GitHub(), At, new HistoryBuildOptions());

        Assert.Equal(3, roster.Count);
        Assert.Equal(ScenarioStableId.Compute("Suite", "Checkout", "Pay with a valid card"), roster.Ids[0]);
        Assert.Equal(ScenarioStableId.Compute("Suite", "Refunds", "Refund a paid order"), roster.Ids[2]);
        Assert.Equal(["Pay with a valid card", "Pay with an expired card", "Refund a paid order"], roster.Names);
        Assert.Equal(["Checkout", "Checkout", "Refunds"], roster.Features);
        Assert.Equal("Features/Checkout.feature:12", roster.Sources[0]);
        Assert.Null(roster.Sources[1]);
        Assert.Equal("Suite", roster.Suite);
        Assert.Equal(roster.Hash, run.RosterHash);
    }

    [Fact]
    public void Results_attempts_durations_and_errors_are_positional()
    {
        var (_, run) = HistoryRunBuilder.Build(Features(), Logs(), "Suite", GitHub(), At, new HistoryBuildOptions());

        Assert.Equal("PFS", run.Results);
        Assert.Equal("-2-", run.Attempts);
        Assert.Equal([1235, 80, null], run.Durations);
        Assert.Equal([null, "e1", null], run.Errors);
        Assert.Equal("Expected 200 but got 500", run.ErrorText["e1"]);
    }

    [Fact]
    public void Two_failures_sharing_a_first_line_share_a_key_and_a_long_line_is_capped()
    {
        var features = Features();
        features[0].Scenarios[0].Result = ExecutionResult.Failed;
        features[0].Scenarios[0].ErrorMessage = "Expected 200 but got 500\nsomething else";
        features[1].Scenarios[0].Result = ExecutionResult.Failed;
        features[1].Scenarios[0].ErrorMessage = new string('x', 500);

        var (_, run) = HistoryRunBuilder.Build(features, [], "Suite", GitHub(), At, new HistoryBuildOptions());

        Assert.Equal(["e1", "e1", "e2"], run.Errors);
        Assert.Equal(2, run.ErrorText.Count);
        Assert.True(run.ErrorText["e2"].Length <= HistoryFormat.ErrorKeyLimit);
    }

    [Fact]
    public void Error_keys_can_be_hashed_so_no_message_text_reaches_the_ledger()
    {
        // §9.1: a message can carry a payload. HistoryErrorKeys = false keeps the clustering and drops
        // the text - the same first line still hashes to the same key across runs.
        var (_, run) = HistoryRunBuilder.Build(Features(), [], "Suite", GitHub(), At, new HistoryBuildOptions { ErrorKeys = false });

        var text = run.ErrorText["e1"];
        Assert.StartsWith("#", text);
        Assert.Matches("^#[0-9a-f]{8}$", text);
        Assert.DoesNotContain("Expected", text);
    }

    [Fact]
    public void Calls_shapes_and_dependencies_come_from_the_scenario_s_own_interactions()
    {
        var (_, run) = HistoryRunBuilder.Build(Features(), Logs(), "Suite", GitHub(), At, new HistoryBuildOptions());

        Assert.Equal([1, 0, 0], run.Calls);
        Assert.Equal(3, run.ShapeSet!.Count);
        Assert.NotEqual(run.ShapeSet[0], run.ShapeSet[1]);
        Assert.Equal(run.ShapeSet[1], run.ShapeSet[2]); // both made no calls
        Assert.Equal(["Test>payments"], run.Deps);
    }

    [Fact]
    public void Durations_and_shapes_can_each_be_switched_off()
    {
        var (_, run) = HistoryRunBuilder.Build(Features(), Logs(), "Suite", GitHub(), At, new HistoryBuildOptions { Durations = false, Shapes = false });

        Assert.Null(run.Durations);
        Assert.Null(run.ShapeSet);
        Assert.Null(run.ShapeOrdered);
        Assert.Null(run.Calls);
        Assert.NotNull(run.Deps);
    }

    [Fact]
    public void A_defaulted_result_is_recorded_as_unknown_never_as_a_pass()
    {
        // §5.11: a scenario whose process died mid-run took ResultWhenUnknown (Passed by default). Written
        // as P it would poison the trend permanently; written as ? it is not a verdict at all.
        var features = Features();
        features[0].Scenarios[0].ResultDefaulted = true;

        var (_, run) = HistoryRunBuilder.Build(features, [], "Suite", GitHub(), At, new HistoryBuildOptions());

        Assert.Equal("?FS", run.Results);
    }

    [Fact]
    public void The_run_identity_comes_from_the_provider_and_keeps_the_attempt()
    {
        Assert.Equal("gh:18273645:2", HistoryRunBuilder.RunId(GitHub(), At, "salt"));
        Assert.Equal("gh:18273645:1", HistoryRunBuilder.RunId(GitHub(attempt: null), At, "salt"));
        Assert.Equal("ado:9001:1", HistoryRunBuilder.RunId(new CiMetadata(CiEnvironment.AzureDevOps, "20260912.1", "main", "abc", null, null, "9001"), At, "salt"));
        Assert.Matches("^local:20260912T100411Z:[0-9a-f]{8}$", HistoryRunBuilder.RunId(null, At, "salt"));
        Assert.NotEqual(HistoryRunBuilder.RunId(null, At, "salt"), HistoryRunBuilder.RunId(null, At, "other machine"));
    }

    [Fact]
    public void The_run_carries_the_ci_labels_and_leaves_partial_undecided_by_default()
    {
        var (_, run) = HistoryRunBuilder.Build(Features(), [], "Suite", GitHub(), At, new HistoryBuildOptions());

        Assert.Equal("gh:18273645:2", run.Id);
        Assert.Equal("Suite", run.Suite);
        Assert.Equal("feature/x", run.Branch);
        Assert.Equal("abc1234", run.Commit);
        Assert.Equal("GitHubActions", run.Provider);
        Assert.Equal("https://github.com/o/r/actions/runs/18273645", run.Url);
        Assert.Equal(At, run.At);
        Assert.Equal(1, run.Shards);
        Assert.Null(run.Partial);
    }

    [Fact]
    public void A_local_run_has_no_branch_and_is_the_local_stream()
    {
        var (_, run) = HistoryRunBuilder.Build(Features(), [], "Suite", null, At, new HistoryBuildOptions { Partial = true });

        Assert.Null(run.Branch);
        Assert.Null(run.Provider);
        Assert.Equal("local", run.Stream);
        Assert.True(run.Partial);
    }
}
