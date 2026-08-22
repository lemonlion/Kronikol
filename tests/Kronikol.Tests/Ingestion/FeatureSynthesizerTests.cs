using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

public class FeatureSynthesizerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Builds_features_scenarios_steps_and_results_from_test_records()
    {
        var records = new List<TestRunRecord>
        {
            new() { Event = "start", TestId = "t1", TestName = "overview › renders", Feature = "overview.spec.ts", Timestamp = T0 },
            new() { Event = "step", TestId = "t1", Text = "open the overview", Timestamp = T0.AddSeconds(1) },
            new() { Event = "step", TestId = "t1", Text = "assert the summary", Keyword = "Then", Timestamp = T0.AddSeconds(2), Status = "passed", DurationMs = 20 },
            new() { Event = "end", TestId = "t1", Status = "failed", DurationMs = 9000, Error = "expected 1 got 2", Timestamp = T0.AddSeconds(9) },
            new() { Event = "start", TestId = "t2", TestName = "ai › summary", Feature = "ai.spec.ts", Timestamp = T0.AddSeconds(10) },
            new() { Event = "end", TestId = "t2", Status = "passed", Timestamp = T0.AddSeconds(15) },
        };

        var result = FeatureSynthesizer.Build(records, logs: null);

        Assert.Equal(2, result.Features.Length);
        var overview = result.Features.Single(f => f.DisplayName == "overview.spec.ts");
        var s1 = Assert.Single(overview.Scenarios);
        Assert.Equal("t1", s1.Id);
        Assert.Equal("overview › renders", s1.DisplayName);
        Assert.Equal(ExecutionResult.Failed, s1.Result);
        Assert.Equal("expected 1 got 2", s1.ErrorMessage);
        Assert.Equal(TimeSpan.FromSeconds(9), s1.Duration);
        Assert.Equal(["open the overview", "assert the summary"], s1.Steps!.Select(s => s.Text).ToArray());
        Assert.Equal("Then", s1.Steps![1].Keyword);
        Assert.Equal(ExecutionResult.Passed, s1.Steps![1].Status);

        var ai = result.Features.Single(f => f.DisplayName == "ai.spec.ts").Scenarios.Single();
        Assert.Equal(ExecutionResult.Passed, ai.Result);
        Assert.Equal(TimeSpan.FromSeconds(5), ai.Duration); // end - start when durationMs absent

        Assert.Equal(T0.UtcDateTime, result.Start);
        Assert.Equal(T0.AddSeconds(15).UtcDateTime, result.End);
        Assert.Equal("overview › renders", result.TestNames["t1"]);
    }

    [Fact]
    public void Tests_seen_only_in_logs_become_scenarios_in_the_default_feature()
    {
        var logs = new[]
        {
            new RequestResponseLog("From log", "onlylog", HttpMethod.Get, null, new Uri("http://a/"), [], "A", "T", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = T0 },
        };

        var result = FeatureSynthesizer.Build(null, logs, defaultFeatureName: "Captured");

        var feature = Assert.Single(result.Features);
        Assert.Equal("Captured", feature.DisplayName);
        var scenario = Assert.Single(feature.Scenarios);
        Assert.Equal("onlylog", scenario.Id);
        Assert.Equal("From log", scenario.DisplayName);
        Assert.Equal(ExecutionResult.Passed, scenario.Result);
    }

    [Fact]
    public void Status_vocabulary_maps_playwright_and_junit_words()
    {
        Assert.Equal(ExecutionResult.Passed, FeatureSynthesizer.MapStatus("passed"));
        Assert.Equal(ExecutionResult.Failed, FeatureSynthesizer.MapStatus("timedOut"));
        Assert.Equal(ExecutionResult.Failed, FeatureSynthesizer.MapStatus("interrupted"));
        Assert.Equal(ExecutionResult.Skipped, FeatureSynthesizer.MapStatus("skipped"));
        Assert.Equal(ExecutionResult.Skipped, FeatureSynthesizer.MapStatus("pending"));
        Assert.Equal(ExecutionResult.Failed, FeatureSynthesizer.MapStatus("something-else"));
    }

    [Fact]
    public void A_started_but_never_ended_test_uses_the_unknown_result()
    {
        var records = new[] { new TestRunRecord { Event = "start", TestId = "t", TestName = "crashed", Timestamp = T0 } };
        var result = FeatureSynthesizer.Build(records, null, resultWhenUnknown: ExecutionResult.Failed);
        Assert.Equal(ExecutionResult.Failed, result.Features.Single().Scenarios.Single().Result);
    }

    [Fact]
    public void Test_run_records_round_trip_through_json()
    {
        var record = new TestRunRecord { Event = "end", TestId = "t", TestName = "n", Status = "passed", DurationMs = 12.5, Timestamp = T0 };
        var back = TestRunRecord.FromJson(record.ToJson());
        Assert.Equal(record, back);
        Assert.Contains("\"event\":\"end\"", record.ToJson());
    }
    [Fact]
    public void Unknown_events_never_create_a_phantom_scenario()
    {
        // A reporter's run-level event (`testrun`, testId `__run__`) used to become a scenario that
        // never ended — Failed by ResultWhenUnknown, and enough to blank Specifications.html.
        var t0 = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
        var records = new[]
        {
            new TestRunRecord { Event = "start", TestId = "t1", TestName = "real", Timestamp = t0 },
            new TestRunRecord { Event = "end", TestId = "t1", Status = "passed", Timestamp = t0.AddSeconds(1) },
            new TestRunRecord { Event = "testrun", TestId = "__run__", Status = "passed", Timestamp = t0.AddSeconds(2) },
            new TestRunRecord { Event = "somethingelse", TestId = "ghost", Timestamp = t0.AddSeconds(3) },
        };

        var result = FeatureSynthesizer.Build(records, null, resultWhenUnknown: ExecutionResult.Failed);

        var scenario = Assert.Single(result.Features.SelectMany(f => f.Scenarios));
        Assert.Equal("t1", scenario.Id);
        Assert.Equal(ExecutionResult.Passed, scenario.Result);
        Assert.False(TestRunRecord.IsKnownEvent("testrun"));
        Assert.True(TestRunRecord.IsKnownEvent("Attachment"));
    }
}
