using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.Rev3.More;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Dump2.Write("rev3-more");
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions { SpecificationsTitle = "More probe", SeparateSetup = true });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public abstract class Base : DiagrammedComponentTest
{
    protected static readonly AsyncLocal<string?> StepType = new();
    private static MessageTracker Tracker() => new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher });
    private static Calls Client(bool trackSetup, bool fetcher) => new(new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = trackSetup,
        CurrentStepTypeFetcher = fetcher ? () => StepType.Value : null,
    }) { InnerHandler = new AnswerEverything() }), Tracker());

    protected readonly Calls C = Client(true, false);
    protected readonly Calls F = Client(true, true);
    protected readonly Calls FNoSetup = Client(false, true);

    protected Base() { Dump2.Names[TestContext.Current.Test!.UniqueID] = TestContext.Current.Test!.TestDisplayName; }

    [GivenStep] public async Task WovenGiven(string l) => await C.Make(l);
    [WhenStep] public async Task WovenWhen(string l) => await C.Make(l);
    [Step] public async Task OtherTask(string l) => await C.Make(l);
    [Step] public void OtherSync(string l) { }
}

/// <summary>A step for another identity inside a Given, then a keyword under that Given: is the Given still above it on the flow?</summary>
public class H_OtherIdentity : Base
{
    [GivenStep] public async Task Given_other_identity_task(string l)
    {
        await C.Make(l + "-before");
        using (TestIdentityScope.Begin("other", "other-" + l)) { await OtherTask(l + "-other"); }
        await WovenWhen(l + "-nested-when");
        await C.Make(l + "-after");
    }

    [GivenStep] public async Task Given_other_identity_sync(string l)
    {
        await C.Make(l + "-before");
        using (TestIdentityScope.Begin("other", "other-" + l)) { OtherSync(l + "-other"); }
        await WovenWhen(l + "-nested-when");
        await C.Make(l + "-after");
    }

    [Fact] public async Task H10_a_task_step_for_another_identity_inside_a_Given()
    {
        await Given_other_identity_task("h10");
        await WovenWhen("h10-when");
    }

    [Fact] public async Task H11_a_sync_step_for_another_identity_inside_a_Given()
    {
        await Given_other_identity_sync("h11");
        await WovenWhen("h11-when");
    }
}

/// <summary>A suite whose phases come from a custom CurrentStepTypeFetcher, with a woven keyword step somewhere in it.</summary>
public class H_FetcherMix : Base
{
    [Fact] public async Task H20_control_fetcher_only()
    {
        StepType.Value = "GIVEN"; await F.Make("h20-given");
        StepType.Value = "WHEN"; await F.Make("h20-when"); await FNoSetup.Make("h20-when-nosetup");
        StepType.Value = "THEN"; await F.Make("h20-then");
    }

    [Fact] public async Task H21_fetcher_and_one_woven_Given()
    {
        StepType.Value = "GIVEN"; await F.Make("h21-given"); await WovenGiven("h21-woven-given");
        StepType.Value = "WHEN"; await F.Make("h21-when"); await FNoSetup.Make("h21-when-nosetup");
        StepType.Value = "THEN"; await F.Make("h21-then");
    }

    [Fact] public async Task H22_one_woven_Given_first_then_fetcher()
    {
        await WovenGiven("h22-woven-given");
        StepType.Value = "GIVEN"; await F.Make("h22-given");
        StepType.Value = "WHEN"; await F.Make("h22-when"); await FNoSetup.Make("h22-when-nosetup");
        StepType.Value = "THEN"; await F.Make("h22-then");
    }

    [Fact] public async Task H23_fetcher_and_one_woven_When()
    {
        StepType.Value = "GIVEN"; await F.Make("h23-given");
        StepType.Value = "WHEN"; await WovenWhen("h23-woven-when"); await F.Make("h23-when"); await FNoSetup.Make("h23-when-nosetup");
        StepType.Value = "THEN"; await F.Make("h23-then");
    }
}

/// <summary>
/// The race that a timer tick hits by chance, forced: a background step is on top of the stack, before its stub has returned its task, when the
/// step that started it completes. The completion pops the background step. What does that background flow's next keyword see?
/// </summary>
public class H_PoppedBeforeItsStubReturned : Base
{
    private readonly ManualResetEventSlim _entered = new();
    private readonly ManualResetEventSlim _closed = new();
    private readonly TaskCompletionSource _go = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Calls _noSetup = new(new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = false,
    }) { InnerHandler = new AnswerEverything() }), new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));
    private Task? _background;

    [GivenStep] public async Task Blocking_poll(string l)
    {
        _entered.Set();
        _closed.Wait();          // still in the stub's call: it has not returned its task
        await C.Make(l);
    }

    [GivenStep] public async Task Later_poll(string l) => await C.Make(l);

    [GivenStep] public void Given_starts_a_background_step(string l)
    {
        _background = Task.Run(async () =>
        {
            await Blocking_poll(l + "-first");
            await _go.Task;
            await Later_poll(l + "-later");
        });
        _entered.Wait();
    }

    [WhenStep] public async Task When_call(string l) => await C.Make(l);

    [Fact] public async Task H40_a_step_popped_before_its_stub_returned_then_the_flow_keeps_going()
    {
        var id = TestContext.Current.Test!.UniqueID;
        Given_starts_a_background_step("h40");
        _closed.Set();
        await When_call("h40-when");
        await C.Make("h40-after-when");
        _go.SetResult();
        await _background!;
        StepCollector.StartStep(id, "And", "h40-and", null, null);
        await _noSetup.Make("h40-in-and-nosetup");
        StepCollector.CompleteStep(id, true);
    }
}

/// <summary>What the trial's forked-flow rule costs: a loop of Then steps started in a Given, on a client that drops Setup calls.</summary>
public class H_LoopOnANoSetupClient : Base
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Calls _noSetup = new(new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = false,
    }) { InnerHandler = new AnswerEverything() }), new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));
    private Task? _loop;

    [ThenStep] public async Task Then_poll(string l) => await _noSetup.Make(l);

    [GivenStep] public void Given_a_loop(string l)
    {
        _loop = Task.Run(async () =>
        {
            var i = 0;
            while (!_stop.IsCancellationRequested) { await Then_poll($"{l}-poll{i++}"); await Task.Delay(15); }
        });
    }

    [WhenStep] public async Task When_call(string l) => await C.Make(l);

    [Fact] public async Task H41_a_loop_of_Then_steps_from_a_Given_on_a_TrackDuringSetup_false_client()
    {
        Given_a_loop("h41");
        await Task.Delay(50);
        await When_call("h41-when");
        await Task.Delay(50);
        _stop.Cancel();
        await _loop!;
    }
}

