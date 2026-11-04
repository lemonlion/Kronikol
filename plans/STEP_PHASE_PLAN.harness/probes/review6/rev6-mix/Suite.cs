using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev5;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rev6.Mix;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "rev6-mix");
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

/// <summary>An older weaver with this core, and this weaver with an older core: the shapes where the two disagree.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Mix : DiagrammedComponentTest
{
    private static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true) =>
        new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            TrackDuringSetup = trackDuringSetup,
            TrackDuringAction = trackDuringAction
        }) { InnerHandler = new Answer() });

    private readonly HttpClient Http = NewClient();
    private readonly HttpClient NoSetup = NewClient(trackDuringSetup: false);
    private readonly HttpClient NoAction = NewClient(trackDuringAction: false);
    private async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    private void CallSync(string label) => Call(label).GetAwaiter().GetResult();
    private async Task Three(string label)
    {
        await Call(label);
        await NoSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await NoAction.GetAsync($"http://probe.local/{label}-noaction");
    }

    [GivenStep] public async Task GivenAsync(string label) => await Call(label);
    [GivenStep] public void GivenSync(string label) => CallSync(label);
    [WhenStep] public async Task WhenAsync(string label) => await Call(label);
    [ThenStep] public async Task ThenAsync(string label) => await Call(label);

    public bool Yes => true;
    public bool Throws => throw new InvalidOperationException("skip getter");
    [GivenStep(SkipIf = nameof(Throws))] public async Task GivenSkipThrows(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(Throws))] public void GivenSkipThrowsSync(string l) => CallSync(l);
    [GivenStep(SkipIf = nameof(Yes))] public void SkipSync(string l) => CallSync(l);
    [GivenStep(SkipIf = nameof(Yes))] public async Task SkipAsync(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(Yes))] public Task SkipTask(string l) => Call(l);
    [GivenStep] public Task GivenThrowsBeforeTask(string l) => throw new InvalidOperationException("no task");

    [Fact] public async Task M01_SkipIf_getter_throws_async_then_When_Then() { try { await GivenSkipThrows("m01-given"); } catch (InvalidOperationException) { } await Three("m01-after"); await WhenAsync("m01-act"); await Three("m01-after-act"); await ThenAsync("m01-then"); }
    [Fact] public async Task M02_SkipIf_getter_throws_sync_then_When_Then() { try { GivenSkipThrowsSync("m02-given"); } catch (InvalidOperationException) { } await Three("m02-after"); await WhenAsync("m02-act"); await Three("m02-after-act"); await ThenAsync("m02-then"); }
    [Fact] public async Task M03_A_Task_Given_throws_before_its_task_then_When_Then() { await Assert.ThrowsAsync<InvalidOperationException>(() => GivenThrowsBeforeTask("m03-given")); await Three("m03-after"); await WhenAsync("m03-act"); await Three("m03-after-act"); await ThenAsync("m03-then"); }

    [WhenStep] public async Task WhenSkipSync(string l) { await Call(l + "-before"); SkipSync(l + "-skip"); await Three(l + "-after"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }
    [WhenStep] public async Task WhenSkipAsync(string l) { await Call(l + "-before"); await SkipAsync(l + "-skip"); await Three(l + "-after"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }
    [WhenStep] public async Task WhenSkipTask(string l) { await Call(l + "-before"); await SkipTask(l + "-skip"); await Three(l + "-after"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }
    [WhenStep] public async Task WhenLastSkip(string l) { await Call(l + "-before"); await SkipAsync(l + "-skip"); }

    [Fact] public async Task M04_A_sync_skip_under_a_When() { await GivenAsync("m04-seed"); await WhenSkipSync("m04"); await Three("m04-after-when"); await ThenAsync("m04-then"); }
    [Fact] public async Task M05_An_async_skip_under_a_When() { await GivenAsync("m05-seed"); await WhenSkipAsync("m05"); await Three("m05-after-when"); await ThenAsync("m05-then"); }
    [Fact] public async Task M06_A_Task_skip_under_a_When() { await GivenAsync("m06-seed"); await WhenSkipTask("m06"); await Three("m06-after-when"); await ThenAsync("m06-then"); }
    [Fact] public async Task M07_A_skip_last_under_a_When() { await GivenAsync("m07-seed"); await WhenLastSkip("m07"); await Three("m07-after-when"); await ThenAsync("m07-then"); }

    [WhenStep] public async Task WhenSub(string l) { await Call(l + "-before"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }
    [Fact] public async Task M08_The_issue_shape() { await GivenAsync("m08-seed"); await WhenSub("m08"); await Three("m08-after-when"); await ThenAsync("m08-then"); }

    [Fact] public async Task M10_A_Task_Given_throws_before_its_task_caught_in_the_test_then_When_Then() { try { await GivenThrowsBeforeTask("m10-given"); } catch (InvalidOperationException) { } await Three("m10-after"); await WhenAsync("m10-act"); await Three("m10-after-act"); await ThenAsync("m10-then"); }
    [GivenStep(SkipIf = nameof(Yes))] public async void SkipVoid(string l) => await Call(l);
    public bool No => false;
    [GivenStep(SkipIf = nameof(No))] public Task GivenNoSkipThrows(string l) => throw new InvalidOperationException("body");
    [WhenStep] public async Task WhenSkipVoid(string l) { await Call(l + "-before"); SkipVoid(l + "-skip"); await Three(l + "-after"); await GivenAsync(l + "-sub"); await Three(l + "-after-sub"); }
    [Fact] public async Task M11_An_async_void_skip_under_a_When() { await GivenAsync("m11-seed"); await WhenSkipVoid("m11"); await Three("m11-after-when"); await ThenAsync("m11-then"); }
    [Fact] public async Task M12_A_getter_false_Task_stub_throws_caught_then_When() { try { await GivenNoSkipThrows("m12-given"); } catch (InvalidOperationException) { } await Three("m12-after"); await WhenAsync("m12-act"); await Three("m12-after-act"); }

    private static readonly string[] Words = ["GIVEN", "given", "SETUP", "Setup", "And", "AND", "WHEN", "when", "THEN", "BUTWHEN", "Arrange"];

    [Fact]
    public async Task M13_Hand_words_that_say_nothing_through_both_overloads()
    {
        var id = TestContext.Current.Test!.UniqueID;
        await GivenAsync("m13-seed");
        foreach (var w in Words)
        {
            StepCollector.StartStep(id, w, $"m13-{w}-byid", null, null);
            await Three($"m13-{w}-byid-in");
            StepCollector.CompleteStep(id, true);
            StepCollector.StartStep(w, $"m13-{w}-ambient", null, null);
            await Three($"m13-{w}-ambient-in");
            StepCollector.CompleteStep(true);
            await Three($"m13-{w}-after");
        }
        await WhenAsync("m13-act");
        foreach (var w in Words)
        {
            StepCollector.StartStep(id, w, $"m13b-{w}-byid", null, null);
            await Three($"m13b-{w}-byid-in");
            StepCollector.CompleteStep(id, true);
            await Three($"m13b-{w}-after");
        }
    }

    [Fact]
    public async Task M14_Hand_words_that_do_say_through_both_overloads()
    {
        var id = TestContext.Current.Test!.UniqueID;
        StepCollector.StartStep(id, "Given", "m14-given", null, null);
        await Three("m14-given-in");
        StepCollector.CompleteStep(id, true);
        StepCollector.StartStep("But", "m14-but", null, null);
        await Three("m14-but-in");
        StepCollector.CompleteStep(true);
        StepCollector.StartStep(id, "ButWhen", "m14-butwhen", null, null);
        await Three("m14-butwhen-in");
        StepCollector.CompleteStep(id, true);
        StepCollector.StartStep("Then", "m14-then", null, null);
        await Three("m14-then-in");
        StepCollector.CompleteStep(true);
        StepCollector.StartStep(id, "When", "m14-when", null, null);
        await Three("m14-when-in");
        StepCollector.CompleteStep(id, true);
    }

    [Fact] public async Task M09_A_top_level_skip_then_a_real_When() { await SkipAsync("m09-skip"); await Three("m09-after-skip"); await WhenAsync("m09-act"); await Three("m09-after-act"); }
}
