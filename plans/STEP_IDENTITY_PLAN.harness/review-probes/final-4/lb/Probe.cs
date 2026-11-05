using System.Net;
using Kronikol;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit3;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Core.Execution;
using LightBDD.Core.Extensibility.Execution;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;
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

    private readonly HttpClient _client;

    public Driver(Func<string?>? fetcher = null)
    {
        var options = new LightBddTestTrackingMessageHandlerOptions { CallerName = "Tests", FixedNameForReceivingService = "Shop" };
        if (fetcher is not null)
            options = options with { CurrentStepTypeFetcher = fetcher };
        _client = new HttpClient(new TestTrackingMessageHandler(options) { InnerHandler = new Stub() }) { BaseAddress = new Uri("http://shop.test/") };
    }

    public Task Post(string path) => _client.PostAsync(path, new StringContent("{}"));

    [Step] public async Task Seed() => await Post("seed");
    [Step] public async Task Clean() => await Post("clean");
    [Step] public async Task Customer() => await Post("customers");
    [Step] public async Task Order() => await Post("orders");
    [Step] public async Task Read_back() => await Post("orders/1");
    [Step] public async Task Slow_then_call() { await Task.Delay(250); await Post("slow"); }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class CallsBeforeAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await new Driver().Post("decorator-before");
        await scenarioInvocation();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class CallsAfterAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await scenarioInvocation();
        await new Driver().Post("decorator-after");
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class CleansUpAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        try { await scenarioInvocation(); }
        finally { await new Driver().Clean(); }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RetriesOnceAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await scenarioInvocation();
        await scenarioInvocation();
    }
}

/// <summary>A suite's time limit: the scenario ends while its step carries on.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TimeBoxAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        var running = scenarioInvocation();
        var first = await Task.WhenAny(running, Task.Delay(50));
        if (first != running)
            _ = running.ContinueWith(t => _ = t.Exception);
    }
}

public class Decorators : FeatureFixture
{
    private readonly Driver _driver = new();

    private Task Given_a_customer() => _driver.Customer();
    private Task When_an_order() => _driver.Order();
    private Task Then_read_back() => _driver.Read_back();
    private async Task When_it_fails() { await _driver.Post("fails"); throw new InvalidOperationException("it failed"); }
    private async Task When_it_is_slow() { await Task.Delay(200); await _driver.Post("slow-step"); await Task.Delay(100); }

    [Scenario, CallsBefore]
    public Task D1_a_plain_call_before_the_steps() => Runner.RunScenarioAsync(
        given => Given_a_customer(), when => When_an_order(), then => Then_read_back());

    [Scenario, CallsAfter]
    public Task D2_a_plain_call_after_the_steps() => Runner.RunScenarioAsync(
        given => Given_a_customer(), when => When_an_order(), then => Then_read_back());

    [Scenario, CleansUp]
    public Task D3_a_step_method_after_the_steps() => Runner.RunScenarioAsync(
        given => Given_a_customer(), when => When_an_order(), then => Then_read_back());

    [Scenario, CleansUp]
    public Task D4_a_step_method_after_a_failing_step() => Runner.RunScenarioAsync(
        given => Given_a_customer(), when => When_it_fails(), then => Then_read_back());

    [Scenario, RetriesOnce]
    public Task D5_a_decorator_runs_the_steps_twice() => Runner.RunScenarioAsync(
        given => Given_a_customer(), when => When_an_order(), then => Then_read_back());

    [Scenario, TimeBox]
    public Task D6_a_time_limit_ends_the_scenario_with_its_step_running() => Runner.RunScenarioAsync(
        given => Given_a_customer(), when => When_it_is_slow());

    [Scenario, CallsBefore, CallsAfter]
    public Task D7_calls_either_side() => Runner.RunScenarioAsync(
        when => When_an_order());

    [Scenario, CallsAfter]
    public Task D8_a_plain_call_after_a_failing_step_is_not_reached() => Runner.RunScenarioAsync(
        when => When_it_fails(), then => Then_read_back());
}

public class Fetchers : FeatureFixture
{
    private Task Given_a_call_through_a_fetcher_that_throws() =>
        new Driver(() => throw new TimeoutException("a consumer's fetcher threw")).Post("through-throwing-fetcher");

    private Task Then_read_back() => new Driver().Read_back();

    [Scenario]
    public Task F1_a_consumers_step_type_fetcher_throws() => Runner.RunScenarioAsync(
        given => Given_a_call_through_a_fetcher_that_throws(), then => Then_read_back());
}

/// <summary>Many scenarios whose last call is the last thing their last step does, so a clock's margin can show.</summary>
public class LastCalls : FeatureFixture
{
    private readonly Driver _driver = new();
    private Task Given_a_customer() => _driver.Customer();
    private Task Then_read_back() => _driver.Read_back();

    [Scenario]
    [Xunit.InlineData(1)] [Xunit.InlineData(2)] [Xunit.InlineData(3)] [Xunit.InlineData(4)] [Xunit.InlineData(5)]
    [Xunit.InlineData(6)] [Xunit.InlineData(7)] [Xunit.InlineData(8)] [Xunit.InlineData(9)] [Xunit.InlineData(10)]
    public Task N1_the_last_call_ends_the_last_step(int n) => Runner.RunScenarioAsync(
        given => Given_a_customer(), then => Then_read_back());

    [Scenario, CallsAfter]
    [Xunit.InlineData(1)] [Xunit.InlineData(2)] [Xunit.InlineData(3)] [Xunit.InlineData(4)] [Xunit.InlineData(5)]
    [Xunit.InlineData(6)] [Xunit.InlineData(7)] [Xunit.InlineData(8)] [Xunit.InlineData(9)] [Xunit.InlineData(10)]
    public Task N2_the_last_call_is_a_decorators(int n) => Runner.RunScenarioAsync(
        given => Given_a_customer(), then => Then_read_back());
}

/// <summary>A decorator's call through a handler past its Act, which asks for no step type, so 2d9c88c3 runs it too.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CallsAfterThroughSharedAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await scenarioInvocation();
        await SharedCalls.Shared.Post("shared-decorator-after");
    }
}

/// <summary>A step decorator of a suite's own, calling after its step.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StepCallsAfterAttribute : Attribute, IStepDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IStep step, Func<Task> stepInvocation)
    {
        await stepInvocation();
        await SharedCalls.Shared.Post("step-decorator-after");
    }
}

public class SharedCalls : FeatureFixture
{
    public static readonly Driver Shared = new();
    private Task Given_a_customer() => Shared.Customer();
    private Task When_an_order() => Shared.Order();
    private Task Then_read_back() => Shared.Read_back();
    [StepCallsAfter] private Task Then_a_decorated_step() => Shared.Post("decorated-step");

    [Scenario, CallsAfterThroughShared]
    [Xunit.InlineData(1)] [Xunit.InlineData(2)] [Xunit.InlineData(3)] [Xunit.InlineData(4)] [Xunit.InlineData(5)]
    public Task N3_a_decorators_call_past_the_act(int n) => Runner.RunScenarioAsync(
        given => Given_a_customer(), when => When_an_order(), then => Then_read_back());

    [Scenario]
    [Xunit.InlineData(1)] [Xunit.InlineData(2)] [Xunit.InlineData(3)] [Xunit.InlineData(4)] [Xunit.InlineData(5)]
    public Task N4_a_step_decorators_call_after_the_last_step(int n) => Runner.RunScenarioAsync(
        given => Given_a_customer(), when => When_an_order(), then => Then_a_decorated_step());
}

/// <summary>A test's run of the report after a clock has had time to wander.</summary>
public class LateInTheRun : FeatureFixture
{
    private readonly Driver _driver = new();
    private async Task Given_the_run_has_gone_on() { await Task.Delay(TimeSpan.FromSeconds(double.Parse(Environment.GetEnvironmentVariable("LB_WAIT") ?? "0"))); }
    private Task Then_read_back() => _driver.Read_back();

    [Scenario, CallsAfter]
    public Task Z1_a_decorators_call_late_in_the_run() => Runner.RunScenarioAsync(
        given => Given_the_run_has_gone_on(), then => Then_read_back());
}
