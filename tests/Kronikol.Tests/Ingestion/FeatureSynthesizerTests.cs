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
    public void Start_records_carry_examples_block_fields_onto_scenarios()
    {
        var records = new List<TestRunRecord>
        {
            new()
            {
                Event = "start", TestId = "t1", TestName = "Movement (OneWeek)", Feature = "Market share", Timestamp = T0,
                OutlineId = "Movement", ExampleValues = new Dictionary<string, string> { ["Period"] = "OneWeek" },
                ExamplesBlockName = "the merchant gained share", ExamplesBlockDescription = "movement is positive", ExamplesBlockIndex = 0
            },
            new() { Event = "end", TestId = "t1", Status = "passed", Timestamp = T0.AddSeconds(1) },
            new()
            {
                Event = "start", TestId = "t2", TestName = "Movement (FourWeeks)", Feature = "Market share", Timestamp = T0.AddSeconds(2),
                OutlineId = "Movement", ExampleValues = new Dictionary<string, string> { ["Period"] = "FourWeeks" },
                ExamplesBlockIndex = 1
            },
            new() { Event = "end", TestId = "t2", Status = "passed", Timestamp = T0.AddSeconds(3) },
            new() { Event = "start", TestId = "t3", TestName = "Plain", Feature = "Market share", Timestamp = T0.AddSeconds(4) },
            new() { Event = "end", TestId = "t3", Status = "passed", Timestamp = T0.AddSeconds(5) },
        };

        var result = FeatureSynthesizer.Build(records, logs: null);
        var scenarios = result.Features.Single().Scenarios;

        var first = scenarios.Single(s => s.Id == "t1");
        Assert.Equal("the merchant gained share", first.ExamplesBlockName);
        Assert.Equal("movement is positive", first.ExamplesBlockDescription);
        Assert.Equal(0, first.ExamplesBlockIndex);

        var second = scenarios.Single(s => s.Id == "t2");
        Assert.Null(second.ExamplesBlockName);
        Assert.Equal(1, second.ExamplesBlockIndex);

        var plain = scenarios.Single(s => s.Id == "t3");
        Assert.Null(plain.ExamplesBlockName);
        Assert.Null(plain.ExamplesBlockDescription);
        Assert.Null(plain.ExamplesBlockIndex);
    }

    [Fact]
    public void Examples_block_fields_parse_from_ndjson()
    {
        var record = TestRunRecord.FromJson(
            """{"event":"start","testId":"t1","outlineId":"O","exampleValues":{"Period":"OneWeek"},"examplesBlockName":"gained","examplesBlockDescription":"desc","examplesBlockIndex":2}""");

        Assert.Equal("gained", record.ExamplesBlockName);
        Assert.Equal("desc", record.ExamplesBlockDescription);
        Assert.Equal(2, record.ExamplesBlockIndex);
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
    /// <summary>
    /// <c>plans/INGEST_FIDELITY_PLAN.md</c> P3 and T7: one test id, a failed attempt then a passing one. The scenario was
    /// one passed scenario that kept the first attempt's error and both attempts' steps. It is the last attempt, as the
    /// Cucumber lane has always built a retried scenario: its verdict, error, duration and steps, <c>Attempt</c> set, and a
    /// <c>retry N</c> label for each earlier attempt.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_retried_test_is_its_last_attempt_with_a_retry_label_per_earlier_one(bool numbered)
    {
        var records = new List<TestRunRecord>
        {
            new() { Event = "start", TestId = "p3", TestName = "charges a card", Feature = "charge.test.ts", Tags = ["payments"], Attempt = numbered ? 1 : null, Timestamp = T0 },
            new() { Event = "step", TestId = "p3", Text = "attempt one", Timestamp = T0.AddMilliseconds(500) },
            new() { Event = "end", TestId = "p3", Status = "failed", Error = "expected SETTLED, received PENDING", DurationMs = 2000, Timestamp = T0.AddMilliseconds(2000) },
            new() { Event = "start", TestId = "p3", TestName = "charges a card", Feature = "charge.test.ts", Tags = ["payments"], Attempt = numbered ? 2 : null, Timestamp = T0.AddMilliseconds(3000) },
            new() { Event = "step", TestId = "p3", Text = "attempt two", Timestamp = T0.AddMilliseconds(3500) },
            new() { Event = "end", TestId = "p3", Status = "passed", DurationMs = 1500, Timestamp = T0.AddMilliseconds(5000) },
        };

        var result = FeatureSynthesizer.Build(records, logs: null);

        var scenario = Assert.Single(result.Features.SelectMany(f => f.Scenarios));
        Assert.Equal(ExecutionResult.Passed, scenario.Result);
        Assert.Null(scenario.ErrorMessage);
        Assert.Equal(2, scenario.Attempt);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), scenario.Duration);
        Assert.Equal(["attempt two"], scenario.Steps!.Select(s => s.Text));
        // The tags are the winning start's, once; each earlier attempt is a label.
        Assert.Equal(["payments", "retry 1"], scenario.Labels);
        // The run began with the first attempt.
        Assert.Equal(T0.UtcDateTime, result.Start);
    }

    [Fact]
    public void An_attempt_number_on_a_record_places_it_in_that_attempt_whatever_its_timestamp()
    {
        // A worker that writes a failed attempt's verdict late, after the retry has started.
        var records = new List<TestRunRecord>
        {
            new() { Event = "start", TestId = "t", TestName = "late verdict", Attempt = 1, Timestamp = T0 },
            new() { Event = "start", TestId = "t", TestName = "late verdict", Attempt = 2, Timestamp = T0.AddSeconds(3) },
            new() { Event = "end", TestId = "t", Status = "failed", Error = "first attempt", Attempt = 1, Timestamp = T0.AddSeconds(4) },
            new() { Event = "end", TestId = "t", Status = "passed", Attempt = 2, Timestamp = T0.AddSeconds(5) },
        };

        var scenario = FeatureSynthesizer.Build(records, logs: null).Features.Single().Scenarios.Single();

        Assert.Equal(ExecutionResult.Passed, scenario.Result);
        Assert.Null(scenario.ErrorMessage);
        Assert.Equal(2, scenario.Attempt);
    }

    [Fact]
    public void A_test_that_ran_once_says_nothing_about_attempts_unless_its_start_does()
    {
        var records = new List<TestRunRecord>
        {
            new() { Event = "start", TestId = "once", TestName = "ran once", Timestamp = T0 },
            new() { Event = "end", TestId = "once", Status = "passed", Timestamp = T0.AddSeconds(1) },
            // A producer that reports only the attempt that counted: the number is kept, no label is invented.
            new() { Event = "start", TestId = "third", TestName = "third time lucky", Attempt = 3, Timestamp = T0.AddSeconds(2) },
            new() { Event = "end", TestId = "third", Status = "passed", Timestamp = T0.AddSeconds(3) },
        };

        var scenarios = FeatureSynthesizer.Build(records, logs: null).Features.Single().Scenarios;

        Assert.Null(scenarios.Single(s => s.Id == "once").Attempt);
        var third = scenarios.Single(s => s.Id == "third");
        Assert.Equal(3, third.Attempt);
        Assert.Null(third.Labels);
    }

    [Fact]
    public void An_ingested_assertion_is_marked_and_a_step_without_a_keyword_is_not()
    {
        // #145: both are keyword-less, and only the assertion is one (SHOULDLY_ASSERTIONS_PLAN section 3.10).
        var records = new List<TestRunRecord>
        {
            new() { Event = "start", TestId = "mark", TestName = "charges a card", Timestamp = T0 },
            new() { Event = "step", TestId = "mark", Text = "a saved card", Status = "passed", Timestamp = T0.AddSeconds(1) },
            new() { Event = "assertion", TestId = "mark", Text = "the charge settled", Status = "passed", Timestamp = T0.AddSeconds(2) },
            new() { Event = "end", TestId = "mark", Status = "passed", Timestamp = T0.AddSeconds(3) },
        };

        var scenario = FeatureSynthesizer.Build(records, logs: null).Features.Single().Scenarios.Single();
        var steps = scenario.Steps!.SelectMany(s => new[] { s }.Concat(s.SubSteps ?? [])).ToArray();

        Assert.False(steps.Single(s => s.Text == "a saved card").IsAssertion);
        Assert.True(steps.Single(s => s.Text.EndsWith("the charge settled", StringComparison.Ordinal)).IsAssertion);
    }

    /// <summary>
    /// <c>plans/INGEST_FIDELITY_PLAN.md</c> T9 and T10: where a test is written and why a step failed. The model had every
    /// field (the digest's "written at" and failing step, CTRF's <c>filePath</c>, <c>kronikol query failures</c>); the
    /// tests records carried none of it, and a step's error reached only its comments.
    /// </summary>
    [Fact]
    public void Source_locations_and_a_failed_steps_message_reach_the_scenario_its_feature_and_its_steps()
    {
        var records = new List<TestRunRecord>
        {
            new() { Event = "start", TestId = "loc", TestName = "charges a card", Feature = "charge", SourceFile = @"src\charge\charge.test.ts", SourceLine = 12, Timestamp = T0 },
            new() { Event = "step", TestId = "loc", Text = "a saved card", SourceFile = "./src/charge/charge.test.ts", SourceLine = 14, Status = "passed", Timestamp = T0.AddSeconds(1) },
            new() { Event = "step", TestId = "loc", Text = "the card is charged", SourceFile = "src/charge/charge.test.ts", SourceLine = 18, Status = "failed", Error = "expected 201, received 502", StackTrace = "at charge.test.ts:18", Timestamp = T0.AddSeconds(2) },
            new() { Event = "assertion", TestId = "loc", Text = "the charge settled", SourceFile = "src/charge/charge.test.ts", SourceLine = 19, Status = "failed", Error = "expected SETTLED", Timestamp = T0.AddSeconds(3) },
            new() { Event = "end", TestId = "loc", Status = "failed", Error = "expected 201, received 502", Timestamp = T0.AddSeconds(4) },
            new() { Event = "start", TestId = "loc2", TestName = "refunds a card", Feature = "charge", SourceFile = "src/charge/refund.test.ts", SourceLine = 5, Timestamp = T0.AddSeconds(5) },
            new() { Event = "end", TestId = "loc2", Status = "passed", Timestamp = T0.AddSeconds(6) },
        };

        var feature = FeatureSynthesizer.Build(records, logs: null).Features.Single();

        var scenario = feature.Scenarios.Single(s => s.Id == "loc");
        Assert.Equal("src/charge/charge.test.ts", scenario.SourceFile);
        Assert.Equal(12, scenario.SourceLine);
        // The first path seen wins for the feature, as on the ReqNRoll lane.
        Assert.Equal("src/charge/charge.test.ts", feature.SourceFile);
        Assert.Equal("src/charge/refund.test.ts", feature.Scenarios.Single(s => s.Id == "loc2").SourceFile);

        // A step keeps the file name only, the step contract.
        var (given, when) = (scenario.Steps![0], scenario.Steps[1]);
        Assert.Equal(("charge.test.ts", 14), (given.SourceFile, given.SourceLine));
        Assert.Null(given.FailureMessage);
        Assert.Equal(("charge.test.ts", 18), (when.SourceFile, when.SourceLine));
        Assert.Equal("expected 201, received 502", when.FailureMessage);
        // The comments the HTML renders stay as they were.
        Assert.Equal(["expected 201, received 502", "at charge.test.ts:18"], when.Comments);

        var assertion = Assert.Single(when.SubSteps!);
        Assert.Equal(("charge.test.ts", 19), (assertion.SourceFile, assertion.SourceLine));
        Assert.Equal("expected SETTLED", assertion.FailureMessage);
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
