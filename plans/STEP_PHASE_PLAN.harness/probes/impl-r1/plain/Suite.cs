using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Probe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Probe.Plain;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "plain");
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public abstract class Base : DiagrammedComponentTest
{
    protected static HttpClient NewClient(bool s = true, bool a = true) => new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = s, TrackDuringAction = a
    }) { InnerHandler = new Answer() });

    protected readonly HttpClient Http = NewClient();
    protected readonly HttpClient NoSetup = NewClient(s: false);
    protected readonly HttpClient NoAction = NewClient(a: false);
    protected readonly MessageTracker Bus = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher });

    protected string Id => TestContext.Current.Test!.UniqueID;

    protected async Task Call(string label)
    {
        (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
        await NoSetup.GetAsync($"http://probe.local/{label}-nosetup");
        await NoAction.GetAsync($"http://probe.local/{label}-noaction");
        Bus.TrackSendEvent("bus", label, new Uri($"bus://probe/{label}"));
    }
    protected void CallSync(string label) => Call(label).GetAwaiter().GetResult();

    [GivenStep] public async Task GivenAsync(string label) => await Call(label);
    [GivenStep] public void GivenSync(string label) => CallSync(label);
    [WhenStep] public async Task WhenAsync(string label) => await Call(label);
    [WhenStep] public void WhenSync(string label) => CallSync(label);
    [ThenStep] public async Task ThenAsync(string label) => await Call(label);
    [Step] public async Task PlainAsync(string label) => await Call(label);
}

/// <summary>Hand-made steps of the shapes C# makes natural: a scope, a using declaration, a whole-test bracket.</summary>
public sealed class HandStep : IDisposable
{
    private readonly string _id;
    private bool _done;
    public HandStep(string id, string? keyword, string text) { _id = id; StepCollector.StartStep(id, keyword, text, null, null); }
    public void Dispose() { if (_done) return; _done = true; StepCollector.CompleteStep(_id, true); }
}

public sealed class AsyncHandStep : IAsyncDisposable
{
    private readonly string _id;
    public AsyncHandStep(string id, string? keyword, string text) { _id = id; StepCollector.StartStep(id, keyword, text, null, null); }
    public async ValueTask DisposeAsync() { await Task.Yield(); StepCollector.CompleteStep(_id, true); }
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Hand : Base
{
    [Fact]
    public async Task H01_a_using_declaration_Given_spans_the_rest_of_the_test()
    {
        using var arrange = new HandStep(Id, "Given", "arrange by a using declaration");
        await Call("h01-seed");
        await WhenAsync("h01-when");
        await Call("h01-after-when");
        await ThenAsync("h01-then");
    }

    [Fact]
    public async Task H02_a_using_block_Given_closes_before_the_When()
    {
        using (new HandStep(Id, "Given", "arrange in a block"))
            await Call("h02-seed");
        await Call("h02-between");
        await WhenAsync("h02-when");
        await Call("h02-after-when");
    }

    [Fact]
    public async Task H03_ClearSteps_with_a_Given_open_then_a_When()
    {
        StepCollector.StartStep(Id, "Given", "attempt one", null, null);
        await Call("h03-seed");
        StepCollector.ClearSteps(Id);
        await WhenAsync("h03-when");
        await Call("h03-after-when");
    }

    [Fact]
    public async Task H04_a_hand_Given_with_a_body_that_throws_caught_on_the_flow()
    {
        void Given(Action body) { StepCollector.StartStep(Id, "Given", "seed", null, null); body(); StepCollector.CompleteStep(Id, true); }
        try { Given(() => throw new InvalidOperationException("seed failed once")); } catch (InvalidOperationException) { }
        await WhenAsync("h04-when");
        await Call("h04-after-when");
        await ThenAsync("h04-then");
    }

    [Fact]
    public async Task H05_an_await_using_Given_then_a_When()
    {
        await using (new AsyncHandStep(Id, "Given", "arrange"))
            await Call("h05-seed");
        await Call("h05-between");
        await WhenAsync("h05-when");
        await Call("h05-after-when");
        await GivenAsync("h05-second-given");
        await Call("h05-after-second-given");
    }

    [Fact]
    public async Task H06_an_await_using_When_with_a_nested_Given_then_top_level_steps()
    {
        await GivenAsync("h06-given");
        await using (new AsyncHandStep(Id, "When", "act"))
        {
            await Call("h06-in-when");
            await GivenAsync("h06-nested-given");
            await Call("h06-after-nested");
        }
        await Call("h06-after-when");
        await ThenAsync("h06-then");
        await GivenAsync("h06-given-again");
        await Call("h06-after-given-again");
    }

    [WhenStep] public void WhenLeavesAKeywordFreeHandStepOpen(string l) { CallSync(l); StepCollector.StartStep(Id, null, "a sub-step never closed", null, null); }

    [Fact]
    public async Task H08_a_sync_When_leaves_a_keyword_free_hand_step_open_then_a_Given_and_Then()
    {
        WhenLeavesAKeywordFreeHandStepOpen("h08-when");
        await Call("h08-after-when");
        await GivenAsync("h08-given");
        await Call("h08-after-given");
        await ThenAsync("h08-then");
    }

    [Fact]
    public async Task H07_CompleteStep_with_the_wrong_id_leaves_the_Given_open_then_a_When()
    {
        StepCollector.StartStep(Id, "Given", "opened by id", null, null);
        await Call("h07-seed");
        StepCollector.CompleteStep("some-other-id", true);
        await WhenAsync("h07-when");
        await Call("h07-after-when");
    }
}

/// <summary>A step opened by the test class around the test: its constructor and Dispose.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Bracket : Base, IDisposable
{
    private readonly HandStep _whole;
    public Bracket() { _whole = new HandStep(Id, "Given", "a bracket from the constructor"); }
    public void Dispose() => _whole.Dispose();

    [Fact]
    public async Task B01_a_constructor_bracket_Given_then_steps()
    {
        await Call("b01-first");
        await WhenAsync("b01-when");
        await Call("b01-after-when");
    }
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class BracketAsync : Base, IAsyncLifetime
{
    private HandStep? _whole;
    public ValueTask InitializeAsync() { _whole = new HandStep(Id, "Given", "a bracket from InitializeAsync"); return default; }
    public ValueTask DisposeAsync() { _whole!.Dispose(); return default; }

    [Fact]
    public async Task B02_an_InitializeAsync_bracket_Given_then_steps()
    {
        await Call("b02-first");
        await WhenAsync("b02-when");
        await Call("b02-after-when");
    }
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Lifecycle : Base, IAsyncLifetime
{
    public Lifecycle() { GivenSync("l-ctor-given"); }
    public async ValueTask InitializeAsync() { await GivenAsync("l-init-given"); await Call("l-init-after"); }
    public async ValueTask DisposeAsync() { await Call("l-dispose-first"); await GivenAsync("l-dispose-given"); await Call("l-dispose-after"); }

    [Fact]
    public async Task L01_steps_in_the_constructor_and_InitializeAsync_and_DisposeAsync()
    {
        await Call("l01-first");
        await WhenAsync("l01-when");
        await Call("l01-after-when");
    }
}

/// <summary>Consumer habits around an outer keyword step.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Outer : Base
{
    [WhenStep]
    public async Task WhenStartSetupThenInnerWhen(string l)
    {
        TrackingDiagramOverride.StartSetup();
        await Call($"{l}-seed-by-hand");
        await WhenAsync($"{l}-inner-when");
        await Call($"{l}-after-inner-when");
    }

    [GivenStep]
    public async Task GivenStartActionThenInnerGiven(string l)
    {
        TrackingDiagramOverride.StartAction();
        await Call($"{l}-act-by-hand");
        await GivenAsync($"{l}-inner-given");
        await Call($"{l}-after-inner-given");
    }

    [WhenStep]
    public async Task WhenPhaseWrittenThenInnerWhen(string l)
    {
        TestPhaseContext.Current = TestPhase.Setup;
        await Call($"{l}-seed-by-write");
        await WhenAsync($"{l}-inner-when");
        await Call($"{l}-after-inner-when");
    }

    [WhenStep]
    public async Task WhenPhaseRestored(string l)
    {
        var was = TestPhaseContext.Current;
        TestPhaseContext.Current = TestPhase.Setup;
        await GivenAsync($"{l}-inner-given");
        TestPhaseContext.Current = was;
        await Call($"{l}-after");
    }

    [GivenStep]
    public async Task GivenPlacesAnOrderThroughAWhen(string l)
    {
        await WhenAsync($"{l}-reused-when");
        await Call($"{l}-after-reused-when");
    }

    [Fact]
    public async Task O01_a_When_marks_Setup_by_hand_and_lets_an_inner_When_end_it()
    {
        await GivenAsync("o01-given");
        await WhenStartSetupThenInnerWhen("o01");
        await Call("o01-after-when");
    }

    [Fact]
    public async Task O02_a_Given_marks_Action_by_hand_and_lets_an_inner_Given_end_it()
    {
        await GivenStartActionThenInnerGiven("o02");
        await Call("o02-after-given");
    }

    [Fact]
    public async Task O03_a_When_writes_Setup_and_lets_an_inner_When_end_it()
    {
        await GivenAsync("o03-given");
        await WhenPhaseWrittenThenInnerWhen("o03");
    }

    [Fact]
    public async Task O04_the_consumers_own_workaround_for_224_save_and_restore()
    {
        await GivenAsync("o04-given");
        await WhenPhaseRestored("o04");
        await Call("o04-after-when");
    }

    [Fact]
    public async Task O05_a_Given_reuses_a_When_to_arrange()
    {
        await GivenPlacesAnOrderThroughAWhen("o05");
        await Call("o05-after-given");
        await WhenAsync("o05-when");
    }
}
