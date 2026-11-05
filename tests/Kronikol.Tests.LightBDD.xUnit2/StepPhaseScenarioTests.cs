using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit2;
using Kronikol.Reports;
using Kronikol.Tests.LightBDD.xUnit2;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Core.ExecutionContext;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit2;

[assembly: StepPhaseScope]

namespace Kronikol.Tests.LightBDD.xUnit2;

internal sealed class StepPhaseScopeAttribute : LightBddScopeAttribute
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        // A suite can add step types; "but" is not one of LightBDD's own.
        configuration.StepTypeConfiguration().UpdatePredefinedStepTypes("given", "when", "then", "and", "but", "setup");
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Step phases" });
    }
}

/// <summary>
/// #224 on LightBDD, on real scenarios (plans/STEP_PHASE_PLAN.md 5.4). A LightBDD keyword is upper case, and each step runs in
/// an async frame of its own, so a step's body reads its type's phase, and an <c>AND</c> reads the test's. Outside a scenario the
/// decorator's test id is null, so nothing less than a scenario reaches this.
/// </summary>
public class StepPhaseScenarioTests : FeatureFixture
{
    private readonly List<string> _reads = [];
    private string? _id;

    private Task At(string label)
    {
        _id ??= Id;
        lock (_reads)
            _reads.Add($"{label}={TestPhaseContext.Current}");
        return Task.CompletedTask;
    }

    private static string Id => ScenarioExecutionContext.CurrentScenario.Info.RuntimeId.ToString();

    private async Task Given_a_read(string label) { await Task.Yield(); await At(label); }
    private async Task And_a_read(string label) { await Task.Yield(); await At(label); }
    private async Task When_a_read(string label) { await Task.Yield(); await At(label); }
    private async Task Then_a_read(string label) { await Task.Yield(); await At(label); }
    private async Task But_a_read(string label) { await Task.Yield(); await At(label); }
    private async Task Setup_a_read(string label) { await Task.Yield(); await At(label); }
    private async Task A_read(string label) { await Task.Yield(); await At(label); }

    private async Task<CompositeStep> When_holding_a_Given(string label)
    {
        await At($"{label}-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => A_read($"{label}-first-untyped"),
                _ => Given_a_read($"{label}-given"),
                _ => A_read($"{label}-untyped-after-given"))
            .Build();
    }

    private async Task<CompositeStep> Given_holding_a_When(string label)
    {
        await At($"{label}-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => When_a_read($"{label}-when"),
                _ => A_read($"{label}-untyped-after-when"))
            .Build();
    }

    private async Task<CompositeStep> And_holding_a_Given(string label)
    {
        await At($"{label}-body");
        return CompositeStep.DefineNew().AddAsyncSteps(_ => Given_a_read($"{label}-given")).Build();
    }

    private Task A_bypassed_sub_step()
    {
        StepExecution.Current.Bypass("bypassed");
        return Task.CompletedTask;
    }

    private async Task<CompositeStep> Given_holding_a_bypassed_step(string label)
    {
        await At($"{label}-body");
        return CompositeStep.DefineNew().AddAsyncSteps(
                _ => A_bypassed_sub_step(),
                _ => When_a_read($"{label}-when"),
                _ => A_read($"{label}-untyped"))
            .Build();
    }

    private async Task When_holding_a_step_attribute(string label)
    {
        await At($"{label}-before");
        StepCollector.StartStep(Id, "Given", "by hand", null, null);
        await At($"{label}-in");
        StepCollector.CompleteStep(Id, passed: true);
        await At($"{label}-after");
    }

    private async Task And_asking_the_fetcher(string label)
    {
        new LightBddTestTrackingMessageHandlerOptions().CurrentStepTypeFetcher!();
        await At(label);
    }

    private async Task When_asking_the_fetcher(string label)
    {
        new LightBddTestTrackingMessageHandlerOptions().CurrentStepTypeFetcher!();
        await At(label);
    }

    // A step with no type says nothing, so the step attributes under it decide what the calls read, with the fetcher asked for each call.
    private async Task A_step_holding_a_Given_then_a_When(string label)
    {
        StepCollector.StartStep(Id, "Given", "by hand", null, null);
        new LightBddTestTrackingMessageHandlerOptions().CurrentStepTypeFetcher!();
        await At($"{label}-given");
        StepCollector.CompleteStep(Id, passed: true);
        StepCollector.StartStep(Id, "When", "by hand", null, null);
        new LightBddTestTrackingMessageHandlerOptions().CurrentStepTypeFetcher!();
        await At($"{label}-when");
        StepCollector.CompleteStep(Id, passed: true);
    }

    private void Assert_reads(params string[] expected) => Assert.Equal(expected, _reads);

    [Scenario]
    public async Task A_typed_steps_body_reads_its_phase_and_AND_and_a_suites_BUT_continue()
    {
        await Runner.RunScenarioAsync(
            _ => Given_a_read("given"),
            _ => And_a_read("given-and"),
            _ => But_a_read("given-but"),
            _ => When_a_read("when"),
            _ => And_a_read("when-and"),
            _ => But_a_read("when-but"),
            _ => Then_a_read("then"),
            _ => And_a_read("then-and"),
            _ => But_a_read("then-but"));

        Assert_reads("given=Setup", "given-and=Setup", "given-but=Setup", "when=Action", "when-and=Action", "when-but=Action",
            "then=Action", "then-and=Action", "then-but=Action");
    }

    // L12: before the action began and after it.
    // review4/F10: the fetcher wrote the type's Unknown over what the step attribute had just said.
    [Scenario]
    public async Task A_step_with_no_type_leaves_its_step_attributes_to_decide()
    {
        await Runner.RunScenarioAsync(_ => A_step_holding_a_Given_then_a_When("in"));

        Assert_reads("in-given=Setup", "in-when=Action");
    }

    [Scenario]
    public async Task A_step_with_no_type_reads_Unknown()
    {
        await Runner.RunScenarioAsync(
            _ => A_read("first"),
            _ => Given_a_read("given"),
            _ => A_read("after-given"),
            _ => When_a_read("when"),
            _ => A_read("after-when"));

        Assert_reads("first=Unknown", "given=Setup", "after-given=Unknown", "when=Action", "after-when=Unknown");
    }

    // L13.
    [Scenario]
    public async Task LightBDDs_own_setup_type_reads_Setup()
    {
        await Runner.RunScenarioAsync(
            _ => Setup_a_read("setup"),
            _ => When_a_read("when"));

        Assert_reads("setup=Setup", "when=Action");
    }

    // L02.
    [Scenario]
    public async Task A_typed_sub_step_under_a_When_is_a_label()
    {
        await Runner.RunScenarioAsync(
            _ => Given_a_read("given"),
            _ => When_holding_a_Given("when"),
            _ => Then_a_read("then"),
            _ => And_a_read("then-and"));

        Assert_reads("given=Setup", "when-body=Action", "when-first-untyped=Action", "when-given=Action", "when-untyped-after-given=Action",
            "then=Action", "then-and=Action");
    }

    // L03.
    [Scenario]
    public async Task A_typed_sub_step_under_a_Given_is_a_label()
    {
        await Runner.RunScenarioAsync(
            _ => Given_holding_a_When("given"),
            _ => And_a_read("given-and"),
            _ => When_a_read("when"));

        Assert_reads("given-body=Setup", "given-when=Setup", "given-untyped-after-when=Setup", "given-and=Setup", "when=Action");
    }

    // L04, by hand, as this project weaves nothing: a step attribute's call reaches StepCollector the same way.
    [Scenario]
    public async Task A_step_attribute_under_a_typed_step_is_a_label()
    {
        await Runner.RunScenarioAsync(
            _ => Given_a_read("given"),
            _ => When_holding_a_step_attribute("when"));

        Assert_reads("given=Setup", "when-before=Action", "when-in=Action", "when-after=Action");
    }

    // L14 and F15: a bypassed sub-step reads Failed, at 5.28.2 and after (plans/STEP_PHASE_PLAN.md 11), and closes itself alone.
    [Scenario]
    public async Task A_bypassed_sub_step_closes_itself_and_not_its_parent_and_the_siblings_after_it_stay_labels()
    {
        await Runner.RunScenarioAsync(
            _ => Given_holding_a_bypassed_step("given"),
            _ => And_a_read("given-and"),
            _ => When_a_read("when"));

        Assert_reads("given-body=Setup", "given-when=Setup", "given-untyped=Setup", "given-and=Setup", "when=Action");

        var steps = StepCollector.GetSteps(_id);
        Assert.Equal(3, steps.Length);
        Assert.Equal(ExecutionResult.Passed, steps[0].Status);
        Assert.Equal(3, steps[0].SubSteps!.Length);
        Assert.Equal(ExecutionResult.Failed, steps[0].SubSteps![0].Status);
        Assert.False(StepCollector.HasActiveStep(_id));
    }

    private async Task When_it_fails_with_a_step_left_open(string label)
    {
        await At(label);
        StepCollector.StartStep(Id, null, $"{label}-left-open", null, null);
        throw new InvalidOperationException(label);
    }

    // review1/L15: a step left open under it must not take the close, or the failing step would never reach the step list, with what it held.
    [Scenario]
    public async Task A_failing_step_with_a_step_left_open_under_it_reads_Failed_and_keeps_it()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Runner.RunScenarioAsync(_ => When_it_fails_with_a_step_left_open("when")));

        var step = StepCollector.GetSteps(_id).Single();
        Assert.Equal(ExecutionResult.Failed, step.Status);
        Assert.Single(step.SubSteps!);
    }

    // L15.
    [Scenario]
    public async Task An_AND_with_nothing_before_it_says_nothing_so_a_typed_sub_step_under_it_decides()
    {
        await Runner.RunScenarioAsync(
            _ => And_holding_a_Given("and"),
            _ => When_a_read("when"));

        Assert_reads("and-body=Unknown", "and-given=Setup", "when=Action");
    }

    // L16.
    [Scenario]
    public async Task An_AND_first_then_a_When()
    {
        await Runner.RunScenarioAsync(
            _ => And_a_read("and"),
            _ => When_a_read("when"));

        Assert_reads("and=Unknown", "when=Action");
    }

    // The fetcher's write is for a suite with no decorator: where one runs, it writes only before any step has decided.
    [Scenario]
    public async Task The_fetcher_writes_before_any_step_has_decided()
    {
        await Runner.RunScenarioAsync(_ => And_asking_the_fetcher("and"));

        Assert_reads("and=Setup");
    }

    [Scenario]
    public async Task The_fetcher_writes_nothing_once_a_step_has_decided()
    {
        await Runner.RunScenarioAsync(
            _ => When_a_read("when"),
            _ => And_asking_the_fetcher("and"));

        Assert_reads("when=Action", "and=Action");
    }

    [Scenario]
    public async Task The_fetcher_leaves_a_typed_steps_own_phase()
    {
        await Runner.RunScenarioAsync(
            _ => Given_a_read("given"),
            _ => When_asking_the_fetcher("when"));

        Assert_reads("given=Setup", "when=Action");
    }
}
