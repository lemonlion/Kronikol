using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using Rev4;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;

[assembly: AssemblyFixture(typeof(Rev4.Bddfy.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace Rev4.Bddfy;

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
        Skeleton.Write("rev4-bddfy");
        return ValueTask.CompletedTask;
    }
}

public class Scenarios
{
    private static HttpClient NewClient(bool trackDuringAction = true) => new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringAction = trackDuringAction
    }) { InnerHandler = new Answer() });

    private readonly HttpClient _http = NewClient();
    private readonly HttpClient _noAction = NewClient(trackDuringAction: false);
    private async Task Call(string label) => (await _http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    private async Task GivenAsync(string l) => await Call(l);
    private void GivenSync(string l) => Call(l).GetAwaiter().GetResult();
    private async Task WhenAsync(string l) { await Task.Yield(); await Call(l + "-1"); await Task.Yield(); await Call(l + "-2"); }
    private async Task ThenAsync(string l) => await Call(l);
    private async Task WhenThrows(string l) { await Call(l); throw new InvalidOperationException("boom"); }
    private async void GivenAsyncVoid(string l) { await Task.Yield(); await Call(l); }
    private void TearDownNoAction(string l) => _noAction.GetAsync($"http://probe.local/{l}").GetAwaiter().GetResult();
    private void TearDownCall(string l) => Call(l).GetAwaiter().GetResult();
    private async Task<int> ThenGeneric(string l) { await Call(l); return 1; }

    [WhenStep] public async Task WovenWhenSub(string l) => await Call(l);
    [GivenStep] public async Task WovenGivenSub(string l) => await Call(l);
    private async Task WhenWithGivenSub(string l) { await Call(l + "-before"); await WovenGivenSub(l + "-sub"); await Call(l + "-after"); }

    [Fact]
    public void B01_Async_steps_and_a_tear_down()
    {
        this.Given(_ => GivenAsync("b01-given"))
            .When(_ => WhenAsync("b01-when"))
            .Then(_ => ThenAsync("b01-then"))
            .TearDownWith(_ => TearDownNoAction("b01-teardown-no-action-tracking"))
            .BDDfy();
    }

    [Fact]
    public void B02_Calls_after_the_story_in_the_test_body()
    {
        this.Given(_ => GivenSync("b02-given"))
            .When(_ => WhenAsync("b02-when"))
            .BDDfy();
        _noAction.GetAsync("http://probe.local/b02-after-story-no-action-tracking").GetAwaiter().GetResult();
        Call("b02-after-story").GetAwaiter().GetResult();
    }

    [Fact]
    public void B03_An_async_When_that_fails()
    {
        var ex = Record.Exception(() => this.Given(_ => GivenAsync("b03-given"))
            .When(_ => WhenThrows("b03-when"))
            .Then(_ => ThenAsync("b03-then"))
            .BDDfy());
        Assert.NotNull(ex);
    }

    [Fact]
    public void B04_An_async_void_step_and_a_sub_step_in_a_When()
    {
        this.Given(_ => GivenAsyncVoid("b04-given-void"))
            .When(_ => WhenWithGivenSub("b04-when"))
            .Then(_ => ThenGeneric("b04-then"))
            .BDDfy();
    }

    [Fact]
    public void B05_Async_void_support_off()
    {
        Configurator.AsyncVoidSupportEnabled = false;
        try
        {
            this.Given(_ => GivenSync("b05-given"))
                .When(_ => WhenAsync("b05-when"))
                .Then(_ => ThenAsync("b05-then"))
                .BDDfy();
            Task.Delay(100).GetAwaiter().GetResult();
        }
        finally { Configurator.AsyncVoidSupportEnabled = true; }
    }
}
