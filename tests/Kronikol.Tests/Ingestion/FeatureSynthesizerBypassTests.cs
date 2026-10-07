using Kronikol.Ingestion;
using Kronikol.Reports;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// The tests-file lane's roll-up (#105, CUCUMBER_BYPASS_PLAN.md §4.3): an <c>end</c> of <c>passed</c> becomes
/// <see cref="ExecutionResult.Bypassed"/> when a step of the scenario, at any depth, was bypassed. Every other
/// <c>end</c> stands, since the contract makes it the verdict, and a scenario with none keeps its defaulted verdict.
/// </summary>
public class FeatureSynthesizerBypassTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 14, 23, 24, TimeSpan.Zero);

    private static Scenario Build(string? endStatus, params TestRunRecord[] steps)
    {
        var records = new List<TestRunRecord>
        {
            new() { Event = "start", TestId = "t1", TestName = "a bypass", Feature = "bypass.spec.ts", Timestamp = T0 },
        };
        records.AddRange(steps);
        if (endStatus is not null)
            records.Add(new TestRunRecord { Event = "end", TestId = "t1", Status = endStatus, Timestamp = T0.AddSeconds(9) });

        return FeatureSynthesizer.Build(records, logs: null).Features.Single().Scenarios.Single();
    }

    private static TestRunRecord Step(int second, string text, string status, int level = 0, string? reason = null) => new()
    {
        Event = "step",
        TestId = "t1",
        Text = text,
        Level = level,
        Status = status,
        BypassReason = reason,
        Timestamp = T0.AddSeconds(second),
    };

    [Fact]
    public void A_bypassed_step_makes_a_passed_scenario_bypassed()
    {
        var scenario = Build("passed",
            Step(1, "the overview has loaded", "passed"),
            Step(2, "mock Gemini served the summary", "bypassed", reason: "not in the deployed path"),
            Step(3, "the figure is the API's", "passed"));

        Assert.Equal(ExecutionResult.Bypassed, scenario.Result);
        Assert.False(scenario.ResultDefaulted);
        Assert.Equal("not in the deployed path", scenario.Steps![1].BypassReason);
    }

    [Fact]
    public void A_bypassed_sub_step_makes_a_passed_scenario_bypassed()
    {
        // LightBDD's status rolls up from sub-steps, and a reporter writes a nested step at level 1.
        var scenario = Build("passed",
            Step(1, "the summary is checked", "passed"),
            Step(2, "mock Gemini served it", "bypassed", level: 1, reason: "not in the deployed path"),
            Step(3, "the figure is the API's", "passed"));

        var parent = Assert.Single(scenario.Steps!, s => s.Text == "the summary is checked");
        Assert.Equal(ExecutionResult.Bypassed, Assert.Single(parent.SubSteps!).Status);
        Assert.Equal(ExecutionResult.Bypassed, scenario.Result);
    }

    [Theory]
    [InlineData("failed", ExecutionResult.Failed)]
    [InlineData("skipped", ExecutionResult.Skipped)]
    [InlineData("timedOut", ExecutionResult.Failed)]
    [InlineData("bypassed", ExecutionResult.Bypassed)]
    public void Any_other_end_stands_beside_a_bypassed_step(string endStatus, ExecutionResult expected)
    {
        var scenario = Build(endStatus, Step(1, "mock Gemini served the summary", "bypassed", reason: "not in the deployed path"));

        Assert.Equal(expected, scenario.Result);
    }

    [Fact]
    public void A_failed_step_does_not_overturn_a_passed_end()
    {
        // Only a passed end moves, and only to Bypassed: the end is the verdict by contract (Q3).
        var scenario = Build("passed",
            Step(1, "a step the reporter saw fail", "failed"),
            Step(2, "mock Gemini served the summary", "bypassed", reason: "not in the deployed path"));

        Assert.Equal(ExecutionResult.Bypassed, scenario.Result);
    }

    [Fact]
    public void A_scenario_with_no_end_keeps_its_defaulted_verdict()
    {
        var scenario = Build(null, Step(1, "mock Gemini served the summary", "bypassed", reason: "not in the deployed path"));

        Assert.Equal(ExecutionResult.Passed, scenario.Result);
        Assert.True(scenario.ResultDefaulted);
    }

    [Fact]
    public void A_passed_scenario_with_no_bypassed_step_stays_passed()
    {
        var scenario = Build("passed", Step(1, "the overview has loaded", "passed"), Step(2, "nothing to see", "skipped"));

        Assert.Equal(ExecutionResult.Passed, scenario.Result);
    }
}
