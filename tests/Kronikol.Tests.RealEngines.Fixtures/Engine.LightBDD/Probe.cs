using System.Net;
using Kronikol;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit3;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Core.Extensibility.Execution;
using LightBDD.Core.Execution;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;
using Xunit;
using Xunit.v3;

[assembly: TrackSteps]
[assembly: TestPipelineStartup(typeof(EngineFixtures.Scope))]

namespace EngineFixtures;

public class Scope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions
        {
            ReportsFolderPath = Path.Combine(AppContext.BaseDirectory, "Reports"),
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
        });
        configuration.ProgressNotifierConfiguration().Clear();
    }
}

public class Driver
{
    private sealed class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private readonly HttpClient _client = new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions
    {
        CallerName = "Tests",
        FixedNameForReceivingService = "Shop",
    }) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };

    public Task Post(string path) => _client.PostAsync(path, new StringContent("{}"));

    [Step] public async Task Reference_data_is_seeded() => await Post("seed");
    [Step] public async Task A_customer_is_registered() => await Post("customers");
    [Step] public async Task An_order_is_submitted() => await Post("orders");
    [Step] public async Task The_order_is_read_back() => await Post("orders/1");
    [Step] public async Task A_slow_background_check() { await Task.Delay(200); await Post("background"); }
    [Step] public async Task Seeding_fails() { await Post("seed"); throw new InvalidOperationException("seeding failed"); }
}

/// <summary>What a suite's seeding decorator looks like: a step method before the scenario's first step.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SeededAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;

    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await new Driver().Reference_data_is_seeded();
        await scenarioInvocation();
    }
}

/// <summary>The same, failing: the scenario's steps never run.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SeedingFailsAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;

    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await new Driver().Seeding_fails();
        await scenarioInvocation();
    }
}

public class Probes : FeatureFixture
{
    private readonly Driver _driver = new();

    private Task Given_a_registered_customer() => _driver.A_customer_is_registered();
    private Task When_the_customer_places_the_order() => _driver.An_order_is_submitted();
    private Task Then_the_order_is_confirmed() => _driver.The_order_is_read_back();

    public sealed record OrderDetails(string Customer, int Lines);

    private Task Given_a_customer_with_details(OrderDetails details) => _driver.A_customer_is_registered();

    [Scenario]
    public Task L5_a_step_with_an_object_parameter() => Runner.RunScenarioAsync(
        given => Given_a_customer_with_details(new OrderDetails("Ann", 2)),
        when => When_the_customer_places_the_order(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    public Task L0_control() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_the_customer_places_the_order(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    [Seeded]
    public Task L1_a_step_method_in_a_scenario_decorator() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_the_customer_places_the_order(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    [SeedingFails]
    public Task L9_a_step_method_fails_in_a_scenario_decorator() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_the_customer_places_the_order(),
        then => Then_the_order_is_confirmed());

    private async Task<CompositeStep> Given_a_composite_step_that_seeds_first()
    {
        await _driver.Reference_data_is_seeded();
        return CompositeStep.DefineNew().AddAsyncSteps(
            _ => Native_sub_step_one(),
            _ => Native_sub_step_two()).Build();
    }

    private async Task<CompositeStep> Given_a_composite_step_that_asserts_first()
    {
        await Task.Yield();
        Track.That(() => Assert.True(true));
        return CompositeStep.DefineNew().AddAsyncSteps(
            _ => Native_sub_step_one(),
            _ => Native_sub_step_two()).Build();
    }

    [Scenario]
    public Task L6_an_assertion_before_a_composite_steps_own_steps() => Runner.RunScenarioAsync(
        given => Given_a_composite_step_that_asserts_first(),
        then => Then_the_order_is_confirmed());

    private Task Native_sub_step_one() => _driver.A_customer_is_registered();
    private Task Native_sub_step_two() => _driver.An_order_is_submitted();

    [Scenario]
    public Task L2_a_step_method_before_a_composite_steps_own_steps() => Runner.RunScenarioAsync(
        given => Given_a_composite_step_that_seeds_first(),
        then => Then_the_order_is_confirmed());

    private Task<CompositeStep> Given_a_composite_step_whose_first_step_fails() => Task.FromResult(
        CompositeStep.DefineNew().AddAsyncSteps(
            _ => Native_sub_step_that_fails(),
            _ => Native_sub_step_two()).Build());

    private async Task Native_sub_step_that_fails()
    {
        await _driver.Post("payments");
        throw new InvalidOperationException("sub-step failed");
    }

    [Scenario]
    public Task L3_a_failing_composite_sub_step() => Runner.RunScenarioAsync(
        given => Given_a_composite_step_whose_first_step_fails(),
        then => Then_the_order_is_confirmed());

    private Task Plain_given() => _driver.Post("v1");
    private Task Plain_when() => _driver.Post("v2");
    private Task Plain_then() => _driver.Post("v3");
    private Task Plain_and() => _driver.Post("v4");
    private Task Plain_then_same_path() => _driver.Post("orders/1");
    private Task Plain_when_prefix() => _driver.Post("orders");

    [Scenario]
    public Task V1_plain_calls_no_step_methods() => Runner.RunScenarioAsync(
        given => Plain_given(), when => Plain_when(), then => Plain_then());

    [Scenario]
    public Task V2_plain_calls_with_a_fourth_step() => Runner.RunScenarioAsync(
        given => Plain_given(), when => Plain_when(), then => Plain_then(), and => Plain_and());

    [Scenario]
    public Task V3_plain_calls_on_the_control_paths() => Runner.RunScenarioAsync(
        given => Plain_given(), when => Plain_when_prefix(), then => Plain_then_same_path());

    [Scenario]
    public Task V4_step_methods_in_given_and_then_only() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(), then => Then_the_order_is_confirmed());

    private Task Given_a_step_method_is_left_running()
    {
        _ = _driver.A_slow_background_check();
        return _driver.Post("started");
    }

    [Scenario]
    public Task L4_a_step_method_left_running() => Runner.RunScenarioAsync(
        given => Given_a_step_method_is_left_running(),
        when => When_the_customer_places_the_order(),
        then => Then_the_order_is_confirmed());
}

public class ALongStepBeforeOneThatFails : FeatureFixture
{
    private readonly Driver _driver = new();

    private Task Given_a_step_whose_title_is_longer_than_any_bar_draws_it_whole_so_its_bar_cannot_be_matched_against_its_text_by_anyone() => _driver.Post("long");

    private async Task When_the_next_step_fails()
    {
        await _driver.Post("fails");
        throw new InvalidOperationException("it failed");
    }

    private Task Then_the_order_is_confirmed() => _driver.The_order_is_read_back();

    // A row that never ran has no id, and that must not cost the rows that ran theirs: the long step's bar cannot be read by its text.
    [Scenario]
    public Task M4_a_long_step_and_a_failing_step_with_one_after_it() => Runner.RunScenarioAsync(
        given => Given_a_step_whose_title_is_longer_than_any_bar_draws_it_whole_so_its_bar_cannot_be_matched_against_its_text_by_anyone(),
        when => When_the_next_step_fails(),
        then => Then_the_order_is_confirmed());
}
