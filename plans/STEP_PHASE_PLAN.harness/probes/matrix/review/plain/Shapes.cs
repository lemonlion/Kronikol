using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.Plain;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose() { Dump.Write("plain"); }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public class Shapes : DiagrammedComponentTest
{
    private readonly Calls _calls = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));
    private readonly Calls _real = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerAfterAWait() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    public bool Always => true;

    [Step] public async Task PlainAsync(string label) => await _calls.Make(label);
    [GivenStep] public async Task GivenAsync(string label) => await _calls.Make(label);
    [WhenStep] public async Task WhenAsync(string label) => await _calls.Make(label);
    [ThenStep] public async Task ThenAsync(string label) => await _calls.Make(label);
    [GivenStep] public async Task GivenReal(string label) => await _real.Make(label);
    [WhenStep] public async Task WhenReal(string label) => await _real.Make(label);
    [WhenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public async Task WhenSkipped(string label) => await _calls.Make(label);

    [WhenStep]
    public async Task WhenWithExplicitBoundary(string label)
    {
        await _calls.Make($"{label}-prepare-before-explicit");
        TrackingDiagramOverride.StartAction();
        await _calls.Make($"{label}-after-explicit");
    }

    [Step]
    public async Task WrapperWithWhen(string label) { await WhenAsync($"{label}-wrapped-when"); }

    [GivenStep]
    public async Task Background(TaskCompletionSource gate) => await gate.Task;

    [Fact]
    public async Task S1_explicit_StartAction_inside_a_When_step_after_a_Given()
    {
        await GivenAsync("s1-given");
        await WhenWithExplicitBoundary("s1");
        await _calls.Make("s1-after");
    }

    [Fact]
    public async Task S2_WhenAll_of_a_Given_and_a_When_over_a_client_that_yields()
    {
        await Task.WhenAll(GivenReal("s2-given"), WhenReal("s2-when"));
        await _calls.Make("s2-after");
    }

    [Fact]
    public async Task S3_keyword_free_step_after_an_async_wrapper_with_a_When()
    {
        await GivenAsync("s3-given");
        await WrapperWithWhen("s3");
        await PlainAsync("s3-plain-after-wrapper");
        await _calls.Make("s3-call-after");
    }

    [Fact]
    public async Task S4_a_skipped_When_then_a_Given_then_a_When()
    {
        await GivenAsync("s4-given");
        await WhenSkipped("s4-skipped");
        await GivenAsync("s4-given-2");
        await WhenAsync("s4-when");
    }

    [Fact]
    public async Task S5_a_step_never_awaited()
    {
        var gate = new TaskCompletionSource();
        _ = Background(gate);
        await _calls.Make("s5-while-open");
        await WhenAsync("s5-when");
        await ThenAsync("s5-then");
        gate.SetResult();
    }
}
