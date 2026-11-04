using System.Threading.Channels;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev4;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rev4.A;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write("rev4-a");
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public abstract class Base : DiagrammedComponentTest
{
    protected static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true) =>
        new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            TrackDuringSetup = trackDuringSetup,
            TrackDuringAction = trackDuringAction
        }) { InnerHandler = new Answer() });

    protected readonly HttpClient Http = NewClient();

    protected async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    [GivenStep] public async Task GivenAsync(string label) => await Call(label);
    [GivenStep] public void GivenNoCall(string label) { }
    [WhenStep] public async Task WhenAsync(string label) => await Call(label);
    [WhenStep] public void WhenNoCall(string label) { }
    [ThenStep] public async Task ThenAsync(string label) => await Call(label);
}

/// <summary>The same boundary asked for more than once: "the last marker wins".</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Markers : Base
{
    private async Task ActWithMarker(string label)
    {
        TrackingDiagramOverride.StartAction();
        await Call(label);
    }

    [WhenStep]
    public async Task WhenStepThatMarks(string label)
    {
        TrackingDiagramOverride.StartAction();
        await Call(label);
    }

    // A test helper that marks the boundary, called once per act (each act "is the action").
    [Fact]
    public async Task M01_Given_then_a_helper_that_marks_called_three_times()
    {
        await GivenAsync("m01-seed");
        await ActWithMarker("m01-act1");
        await ActWithMarker("m01-act2");
        await ActWithMarker("m01-act3");
    }

    // No steps at all, as the wiki's StartAction section does: seed, then a loop of acts.
    [Fact]
    public async Task M02_No_steps_a_loop_of_marked_acts()
    {
        TrackingDiagramOverride.StartSetup();
        await Call("m02-seed");
        for (var i = 1; i <= 3; i++)
        {
            TrackingDiagramOverride.StartAction();
            await Call($"m02-act{i}");
        }
    }

    // The step definition shape the repo's examples use (a When that calls StartAction), used twice in one test.
    [Fact]
    public async Task M03_Two_When_steps_each_marking()
    {
        await GivenAsync("m03-seed");
        await WhenStepThatMarks("m03-act1");
        await WhenStepThatMarks("m03-act2");
        await ThenAsync("m03-then");
    }

    // No setup at all: two marked acts. 5.28.2 draws no Setup box; last-wins boxes the first act.
    [Fact]
    public async Task M06_Two_marked_acts_and_no_setup()
    {
        await ActWithMarker("m06-act1");
        await ActWithMarker("m06-act2");
    }

    // One explicit mark, then a second one in a Then: what "explicit overrides" reads like for a helper shared by When and Then.
    [Fact]
    public async Task M04_Explicit_mark_in_a_Then_helper()
    {
        await GivenAsync("m04-seed");
        await WhenAsync("m04-act");
        await ActWithMarker("m04-then-helper");
    }
}

/// <summary>Flows the getter answers for.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Flows : Base
{
    // A consumer loop, as a host's BackgroundService under AddTestTrackingContextPropagation: detached from the test whose
    // flow started it, and naming each scenario it works for with a scope.
    private static class Consumer
    {
        private static readonly Channel<(string Name, string Id, string Label, TaskCompletionSource Done)> Work = Channel.CreateUnbounded<(string, string, string, TaskCompletionSource)>();
        private static readonly HttpClient Client = NewClient();
        private static readonly HttpClient NoAction = NewClient(trackDuringAction: false);
        private static int _started;

        public static void EnsureStarted()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            using (TestIdentityScope.Detach())
                _ = Task.Run(Loop);
        }

        private static async Task Loop()
        {
            await foreach (var item in Work.Reader.ReadAllAsync())
            {
                using (TestIdentityScope.Begin(item.Name, item.Id))
                {
                    await Client.GetAsync($"http://probe.local/{item.Label}");
                    await NoAction.GetAsync($"http://probe.local/{item.Label}-no-action-tracking");
                }
                item.Done.SetResult();
            }
        }

        public static async Task Handle(string label)
        {
            var ctx = TestContext.Current.Test!;
            var done = new TaskCompletionSource();
            await Work.Writer.WriteAsync((ctx.TestDisplayName, ctx.UniqueID, label, done));
            await done.Task;
        }
    }

    private async Task DetachedBody(string n)
    {
        Consumer.EnsureStarted();
        await GivenAsync($"{n}-seed");
        await Consumer.Handle($"{n}-consumer-in-setup");
        await WhenAsync($"{n}-act");
        await Consumer.Handle($"{n}-consumer-in-action");
        await ThenAsync($"{n}-then");
    }

    [Fact] public Task F01a_Detached_consumer_first_or_second() => DetachedBody("f01a");
    [Fact] public Task F01b_Detached_consumer_first_or_second() => DetachedBody("f01b");

    // The wiki's recipe (Tracking-Custom-Dependencies.md:677), for a flow that outlasts the write: silence a poller.
    [Fact]
    public async Task F02_Poller_silenced_with_the_public_setter()
    {
        var quiet = NewClient(trackDuringSetup: false);
        var stop = new CancellationTokenSource();
        TestPhaseContext.Current = TestPhase.Setup;
        var poller = Task.Run(async () =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                await quiet.GetAsync($"http://probe.local/f02-poll{++i}");
                await Task.Delay(10);
            }
        });
        TestPhaseContext.Current = TestPhase.Unknown;

        await GivenAsync("f02-seed");
        await Task.Delay(40);
        await WhenAsync("f02-act");
        await Task.Delay(40);
        await ThenAsync("f02-then");
        stop.Cancel();
        await poller;
    }

    // A request and its response read the phase apart: the request is dropped in Setup, its response is not.
    [Fact]
    public async Task F03_Message_straddles_the_decision_TrackDuringSetup_false()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Probe",
            ServiceName = "Bus",
            CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher,
            TrackDuringSetup = false
        });
        var uri = new Uri("bus://probe/f03-queue");
        var requested = new TaskCompletionSource();
        var decided = new TaskCompletionSource();
        Task? consumer = null;

        void Start() => consumer = Task.Run(async () =>
        {
            var id = tracker.TrackMessageRequest("bus", "Bus", uri, new { a = 1 });
            requested.SetResult();
            await decided.Task;
            tracker.TrackMessageResponse("bus", "Bus", uri, id, new { ok = true });
        });

        GivenStarts(Start);
        await requested.Task;
        await WhenAsync("f03-act");
        decided.SetResult();
        await consumer!;
        await ThenAsync("f03-then");
    }

    [GivenStep] public void GivenStarts(Action start) => start();

    // The same straddle from no step at all, forked before the When.
    [Fact]
    public async Task F03b_Call_forked_before_the_When_message_straddles()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Probe",
            ServiceName = "Bus",
            CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher,
            TrackDuringSetup = false
        });
        var uri = new Uri("bus://probe/f03b-queue");
        await GivenAsync("f03b-seed");
        TestPhaseContext.Current = TestPhase.Setup;
        var requested = new TaskCompletionSource();
        var decided = new TaskCompletionSource();
        var consumer = Task.Run(async () =>
        {
            var id = tracker.TrackMessageRequest("bus", "Bus", uri, new { a = 1 });
            requested.SetResult();
            await decided.Task;
            tracker.TrackMessageResponse("bus", "Bus", uri, id, new { ok = true });
        });
        await requested.Task;
        await WhenAsync("f03b-act");
        decided.SetResult();
        await consumer;
    }

    // A hand-made step scope whose completer is async: the chain keeps the closed step, so the test's flow reads as having outlived it.
    private sealed class HandStep : IAsyncDisposable
    {
        private readonly string _id;
        public HandStep(string id, string keyword, string text)
        {
            _id = id;
            StepCollector.StartStep(id, keyword, text, null, null);
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            StepCollector.CompleteStep(_id, true);
        }
    }

    [Fact]
    public async Task F04_Hand_steps_with_an_async_completer()
    {
        var id = TestContext.Current.Test!.UniqueID;
        await using (new HandStep(id, "Given", "hand given"))
            await Call("f04-seed");
        await using (new HandStep(id, "When", "hand when"))
            await Call("f04-act");
        await Call("f04-after");
        await Task.Run(() => Call("f04-child-flow-after"));
        await ThenAsync("f04-then");
    }

    // The same with the woven When after the hand Given.
    [Fact]
    public async Task F04b_Hand_given_async_completer_then_woven_when()
    {
        var id = TestContext.Current.Test!.UniqueID;
        await using (new HandStep(id, "Given", "hand given"))
            await Call("f04b-seed");
        await WhenAsync("f04b-act");
        await Task.Run(() => Call("f04b-child-flow-after"));
        await ThenAsync("f04b-then");
    }

    // A step started under another test's id inside this test's Given drops this test's chain.
    [Fact]
    public async Task F05_A_hand_step_of_another_id_inside_a_Given()
    {
        await GivenCallsOtherIdThenWhen("f05");
        await WhenAsync("f05-act");
    }

    [GivenStep]
    public async Task GivenCallsOtherIdThenWhen(string label)
    {
        await Call($"{label}-seed");
        StepCollector.StartStep("some-other-test", "Given", "other", null, null);
        StepCollector.CompleteStep("some-other-test", true);
        await WhenAsync($"{label}-label-under-given");
        await Call($"{label}-after-label");
    }
}
