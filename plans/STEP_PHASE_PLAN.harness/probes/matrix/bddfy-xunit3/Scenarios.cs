using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using xRetry.v3;
using Xunit;

[assembly: AssemblyFixture(typeof(PhaseProbe.Bddfy.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace PhaseProbe.Bddfy;

public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        Dump.Write("bddfy-xunit3");
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
        return ValueTask.CompletedTask;
    }
}

public class Scenarios
{
    private readonly Calls _calls = new(
        new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    private static readonly HttpClient SharedClient =
        new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });

    private Calls? _sharedActive;
    private Calls Active => _sharedActive ?? _calls;
    private void UseShared() => _sharedActive = new Calls(SharedClient, new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    public void Given_a_call(string label) => Active.Make(label).GetAwaiter().GetResult();
    public void A_teardown_call(string label) => Active.Make(label).GetAwaiter().GetResult();
    public void And_a_call(string label) => Active.Make(label).GetAwaiter().GetResult();
    public void When_a_call(string label) => Active.Make(label).GetAwaiter().GetResult();
    public void Then_a_call(string label) => Active.Make(label).GetAwaiter().GetResult();
    public void But_a_call(string label) => Active.Make(label).GetAwaiter().GetResult();

    public void Given_a_message(string label) => Active.Message(label);
    public void When_a_message(string label) => Active.Message(label);

    public async Task Given_an_async_call(string label) => await Active.Make(label);
    public async Task When_an_async_call(string label) { await Task.Yield(); await Active.Make(label); }

    [GivenStep] public async Task WovenGiven(string label) => await Active.Make(label);
    [WhenStep] public async Task WovenWhen(string label) => await Active.Make(label);

    public void When_a_step_calls_a_woven_Given(string label)
    {
        Active.Make($"{label}-before-sub").GetAwaiter().GetResult();
        WovenGiven($"{label}-in-sub").GetAwaiter().GetResult();
        Active.Make($"{label}-after-sub").GetAwaiter().GetResult();
    }

    public void Given_a_step_calls_a_woven_When(string label)
    {
        Active.Make($"{label}-before-sub").GetAwaiter().GetResult();
        WovenWhen($"{label}-in-sub").GetAwaiter().GetResult();
        Active.Make($"{label}-after-sub").GetAwaiter().GetResult();
    }

    public async Task When_an_async_step_calls_a_woven_Given(string label)
    {
        await Task.Yield();
        await Active.Make($"{label}-before-sub");
        await WovenGiven($"{label}-in-sub");
        await Active.Make($"{label}-after-sub");
    }

    [Fact]
    public void B01_plain_Given_And_When_And_Then_But()
    {
        this.Given(x => x.Given_a_call("b01-given"))
            .And(x => x.And_a_call("b01-and"))
            .When(x => x.When_a_call("b01-when"))
            .And(x => x.And_a_call("b01-when-and"))
            .Then(x => x.Then_a_call("b01-then"))
            .And(x => x.But_a_call("b01-then-but"))
            .BDDfy();
    }

    [Fact]
    public void B02_a_When_step_calls_a_woven_Given()
    {
        this.Given(x => x.Given_a_call("b02-given"))
            .When(x => x.When_a_step_calls_a_woven_Given("b02"))
            .Then(x => x.Then_a_call("b02-then"))
            .BDDfy();
    }

    [Fact]
    public void B03_a_Given_step_calls_a_woven_When()
    {
        this.Given(x => x.Given_a_step_calls_a_woven_When("b03"))
            .When(x => x.When_a_call("b03-when"))
            .BDDfy();
    }

    [Fact]
    public void B04_an_async_When_step_calls_a_woven_Given()
    {
        this.Given(x => x.Given_a_call("b04-given"))
            .When(x => x.When_an_async_step_calls_a_woven_Given("b04"))
            .Then(x => x.Then_a_call("b04-then"))
            .BDDfy();
    }

    [Fact]
    public void B05_async_steps_that_yield()
    {
        this.Given(x => x.Given_an_async_call("b05-given"))
            .When(x => x.When_an_async_call("b05-when"))
            .Then(x => x.Then_a_call("b05-then"))
            .BDDfy();
    }

    [Fact]
    public void B08_a_When_with_no_HTTP_call()
    {
        this.Given(x => x.Given_a_call("b08-given"))
            .When(x => x.When_a_message("b08-when-message"))
            .Then(x => x.Then_a_call("b08-then"))
            .BDDfy();
    }

    [Fact]
    public void B10_a_teardown_after_the_boundary()
    {
        this.Given(x => x.Given_a_call("b10-given"))
            .When(x => x.When_a_call("b10-when"))
            .TearDownWith(x => x.A_teardown_call("b10-teardown"))
            .BDDfy();
    }

    [Fact]
    public void B11_async_steps_with_AsyncVoidSupport_off()
    {
        TestStack.BDDfy.Configuration.Configurator.AsyncVoidSupportEnabled = false;
        try
        {
            this.Given(x => x.Given_an_async_call("b11-given"))
                .When(x => x.When_an_async_call("b11-when"))
                .Then(x => x.Then_a_call("b11-then"))
                .BDDfy();
            // BDDfy waits on nothing here, so let the async steps finish before the test ends.
            Task.Delay(200).GetAwaiter().GetResult();
        }
        finally
        {
            TestStack.BDDfy.Configuration.Configurator.AsyncVoidSupportEnabled = true;
        }
    }

    [Fact]
    public void B09_a_Given_with_no_HTTP_call()
    {
        this.Given(x => x.Given_a_message("b09-given-message"))
            .When(x => x.When_a_call("b09-when"))
            .BDDfy();
    }

    [Fact]
    public void B06_first_scenario_on_a_shared_handler()
    {
        UseShared();
        this.Given(x => x.Given_a_call("b06-given")).When(x => x.When_a_call("b06-when")).BDDfy();
    }

    [Fact]
    public void B07_second_scenario_on_a_shared_handler()
    {
        UseShared();
        this.Given(x => x.Given_a_call("b07-given")).When(x => x.When_a_call("b07-when")).BDDfy();
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Attempts = new();

    public void Then_the_first_attempt_fails(int attempt)
    {
        if (attempt == 1) throw new InvalidOperationException("the first attempt fails");
    }

    [RetryFact(3)]
    public void B90_retried_with_a_handler_per_attempt()
    {
        var n = Attempts.AddOrUpdate(nameof(B90_retried_with_a_handler_per_attempt), 1, (_, c) => c + 1);
        string given = $"b90-a{n}-given", when = $"b90-a{n}-when", then = $"b90-a{n}-then";
        this.Given(x => x.Given_a_call(given))
            .When(x => x.When_a_call(when))
            .Then(x => x.Then_the_first_attempt_fails(n))
            .And(x => x.And_a_call(then))
            .BDDfy();
    }

    [RetryFact(3)]
    public void B91_retried_with_a_shared_handler()
    {
        UseShared();
        var n = Attempts.AddOrUpdate(nameof(B91_retried_with_a_shared_handler), 1, (_, c) => c + 1);
        string given = $"b91-a{n}-given", when = $"b91-a{n}-when", then = $"b91-a{n}-then";
        this.Given(x => x.Given_a_call(given))
            .When(x => x.When_a_call(when))
            .Then(x => x.Then_the_first_attempt_fails(n))
            .And(x => x.And_a_call(then))
            .BDDfy();
    }
}
