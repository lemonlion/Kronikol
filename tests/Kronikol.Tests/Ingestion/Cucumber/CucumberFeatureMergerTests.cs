using Kronikol.Ingestion;
using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion.Cucumber;

public class CucumberFeatureMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static CucumberSynthesisResult BuildCucumber(string testId, params (string Keyword, string Text, int StartSeconds)[] steps)
    {
        var windows = new List<CucumberStepWindow>();
        var scenarioSteps = new List<ScenarioStep>();
        foreach (var (keyword, text, start) in steps)
        {
            var step = new ScenarioStep { Keyword = keyword, Text = text, Status = ExecutionResult.Passed };
            scenarioSteps.Add(step);
            windows.Add(new CucumberStepWindow(step, T0.AddSeconds(start), T0.AddSeconds(start + 1)));
        }

        var scenario = new Scenario
        {
            Id = testId,
            DisplayName = "A gherkin scenario",
            Result = ExecutionResult.Passed,
            Steps = scenarioSteps.ToArray(),
        };
        var feature = new Feature { DisplayName = "Gherkin feature", Scenarios = [scenario] };
        return new CucumberSynthesisResult(
            [feature],
            T0.UtcDateTime,
            T0.AddSeconds(10).UtcDateTime,
            [],
            new Dictionary<string, string> { [testId] = scenario.DisplayName },
            new Dictionary<string, IReadOnlyList<CucumberStepWindow>> { [testId] = windows },
            new HashSet<string> { testId },
            []);
    }

    [Fact]
    public void With_nothing_to_own_the_tests_file_model_is_returned_unchanged()
    {
        var fromTests = FeatureSynthesizer.Build(
            [new TestRunRecord { Event = "start", TestId = "t1", TestName = "plain", Timestamp = T0 }], null);
        var empty = CucumberFeatureSynthesizer.Build(CucumberMessagesReader.Read(new StringReader("")));

        var merged = CucumberFeatureMerger.Merge(empty, fromTests);

        Assert.Same(fromTests, merged);
    }

    [Fact]
    public void Assertions_nest_under_the_step_that_was_running()
    {
        const string testId = "t1";
        var cucumber = BuildCucumber(testId, ("Given", "a customer", 0), ("When", "an order is placed", 5));
        var records = new[]
        {
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the customer exists", Status = "passed", Timestamp = T0.AddSeconds(0.5) },
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the order is confirmed", Status = "failed", Error = "nope", Timestamp = T0.AddSeconds(5.5) },
            // Between the two steps: belongs to the step whose bar was last drawn.
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "in between", Status = "passed", Timestamp = T0.AddSeconds(3) },
        };
        var fromTests = FeatureSynthesizer.Build(records, null);

        var merged = CucumberFeatureMerger.Merge(cucumber, fromTests, records);

        var scenario = Assert.Single(merged.Features.SelectMany(f => f.Scenarios));
        Assert.Equal(
            new[] { "the customer exists", "in between" }.Select(t => $"{Track.PassSymbol} {t}"),
            scenario.Steps![0].SubSteps!.Select(s => s.Text));
        var failed = Assert.Single(scenario.Steps![1].SubSteps!);
        Assert.Equal($"{Track.FailSymbol} the order is confirmed", failed.Text);
        Assert.Equal(ExecutionResult.Failed, failed.Status);
        Assert.Contains("nope", failed.Comments!);
    }

    [Fact]
    public void An_assertion_before_every_step_becomes_a_top_level_row()
    {
        const string testId = "t1";
        var cucumber = BuildCucumber(testId, ("Given", "a customer", 5));
        var records = new[]
        {
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "before anything ran", Status = "passed", Timestamp = T0 },
        };

        var merged = CucumberFeatureMerger.Merge(cucumber, FeatureSynthesizer.Build(records, null), records);

        var scenario = Assert.Single(merged.Features.SelectMany(f => f.Scenarios));
        Assert.Equal(2, scenario.Steps!.Length);
        Assert.Equal($"{Track.PassSymbol} before anything ran", scenario.Steps[^1].Text);
    }

    [Fact]
    public void Reporter_step_events_of_an_owned_scenario_are_the_ones_the_messages_replace()
    {
        var cucumber = BuildCucumber("t1", ("Given", "a customer", 0));

        Assert.True(CucumberFeatureMerger.IsReplacedStep(
            new TestRunRecord { Event = "step", TestId = "t1", Text = "reporter step" }, cucumber));
        Assert.False(CucumberFeatureMerger.IsReplacedStep(
            new TestRunRecord { Event = "step", TestId = "other", Text = "reporter step" }, cucumber));
        Assert.False(CucumberFeatureMerger.IsReplacedStep(
            new TestRunRecord { Event = "assertion", TestId = "t1", Text = "an assertion" }, cucumber));
    }

    [Fact]
    public void The_tests_file_still_contributes_attachments_and_a_failure_the_gherkin_steps_never_saw()
    {
        const string testId = "t1";
        var cucumber = BuildCucumber(testId, ("Given", "a customer", 0));
        var fromTests = FeatureSynthesizer.Build(
            [new TestRunRecord { Event = "end", TestId = testId, Status = "failed", Error = "Test timeout of 30000ms exceeded", Timestamp = T0.AddSeconds(30) }],
            null);
        fromTests.Features[0].Scenarios[0].Attachments = [new FileAttachment("trace.zip", "/tmp/trace.zip", "application/zip")];

        var merged = CucumberFeatureMerger.Merge(cucumber, fromTests);

        var scenario = Assert.Single(merged.Features.SelectMany(f => f.Scenarios));
        Assert.Equal("Test timeout of 30000ms exceeded", scenario.ErrorMessage);
        Assert.Equal("trace.zip", Assert.Single(scenario.Attachments!).Name);
    }

    [Fact]
    public void Unowned_scenarios_are_kept_and_ordered_after_the_gherkin_features()
    {
        var cucumber = BuildCucumber("t1", ("Given", "a customer", 0));
        var fromTests = FeatureSynthesizer.Build(
        [
            new TestRunRecord { Event = "start", TestId = "t1", TestName = "reporter name", Feature = "Gherkin feature", Timestamp = T0 },
            new TestRunRecord { Event = "start", TestId = "t2", TestName = "a unit test", Feature = "Units", Timestamp = T0 },
        ], null);

        var merged = CucumberFeatureMerger.Merge(cucumber, fromTests);

        Assert.Equal(["Gherkin feature", "Units"], merged.Features.Select(f => f.DisplayName));
        // The scenario the messages own keeps the Gherkin display name, not the reporter's.
        Assert.Equal("A gherkin scenario", merged.Features[0].Scenarios[0].DisplayName);
        Assert.Equal("a unit test", merged.Features[1].Scenarios[0].DisplayName);
        Assert.Equal("A gherkin scenario", merged.TestNames["t1"]);
    }
    [Fact]
    public void The_reporters_attachments_win_over_the_messages_copies_of_the_same_artefact()
    {
        // playwright-bdd inlines every attachment as BASE64 into the messages, so a screenshot the
        // reporter already materialised arrived twice (seen live: 58 files for 29 screenshots).
        var fromMessages = new[]
        {
            new FileAttachment("screenshot-start.png", "C:/tmp/msg/1.png", "image/png"),
            new FileAttachment("only-in-messages.txt", "C:/tmp/msg/2.txt", "text/plain"),
        };
        var fromTests = new[]
        {
            new FileAttachment("screenshot-start.png", "C:/logs/attachments/t/screenshot-start.png", "image/png"),
            new FileAttachment("Grafana trace", "http://localhost:3900/explore", "text/uri-list"),
        };

        var merged = CucumberFeatureMerger.MergeAttachments(fromMessages, fromTests);

        Assert.Equal(["screenshot-start.png", "Grafana trace", "only-in-messages.txt"], merged.Select(a => a.Name));
        Assert.Equal("C:/logs/attachments/t/screenshot-start.png", merged[0].RelativePath);
        Assert.Same(fromTests, CucumberFeatureMerger.MergeAttachments(null, fromTests));
    }
}
