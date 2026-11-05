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
[assembly: TestPipelineStartup(typeof(Lb7.Scope))]

namespace Lb7;

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
        if (Environment.GetEnvironmentVariable("NOBARS") == "1") StepCollector.Options.ShowStepDelimiters = false;
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
    private static readonly TaskCompletionSource Never = new();
    [Step] public async Task A_step_that_never_ends() { await Post("open"); await Never.Task; }
    [Step] public async Task Then_the_order_is_confirmed_by_the_audit() { await Post("audit"); await Never.Task; }
    [Step] public async Task A_slow_background_check() { await Task.Delay(100); await Post("background"); }
}

/// <summary>A retry a suite writes as a scenario decorator: runs the scenario again when it fails.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RetriedAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;

    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        try { await scenarioInvocation(); }
        catch (Exception) { await scenarioInvocation(); }
    }
}

/// <summary>A step method after the scenario, as a suite's cleaning decorator has it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CleanedAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;

    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await scenarioInvocation();
        await new Driver().The_order_is_read_back();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class LeftOpenAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await scenarioInvocation();
        _ = new Driver().A_step_that_never_ends();
        await Task.Delay(30);
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class LeftOpenAfterFailureAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        try { await scenarioInvocation(); }
        finally
        {
            _ = new Driver().A_step_that_never_ends();
            await Task.Delay(30);
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditLeftOpenAfterFailureAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        try { await scenarioInvocation(); }
        finally
        {
            _ = new Driver().Then_the_order_is_confirmed_by_the_audit();
            await Task.Delay(30);
        }
    }
}

public class Probes : FeatureFixture
{
    private readonly Driver _driver = new();
    private static int _flaky;

    private Task Given_a_registered_customer() => _driver.A_customer_is_registered();
    private Task When_the_customer_places_the_order() => _driver.An_order_is_submitted();
    private Task Then_the_order_is_confirmed() => _driver.The_order_is_read_back();

    private async Task When_it_fails_the_first_time()
    {
        await _driver.Post("flaky");
        if (Interlocked.Increment(ref _flaky) == 1) throw new InvalidOperationException("first time");
    }

    private async Task Given_a_step_left_running() { _ = _driver.A_slow_background_check(); await _driver.Post("started"); }
    private async Task When_it_takes_a_while() { await _driver.Post("while-1"); await Task.Delay(200); await _driver.Post("while-2"); }
    private async Task When_it_fails() { await _driver.Post("fails"); throw new InvalidOperationException("fails"); }

    [Scenario]
    public Task M8_a_step_left_running() => Runner.RunScenarioAsync(
        given => Given_a_step_left_running(),
        when => When_it_takes_a_while(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    [LeftOpen]
    public Task M9_a_decorator_leaves_a_step_open() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    [LeftOpenAfterFailure]
    public Task M10_a_decorator_leaves_a_step_open_after_a_failure() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_it_fails(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    [AuditLeftOpenAfterFailure]
    public Task M11_as_M10_and_its_bar_holds_the_next_rows_text() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_it_fails(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    public Task M0_control() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_the_customer_places_the_order(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    [Retried]
    public Task M1_a_scenario_decorator_runs_it_again() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_it_fails_the_first_time(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    [Cleaned]
    public Task M2_a_step_method_after_the_scenario() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        then => Then_the_order_is_confirmed());

    private async Task When_the_scenario_ignores_itself()
    {
        await _driver.Post("ignored");
        StepExecution.Current.IgnoreScenario("not today");
    }

    [Scenario]
    public Task M3_a_step_ignores_the_scenario() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_the_scenario_ignores_itself(),
        then => Then_the_order_is_confirmed());

    private async Task When_the_step_bypasses_itself()
    {
        await _driver.Post("bypassed");
        StepExecution.Current.Bypass("not today");
        await _driver.Post("after-bypass");
    }

    [Scenario]
    public Task M4_a_step_bypasses_itself() => Runner.RunScenarioAsync(
        given => Given_a_registered_customer(),
        when => When_the_step_bypasses_itself(),
        then => Then_the_order_is_confirmed());

    private async Task Given_two_step_methods_at_once() => await Task.WhenAll(_driver.A_customer_is_registered(), _driver.An_order_is_submitted());

    [Scenario]
    public Task M5_two_step_methods_at_once() => Runner.RunScenarioAsync(
        given => Given_two_step_methods_at_once(),
        then => Then_the_order_is_confirmed());

    private Task<CompositeStep> Given_a_composite_with_inner_composite() => Task.FromResult(
        CompositeStep.DefineNew().AddAsyncSteps(
            _ => Given_a_composite_step_that_seeds_first(),
            _ => Native_sub_step_two()).Build());

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
    public Task M6_nested_composites() => Runner.RunScenarioAsync(
        given => Given_a_composite_with_inner_composite(),
        then => Then_the_order_is_confirmed());

    [Scenario]
    public async Task M7_two_runs_in_one_test()
    {
        await Runner.RunScenarioAsync(given => Given_a_registered_customer(), then => Then_the_order_is_confirmed());
        await Runner.RunScenarioAsync(when => When_the_customer_places_the_order());
    }
}
