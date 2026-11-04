using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
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
        if (Environment.GetEnvironmentVariable("PROBE_ASYNC_OFF") == "1")
            Configurator.AsyncVoidSupportEnabled = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Dump.Write(Environment.GetEnvironmentVariable("PROBE_ASYNC_OFF") == "1" ? "bddfy-asyncoff" : "bddfy");
        return ValueTask.CompletedTask;
    }
}

public class Base
{
    protected readonly Calls Active = new(
        new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));
}

public class Scenarios : Base
{
    public void Given_a_call(string label) => Active.Make(label).GetAwaiter().GetResult();
    public void When_a_call(string label) => Active.Make(label).GetAwaiter().GetResult();
    public void Then_a_call(string label) => Active.Make(label).GetAwaiter().GetResult();
    public void A_tear_down_call(string label) => Active.Make(label).GetAwaiter().GetResult();

    public async Task Given_an_async_call(string label) { await Task.Delay(40); await Active.Make(label); }
    public async Task When_an_async_call(string label) { await Task.Delay(10); await Active.Make(label); }
    public async Task Then_an_async_call(string label) { await Task.Delay(10); await Active.Make(label); }

    [Fact]
    public void B1_a_TearDownWith_step()
    {
        this.Given(x => x.Given_a_call("b1-given"))
            .When(x => x.When_a_call("b1-when"))
            .Then(x => x.Then_a_call("b1-then"))
            .TearDownWith(x => x.A_tear_down_call("b1-teardown"))
            .BDDfy();
    }

    [Fact]
    public async Task B2_three_async_steps_that_yield()
    {
        this.Given(x => x.Given_an_async_call("b2-given"))
            .When(x => x.When_an_async_call("b2-when"))
            .Then(x => x.Then_an_async_call("b2-then"))
            .BDDfy();
        await Task.Delay(300);
    }
}

public class ReflectiveStory : Base
{
    public void Setup() => Active.Make("b3-setup-method").GetAwaiter().GetResult();
    public void GivenACall() => Active.Make("b3-given").GetAwaiter().GetResult();
    public void WhenACall() => Active.Make("b3-when").GetAwaiter().GetResult();
    public void ThenACall() => Active.Make("b3-then").GetAwaiter().GetResult();
    public void TearDown() => Active.Make("b3-teardown-method").GetAwaiter().GetResult();

    [Fact]
    public void B3_a_reflective_story_with_Setup_and_TearDown() => this.BDDfy();
}
