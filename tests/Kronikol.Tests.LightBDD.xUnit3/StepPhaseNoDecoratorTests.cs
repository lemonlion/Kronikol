using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit3;
using Kronikol.Tests.LightBDD.xUnit3;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(NoStepDecoratorScope))]

namespace Kronikol.Tests.LightBDD.xUnit3;

public sealed class NoStepDecoratorScope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
#pragma warning disable CS0618 // The overload this pins, as a suite on it has no step decorator.
        configuration.ReportWritersConfiguration().CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "No step decorator" });
#pragma warning restore CS0618
    }
}

/// <summary>
/// plans/STEP_PHASE_PLAN.md 5.4: the <c>[Obsolete]</c> overload registers no step decorator, so nothing starts a step and the HTTP
/// handler's fetcher is the only source of a phase. It still sets one.
/// </summary>
public class StepPhaseNoDecoratorTests : FeatureFixture
{
    private readonly List<string> _reads = [];

    private void Ask(string label)
    {
        new LightBddTestTrackingMessageHandlerOptions().CurrentStepTypeFetcher!();
        _reads.Add($"{label}={TestPhaseContext.Current}");
    }

    private Task Given_a_call(string label)
    {
        Ask(label);
        return Task.CompletedTask;
    }

    private Task Given_a_step_attribute_and_a_call(string label)
    {
        var (_, id) = CurrentTestInfo.Fetcher();
        StepCollector.StartStep(id, "Given", "by hand", null, null);
        StepCollector.CompleteStep(id, passed: true);
        Ask(label);
        return Task.CompletedTask;
    }

    private Task When_a_call(string label)
    {
        Ask(label);
        return Task.CompletedTask;
    }

    [Scenario]
    public async Task With_no_decorator_the_fetcher_still_sets_the_phase()
    {
        await Runner.RunScenarioAsync(
            _ => Given_a_call("given"),
            _ => When_a_call("when"));

        Assert.Equal(["given=Setup", "when=Action"], _reads);
    }

    // A step attribute's keyword moves the phase and is not the framework's: the fetcher still has the next step to report (mut/m22).
    [Scenario]
    public async Task A_step_attribute_leaves_the_fetcher_setting_the_phase()
    {
        await Runner.RunScenarioAsync(
            _ => Given_a_step_attribute_and_a_call("given"),
            _ => When_a_call("when"));

        Assert.Equal(["given=Setup", "when=Action"], _reads);
    }
}
