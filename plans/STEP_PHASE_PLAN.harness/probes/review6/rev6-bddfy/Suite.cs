using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using Rev5;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;

[assembly: AssemblyFixture(typeof(Rev6.Bddfy.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace Rev6.Bddfy;

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
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "rev6-bddfy");
        return ValueTask.CompletedTask;
    }
}

public static class Helpers
{
    public static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true) => new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = trackDuringSetup,
        TrackDuringAction = trackDuringAction
    }) { InnerHandler = new Answer() });
}

/// <summary>A story BDDfy scans by name: Setup and TearDown have no keyword.</summary>
public class ByNameStory(Stories t, string l)
{
    public void Setup() => t.Three(l + "-setup");
    public void Given_a_seed() => t.CallSync(l + "-given");
    public void When_an_act() => t.CallSync(l + "-when");
    public void Then_a_check() => t.CallSync(l + "-then");
    public void TearDown() => t.Three(l + "-teardown");
}

public class Stories
{
    public readonly HttpClient Http = Helpers.NewClient();
    public readonly HttpClient NoSetup = Helpers.NewClient(trackDuringSetup: false);
    public readonly HttpClient NoAction = Helpers.NewClient(trackDuringAction: false);
    public async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    public void CallSync(string label) => Call(label).GetAwaiter().GetResult();
    public void Three(string l)
    {
        CallSync(l);
        NoSetup.GetAsync($"http://probe.local/{l}-nosetup").GetAwaiter().GetResult();
        NoAction.GetAsync($"http://probe.local/{l}-noaction").GetAwaiter().GetResult();
    }

    protected void GivenSync(string l) => CallSync(l);
    protected async Task GivenAsync(string l) { await Task.Yield(); await Call(l); }
    protected void WhenSync(string l) => CallSync(l);
    protected void ThenSync(string l) => CallSync(l);

    [GivenStep] public async Task WovenGiven(string l) => await Call(l);
    [WhenStep] public async Task WovenWhen(string l) => await Call(l);
    [ThenStep] public async Task WovenThen(string l) => await Call(l);

    public bool Yes => true;
    [GivenStep(SkipIf = nameof(Yes))] public async Task SkipAsync(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(Yes))] public void SkipSync(string l) => CallSync(l);
    [GivenStep(SkipIf = nameof(Yes))] public Task<int> SkipTaskInt(string l) => Task.FromResult(1);

    private int _attempts;
    protected void ThenFailsTheFirstTime(string l)
    {
        CallSync(l);
        if (_attempts++ == 0) throw new InvalidOperationException("first attempt");
    }

    protected void WhenMarksAfterACall(string l)
    {
        CallSync(l + "-arrange");
        TrackingDiagramOverride.StartAction();
        CallSync(l);
    }

    protected async Task WhenMarksAfterAnAwait(string l)
    {
        await Call(l + "-arrange");
        TrackingDiagramOverride.StartAction();
        await Call(l);
    }

    protected async Task WhenRunsSkips(string l)
    {
        await Call(l + "-before");
        await SkipAsync(l + "-skip-async");
        Three(l + "-after-skip-async");
        SkipSync(l + "-skip-sync");
        Three(l + "-after-skip-sync");
        await SkipTaskInt(l + "-skip-int");
        await WovenGiven(l + "-sub");
        Three(l + "-after");
    }

    protected void WhenRunsSkipsSync(string l)
    {
        CallSync(l + "-before");
        SkipSync(l + "-skip-sync");
        Three(l + "-after-skip-sync");
        SkipAsync(l + "-skip-async").GetAwaiter().GetResult();
        WovenGiven(l + "-sub").GetAwaiter().GetResult();
        Three(l + "-after");
    }

    [Fact] public async Task B20_By_name_story_Setup_after_a_woven_Given() { await WovenGiven("b20-woven"); Three("b20-after-woven"); new ByNameStory(this, "b20").BDDfy(); Three("b20-after-story"); }
    [Fact] public void B21_By_name_story_Setup_after_StartSetup() { TrackingDiagramOverride.StartSetup(); new ByNameStory(this, "b21").BDDfy(); Three("b21-after-story"); }
    [Fact] public void B22_By_name_story_Setup_after_StartAction() { TrackingDiagramOverride.StartAction(); new ByNameStory(this, "b22").BDDfy(); Three("b22-after-story"); }
    [Fact] public async Task B23_By_name_story_Setup_after_a_woven_When() { await WovenWhen("b23-woven"); new ByNameStory(this, "b23").BDDfy(); }
    [Fact] public void B19_By_name_story_alone() { new ByNameStory(this, "b19").BDDfy(); }
    [Fact] public void B18_By_name_story_after_a_phase_written_by_hand() { TestPhaseContext.Current = TestPhase.Setup; new ByNameStory(this, "b18").BDDfy(); }

    [Fact]
    public async Task B24_A_woven_Given_and_a_woven_When_before_the_story()
    {
        await WovenGiven("b24-woven-given");
        await WovenWhen("b24-woven-when");
        this.Given(_ => GivenSync("b24-seed")).When(_ => WhenSync("b24-act")).Then(_ => ThenSync("b24-then")).BDDfy();
    }

    [Fact]
    public async Task B25_A_woven_When_and_a_woven_Given_before_the_story()
    {
        await WovenWhen("b25-woven-when");
        await WovenGiven("b25-woven-given");
        this.Given(_ => GivenSync("b25-seed")).When(_ => WhenSync("b25-act")).Then(_ => ThenSync("b25-then")).BDDfy();
    }

    [Fact]
    public void B26_A_hand_retry_of_a_story_that_marks_in_its_When()
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var a = attempt;
            try
            {
                this.Given(_ => GivenSync($"b26-seed{a}")).When(_ => WhenMarksAfterACall($"b26-act{a}")).Then(_ => ThenFailsTheFirstTime($"b26-then{a}")).BDDfy();
            }
            catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public void B27_A_hand_retry_of_a_story_with_no_mark()
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var a = attempt;
            try
            {
                this.Given(_ => GivenSync($"b27-seed{a}")).When(_ => WhenSync($"b27-act{a}")).Then(_ => ThenFailsTheFirstTime($"b27-then{a}")).BDDfy();
            }
            catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public void B28_An_async_When_marks_after_its_first_await()
    {
        this.Given(_ => GivenSync("b28-seed")).When(_ => WhenMarksAfterAnAwait("b28-act")).Then(_ => ThenSync("b28-then")).BDDfy();
    }

    [Fact]
    public void B29_An_async_When_runs_skipped_woven_steps()
    {
        this.Given(_ => GivenSync("b29-seed")).When(_ => WhenRunsSkips("b29")).Then(_ => ThenSync("b29-then")).BDDfy();
    }

    [Fact]
    public void B30_A_sync_When_runs_skipped_woven_steps()
    {
        this.Given(_ => GivenSync("b30-seed")).When(_ => WhenRunsSkipsSync("b30")).Then(_ => ThenSync("b30-then")).BDDfy();
    }

    private TaskCompletionSource _gate = new();
    private Task _left = Task.CompletedTask;
    [GivenStep] public async Task WovenSlow(string l, Task gate) { await gate; await Call(l); }
    protected void WhenLeavesRunning(string l) { _gate = new TaskCompletionSource(); _left = WovenSlow(l + "-left", _gate.Task); CallSync(l); }
    protected void ThenMarks(string l) { TrackingDiagramOverride.StartAction(); CallSync(l); }
    protected void ThenReleases(string l) { _gate.TrySetResult(); _left.GetAwaiter().GetResult(); CallSync(l); }

    [Fact]
    public void B32_A_When_leaves_a_woven_step_running_a_later_Then_marks()
    {
        this.Given(_ => GivenSync("b32-seed")).When(_ => WhenLeavesRunning("b32-act")).Then(_ => ThenMarks("b32-then")).And(_ => ThenReleases("b32-release")).BDDfy();
    }

    [Fact]
    public void B31_A_sync_Given_runs_skipped_woven_steps()
    {
        this.Given(_ => WhenRunsSkipsSync("b31")).When(_ => WhenSync("b31-act")).Then(_ => ThenSync("b31-then")).BDDfy();
    }
}
