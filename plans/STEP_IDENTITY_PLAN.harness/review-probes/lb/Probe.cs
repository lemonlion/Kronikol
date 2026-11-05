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
using Xunit.v3;

[assembly: TrackSteps]
[assembly: TestPipelineStartup(typeof(HookSteps.LightBdd.Scope))]

namespace HookSteps.LightBdd;

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
    [Step] public async Task Seeding_fails() { await Post("seed"); throw new InvalidOperationException("seeding failed"); }
    [Step] public async Task A_slow_inner_check() { await Task.Delay(200); await Post("background"); }
    [Step] public Task A_forwarding_check() => A_slow_inner_check();
    [Step] public async Task An_order_is_submitted_slowly() { await Task.Delay(400); await Post("orders"); }
    [Step] public async Task A_slow_background_check() { await Task.Delay(200); await Post("background"); }
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
    [Scenario]
    [SeedingFails]
    public Task L5_a_step_method_fails_in_a_scenario_decorator() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_the_customer_places_the_order(),
        then => Then_the_order_is_confirmed());

    private Task Given_a_forwarder_is_left_running()
    {
        _ = _driver.A_forwarding_check();
        return _driver.Post("started");
    }

    private async Task When_the_order_takes_a_while_and_is_read_back()
    {
        await _driver.An_order_is_submitted_slowly();
        await _driver.The_order_is_read_back();
    }

    [Scenario]
    public Task L6_a_forwarding_step_method_left_running() => Runner.RunScenarioAsync(
        given => Given_a_forwarder_is_left_running(),
        when => When_the_order_takes_a_while_and_is_read_back(),
        then => Then_the_order_is_confirmed());

    private readonly Driver _driver = new();

    private Task Given_a_registered_customer() => _driver.A_customer_is_registered();
    private Task When_the_customer_places_the_order() => _driver.An_order_is_submitted();
    private Task Then_the_order_is_confirmed() => _driver.The_order_is_read_back();

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

    private async Task<CompositeStep> Given_a_composite_step_that_seeds_first()
    {
        await _driver.Reference_data_is_seeded();
        return CompositeStep.DefineNew().AddAsyncSteps(
            _ => Native_sub_step_one(),
            _ => Native_sub_step_two()).Build();
    }

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
