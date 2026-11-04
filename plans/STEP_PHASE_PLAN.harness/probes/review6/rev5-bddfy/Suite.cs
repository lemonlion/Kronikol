using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using Rev5;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;

[assembly: AssemblyFixture(typeof(Rev5.Bddfy.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace Rev5.Bddfy;

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
        Skeleton.Write("rev5-bddfy");
        return ValueTask.CompletedTask;
    }
}

public class Helpers
{
    public static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true) => new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = trackDuringSetup,
        TrackDuringAction = trackDuringAction
    }) { InnerHandler = new Answer() });
}

/// <summary>A class fixture has no xUnit test: BDDfy's executor gets no id. A story seeds under a scope, its client not tracking setup.</summary>
public class SeedFixture : IAsyncLifetime
{
    private readonly HttpClient _noSetup = Helpers.NewClient(trackDuringSetup: false);
    private readonly HttpClient _noAction = Helpers.NewClient(trackDuringAction: false);
    public static TestPhase PhaseInGiven, PhaseInWhen;

    private void GivenSeed()
    {
        PhaseInGiven = TestPhaseContext.Current;
        _noSetup.GetAsync("http://probe.local/f01-seed-nosetup").GetAwaiter().GetResult();
    }

    private void WhenWarm()
    {
        PhaseInWhen = TestPhaseContext.Current;
        _noAction.GetAsync("http://probe.local/f01-warm-noaction").GetAwaiter().GetResult();
    }

    public ValueTask InitializeAsync()
    {
        using (TestIdentityScope.Begin("F01 fixture story", "f01-fixture-story"))
        {
            this.Given(_ => GivenSeed()).When(_ => WhenWarm()).BDDfy<SeedFixture>("seed story");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class Stories
{
    protected readonly HttpClient Http = Helpers.NewClient();
    protected readonly HttpClient NoSetup = Helpers.NewClient(trackDuringSetup: false);
    protected readonly HttpClient NoAction = Helpers.NewClient(trackDuringAction: false);
    protected async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    protected void CallSync(string label) => Call(label).GetAwaiter().GetResult();

    protected async Task GivenAsync(string l) => await Call(l);
    protected void GivenSync(string l) => CallSync(l);
    protected async Task WhenAsync(string l) { await Task.Yield(); await Call(l); }
    protected void WhenSync(string l) => CallSync(l);
    protected async Task ThenAsync(string l) => await Call(l);
    protected void ThenSync(string l) => CallSync(l);
    protected async Task WhenThrows(string l) { await Call(l); throw new InvalidOperationException("boom"); }
    protected void GivenThrowsSync(string l) { CallSync(l); throw new InvalidOperationException("boom"); }
    protected void TearDownQuiet(string l)
    {
        NoSetup.GetAsync($"http://probe.local/{l}-nosetup").GetAwaiter().GetResult();
        NoAction.GetAsync($"http://probe.local/{l}-noaction").GetAwaiter().GetResult();
    }
    protected void MarkingWhen(string l) { TrackingDiagramOverride.StartAction(); CallSync(l); }
    protected void MarkingThen(string l) { TrackingDiagramOverride.StartAction(); CallSync(l); }
    protected async ValueTask WhenValueTask(string l) { await Task.Yield(); await Call(l); }

    [ThenStep] public async Task WovenHealthCheck(string l) => await Call(l);
    [WhenStep] public async Task WovenLogin(string l) => await Call(l);
    [GivenStep] public async Task WovenGiven(string l) => await Call(l);
    [GivenStep] public void WovenGivenSync(string l) => CallSync(l);
    [WhenStep] public async Task WovenWhen(string l) => await Call(l);
    [Step] public async Task WovenPlain(string l) => await Call(l);

    protected async Task WhenWithGivenSub(string l) { await Call(l + "-before"); await WovenGiven(l + "-sub"); await Call(l + "-after"); }
    protected async Task WhenThrowsAfterSub(string l) { await Call(l + "-before"); await WovenGiven(l + "-sub"); throw new InvalidOperationException("boom"); }
    protected void WhenSyncWithGivenSub(string l) { CallSync(l + "-before"); WovenGivenSync(l + "-sub"); CallSync(l + "-after"); }

    [Fact]
    public void B02_A_woven_Then_runs_before_the_story()
    {
        WovenHealthCheck("b02-health").GetAwaiter().GetResult();
        this.Given(_ => GivenSync("b02-given"))
            .When(_ => WhenSync("b02-when"))
            .Then(_ => ThenSync("b02-then"))
            .BDDfy();
    }

    [Fact]
    public void B03_A_woven_When_runs_before_the_story()
    {
        WovenLogin("b03-login").GetAwaiter().GetResult();
        this.Given(_ => GivenAsync("b03-given"))
            .When(_ => WhenAsync("b03-when"))
            .Then(_ => ThenAsync("b03-then"))
            .BDDfy();
    }

    [Fact]
    public void B04_Two_stories_in_one_test()
    {
        this.Given(_ => GivenSync("b04-given1")).When(_ => WhenSync("b04-when1")).Then(_ => ThenSync("b04-then1")).BDDfy();
        this.Given(_ => GivenSync("b04-given2")).When(_ => WhenSync("b04-when2")).Then(_ => ThenSync("b04-then2")).BDDfy();
    }

    public class ParallelStory(HttpClient http, string name)
    {
        private async Task Call(string l) => (await http.GetAsync($"http://probe.local/{name}-{l}")).EnsureSuccessStatusCode();
        private async Task GivenSeed() { await Call("given1"); await Task.Delay(3); await Call("given2"); }
        private async Task WhenAct() { await Call("when1"); await Task.Delay(3); await Call("when2"); }
        private async Task ThenCheck() { await Call("then1"); await Task.Delay(3); await Call("then2"); }
        public void Run() => this.Given(_ => GivenSeed()).When(_ => WhenAct()).Then(_ => ThenCheck()).BDDfy();
    }

    [Fact]
    public void B05_Parallel_stories_in_one_test()
    {
        Task.WaitAll(
            Task.Run(() => new ParallelStory(Http, "b05a").Run()),
            Task.Run(() => new ParallelStory(Http, "b05b").Run()));
        CallSync("b05-after");
    }

    [Fact]
    public void B15_A_woven_Given_before_a_story_with_a_tear_down()
    {
        WovenGiven("b15-woven-given").GetAwaiter().GetResult();
        this.When(_ => WhenSync("b15-when"))
            .Then(_ => ThenSync("b15-then"))
            .TearDownWith(_ => TearDownQuiet("b15-teardown"))
            .BDDfy();
        NoSetup.GetAsync("http://probe.local/b15-after-story-nosetup").GetAwaiter().GetResult();
        CallSync("b15-after-story");
    }

    [Fact]
    public void B16_A_woven_When_before_a_story_with_a_tear_down()
    {
        WovenWhen("b16-woven-when").GetAwaiter().GetResult();
        this.Given(_ => GivenSync("b16-given"))
            .Then(_ => ThenSync("b16-then"))
            .TearDownWith(_ => TearDownQuiet("b16-teardown"))
            .BDDfy();
        NoAction.GetAsync("http://probe.local/b16-after-story-noaction").GetAwaiter().GetResult();
        CallSync("b16-after-story");
    }

    [Fact]
    public void B17_StartAction_before_a_story_with_a_tear_down()
    {
        CallSync("b17-arrange");
        TrackingDiagramOverride.StartAction();
        this.Given(_ => GivenSync("b17-given"))
            .When(_ => WhenSync("b17-when"))
            .TearDownWith(_ => TearDownQuiet("b17-teardown"))
            .BDDfy();
        CallSync("b17-after-story");
    }

    [Fact]
    public void B18_A_story_with_an_initial_setup_method()
    {
        this.Given(_ => GivenSync("b18-given"))
            .When(_ => WhenSync("b18-when"))
            .BDDfy();
        this.Given(_ => WhenSync("b18-second-given"))
            .Then(_ => ThenSync("b18-second-then"))
            .BDDfy();
    }

    [Fact]
    public void B06_Async_void_support_off_with_a_nested_Given_and_a_failing_step()
    {
        Configurator.AsyncVoidSupportEnabled = false;
        try
        {
            Record.Exception(() => this.Given(_ => GivenAsync("b06-given"))
                .When(_ => WhenWithGivenSub("b06-when"))
                .Then(_ => WhenThrows("b06-then"))
                .BDDfy());
            Task.Delay(150).GetAwaiter().GetResult();
            CallSync("b06-after");
        }
        finally { Configurator.AsyncVoidSupportEnabled = true; }
    }

    [Fact]
    public void B07_Failing_async_When_after_a_nested_Given_then_tear_down()
    {
        Record.Exception(() => this.Given(_ => GivenAsync("b07-given"))
            .When(_ => WhenThrowsAfterSub("b07-when"))
            .Then(_ => ThenAsync("b07-then"))
            .TearDownWith(_ => TearDownQuiet("b07-teardown"))
            .BDDfy());
        CallSync("b07-after");
    }

    [Fact]
    public void B08_StartSetup_before_the_story_and_a_tear_down()
    {
        TrackingDiagramOverride.StartSetup();
        CallSync("b08-arrange");
        this.Given(_ => GivenSync("b08-given"))
            .When(_ => WhenSync("b08-when"))
            .TearDownWith(_ => TearDownQuiet("b08-teardown"))
            .BDDfy();
        NoSetup.GetAsync("http://probe.local/b08-after-story-nosetup").GetAwaiter().GetResult();
        CallSync("b08-after-story");
    }

    [Fact]
    public void B09_A_scenario_in_a_woven_Given_then_a_tear_down_and_a_When()
    {
        StoryInGiven("b09");
        CallSync("b09-after-woven");
        WovenWhen("b09-woven-when").GetAwaiter().GetResult();
        CallSync("b09-after-when");
    }

    [GivenStep]
    public void StoryInGiven(string l)
    {
        this.Given(_ => GivenAsync(l + "-story-given"))
            .When(_ => WhenAsync(l + "-story-when"))
            .Then(_ => ThenAsync(l + "-story-then"))
            .TearDownWith(_ => TearDownQuiet(l + "-story-teardown"))
            .BDDfy();
        CallSync(l + "-after-story");
    }

    [Fact]
    public void B10_A_scenario_in_a_woven_When_failing_in_its_Given()
    {
        Record.Exception(() => StoryInWhen("b10"));
        CallSync("b10-after-woven");
    }

    [WhenStep]
    public void StoryInWhen(string l)
    {
        CallSync(l + "-before-story");
        this.Given(_ => GivenThrowsSync(l + "-story-given"))
            .When(_ => WhenAsync(l + "-story-when"))
            .BDDfy();
    }

    [Fact]
    public void B11_A_step_that_marks_the_boundary_later_in_the_story()
    {
        this.Given(_ => GivenSync("b11-given"))
            .When(_ => WhenSync("b11-when"))
            .Then(_ => MarkingThen("b11-then"))
            .BDDfy();
    }

    [Fact]
    public void B12_A_ValueTask_step()
    {
        this.Given(_ => GivenSync("b12-given"))
            .When(_ => WhenValueTask("b12-when"))
            .Then(_ => ThenAsync("b12-then"))
            .BDDfy();
    }

    [Fact]
    public void B13_A_story_on_a_flow_forked_inside_a_woven_Given()
    {
        Task? story = null;
        GivenThatForksAStory("b13", t => story = t);
        story!.GetAwaiter().GetResult();
        CallSync("b13-after");
    }

    [GivenStep]
    public void GivenThatForksAStory(string l, Action<Task> keep)
    {
        CallSync(l + "-seed");
        keep(Task.Run(async () =>
        {
            await Task.Delay(20);
            this.Given(_ => GivenSync(l + "-story-given")).When(_ => WhenSync(l + "-story-when")).Then(_ => ThenSync(l + "-story-then")).BDDfy();
        }));
    }

    [Fact]
    public void B14_Fixture_story_with_no_test_id()
    {
        var fixture = new SeedFixture();
        fixture.InitializeAsync().GetAwaiter().GetResult();
        CallSync("b14-after");
    }
}

public class FixtureStories : IClassFixture<SeedFixture>
{
    [Fact]
    public void F01_Class_fixture_story_read_its_phases()
    {
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? ".", "rev5-bddfy-fixture.txt"),
            $"in Given: {SeedFixture.PhaseInGiven}, in When: {SeedFixture.PhaseInWhen}\n");
    }
}
