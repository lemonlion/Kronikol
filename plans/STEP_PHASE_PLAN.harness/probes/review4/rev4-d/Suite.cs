using System.Threading.Channels;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rev4;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rev4.D;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write("rev4-d");
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions { SpecificationsTitle = "rev4-d", SeparateSetup = true, PlantUmlRendering = PlantUmlRendering.BrowserJs });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public abstract class Base : DiagrammedComponentTest
{
    public static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true) =>
        new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            TrackDuringSetup = trackDuringSetup,
            TrackDuringAction = trackDuringAction
        }) { InnerHandler = new Answer() });

    protected readonly HttpClient Http = NewClient();
    protected readonly HttpClient Quiet = NewClient(trackDuringSetup: false);
    protected async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();
    protected async Task CallQuiet(string label) => (await Quiet.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    [GivenStep] public async Task GivenAsync(string label) => await Call(label);
    [WhenStep] public async Task WhenAsync(string label) => await Call(label);
    [ThenStep] public async Task ThenAsync(string label) => await Call(label);
    [ThenStep] public async Task ThenFails(string label) { await Call(label); Assert.Fail("o02 fails in its Then"); }
    [GivenStep] public void GivenStarts(Action start) => start();
}

/// <summary>Responses that lost their request, through the whole report pipeline.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Orphans : Base
{
    private static MessageTracker Tracker() => new(new MessageTrackerOptions
    {
        CallerName = "Probe",
        ServiceName = "Bus",
        CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher,
        TrackDuringSetup = false
    });

    private async Task Straddle(string n, Func<string, Task> then)
    {
        var tracker = Tracker();
        var uri = new Uri($"bus://probe/{n}-queue");
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var decided = new TaskCompletionSource();
        Task? c1 = null, c2 = null;

        Task Consumer(TaskCompletionSource requested, string payload) => Task.Run(async () =>
        {
            var id = tracker.TrackMessageRequest("bus", "Bus", uri, new { payload });
            requested.SetResult();
            await decided.Task;
            tracker.TrackMessageResponse("bus", "Bus", uri, id, new { ok = payload });
        });

        GivenStarts(() =>
        {
            c1 = Consumer(first, "one");
            c2 = Consumer(second, "two");
        });
        await first.Task;
        await second.Task;
        await WhenAsync($"{n}-act");
        decided.SetResult();
        await c1!;
        await c2!;
        await then($"{n}-then");
    }

    [Fact] public Task O01_Two_responses_lose_their_requests() => Straddle("o01", ThenAsync);
    [Fact] public Task O02_Two_responses_lose_their_requests_and_the_Then_fails() => Straddle("o02", ThenFails);
}

/// <summary>A suite's own bridge from its own framework, through the public step API.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Bridge : Base
{
    private static string Id => TestContext.Current.Test!.UniqueID;

    // The natural way to write it: an async helper around a body. Whatever StartStep writes stays in the helper's flow.
    private static async Task Step(string keyword, string text, Func<Task> body)
    {
        StepCollector.StartStep(Id, keyword, text, null, null);
        try
        {
            await body();
            StepCollector.CompleteStep(Id, true);
        }
        catch (Exception e)
        {
            StepCollector.CompleteStep(Id, false, e.Message);
            throw;
        }
    }

    [Fact]
    public async Task B01_Async_helper_bridge()
    {
        await Step("Given", "b01 given", () => Call("b01-seed"));
        await CallQuiet("b01-between-setup");
        await Step("When", "b01 when", () => Call("b01-act"));
        await CallQuiet("b01-between-action");
        await Step("Then", "b01 then", () => Call("b01-then"));
    }

    [Fact]
    public async Task B02_Ambient_overloads_on_the_test_flow()
    {
        StepCollector.StartStep("Given", "b02 given", null, null);
        await Call("b02-seed");
        StepCollector.CompleteStep(true);
        await CallQuiet("b02-between-setup");
        StepCollector.StartStep("When", "b02 when", null, null);
        await Call("b02-act");
        StepCollector.CompleteStep(true);
        await CallQuiet("b02-between-action");
    }

    [Fact]
    public async Task B03_Keywords_in_capitals()
    {
        await Step("GIVEN", "b03 given", () => Call("b03-seed"));
        await Step("WHEN", "b03 when", () => Call("b03-act"));
        await Call("b03-after");
    }

    [Fact]
    public async Task B03b_Keywords_in_capitals_on_the_test_flow()
    {
        StepCollector.StartStep(Id, "GIVEN", "b03b given", null, null);
        await Call("b03b-seed");
        StepCollector.CompleteStep(Id, true);
        StepCollector.StartStep(Id, "WHEN", "b03b when", null, null);
        await Call("b03b-act");
        StepCollector.CompleteStep(Id, true);
        await Call("b03b-after");
    }

    [Fact]
    public async Task B04_Other_words_of_a_bridge()
    {
        StepCollector.StartStep(Id, "Setup", "b04 setup", null, null);
        await Call("b04-setup");
        StepCollector.CompleteStep(Id, true);
        StepCollector.StartStep(Id, "Scenario", "b04 scenario", null, null);
        await Call("b04-scenario");
        StepCollector.CompleteStep(Id, true);
        StepCollector.StartStep(Id, "Act", "b04 act", null, null);
        await Call("b04-act");
        StepCollector.CompleteStep(Id, true);
    }

    // Started and closed on flows of their own, as hooks a framework awaits in turn: nothing reaches the test's flow.
    [Fact]
    public async Task B05_Start_and_complete_on_child_flows()
    {
        var id = Id;
        await Task.Run(() => StepCollector.StartStep(id, "Given", "b05 given", null, null));
        await Call("b05-seed");
        await Task.Run(() => StepCollector.CompleteStep(id, true));
        await Task.Run(() => StepCollector.StartStep(id, "When", "b05 when", null, null));
        await Call("b05-act");
        await Task.Run(() => StepCollector.CompleteStep(id, true));
        await Call("b05-after");
    }

    // A runner of the suite's own: its own threads, a scope naming the test. The resolver answers nothing there.
    [Fact]
    public async Task B06_Own_runner_on_a_thread_under_a_scope()
    {
        var ctx = TestContext.Current.Test!;
        var (name, id) = (ctx.TestDisplayName, ctx.UniqueID);
        var done = new TaskCompletionSource();
        var http = Http;
        var quiet = Quiet;
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using (TestIdentityScope.Begin(name, id))
                    {
                        StepCollector.StartStep(id, "Given", "b06 given", null, null);
                        await http.GetAsync("http://probe.local/b06-seed");
                        await quiet.GetAsync("http://probe.local/b06-seed-quiet");
                        StepCollector.CompleteStep(id, true);
                        StepCollector.StartStep(id, "When", "b06 when", null, null);
                        await http.GetAsync("http://probe.local/b06-act");
                        await quiet.GetAsync("http://probe.local/b06-act-quiet");
                        StepCollector.CompleteStep(id, true);
                    }
                    done.SetResult();
                }
                catch (Exception e) { done.SetException(e); }
            });
        }
        await done.Task;
    }

    // One test, a bridge's keyword-free wrapper, and woven steps inside it.
    [Fact]
    public async Task B07_Bridge_wrapper_with_no_keyword_around_woven_steps()
    {
        StepCollector.StartStep(Id, null, "b07 wrapper", null, null);
        await GivenAsync("b07-seed");
        await WhenAsync("b07-act");
        StepCollector.CompleteStep(Id, true);
        await Call("b07-after");
    }

    // A When of the bridge's with a Given under it, and a Given of the bridge's that a When follows: the hand step and the woven ones.
    [Fact]
    public async Task B08_Bridge_When_with_a_woven_Given_under_it()
    {
        StepCollector.StartStep(Id, "When", "b08 when", null, null);
        await GivenAsync("b08-label");
        await Call("b08-in-when");
        StepCollector.CompleteStep(Id, true);
    }
}

/// <summary>A hosted worker under the wiki's detach, with no scope at all: its calls are the run's background.</summary>
public static class NoScopeHost
{
    public sealed record Item(string Label, TaskCompletionSource Done);
    private static readonly Channel<Item> Work = Channel.CreateUnbounded<Item>();
    private static IHost? _host;
    private static readonly object Gate = new();

    public sealed class Worker : BackgroundService
    {
        private readonly HttpClient _client = Base.NewClient();
        private readonly HttpClient _quiet = Base.NewClient(trackDuringAction: false);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var item in Work.Reader.ReadAllAsync(stoppingToken))
            {
                await _client.GetAsync($"http://probe.local/{item.Label}", stoppingToken);
                await _quiet.GetAsync($"http://probe.local/{item.Label}-quiet", stoppingToken);
                item.Done.SetResult();
            }
        }
    }

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_host is not null) return;
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddHostedService<Worker>();
            builder.Services.DetachHostedServicesFromTestIdentity();
            _host = builder.Build();
            _host.StartAsync().GetAwaiter().GetResult();
        }
    }

    public static async Task Handle(string label)
    {
        var done = new TaskCompletionSource();
        await Work.Writer.WriteAsync(new Item(label, done));
        await done.Task;
    }
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Hosted : Base
{
    private async Task Body(string n)
    {
        NoScopeHost.EnsureStarted();
        await GivenAsync($"{n}-seed");
        await NoScopeHost.Handle($"{n}-worker-in-setup");
        await WhenAsync($"{n}-act");
        await NoScopeHost.Handle($"{n}-worker-in-action");
    }

    [Fact] public Task H02a_Detached_worker_with_no_scope() => Body("h02a");
    [Fact] public Task H02b_Detached_worker_with_no_scope() => Body("h02b");
}

/// <summary>
/// A flow forked at a test's start that outlives the test, not detached: the wiki's Background-Thread-Correlation "Expired" case. Its calls after
/// the test's end are re-attributed at report time to no scenario, and list under the report's Background calls.
/// </summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Outliving : Base
{
    private static readonly HttpClient NoActionTracking = NewClient(trackDuringAction: false);
    private static readonly CancellationTokenSource Stop = new();
    private static Task? _poller;
    private static int _started;

    private async Task Body(string n)
    {
        var first = Interlocked.Exchange(ref _started, 1) == 0;
        if (first)
        {
            _poller = Task.Run(async () =>
            {
                var i = 0;
                while (!Stop.IsCancellationRequested)
                {
                    await NoActionTracking.GetAsync($"http://probe.local/e01-poll{++i}");
                    await Task.Delay(25);
                }
            });
        }

        await GivenAsync($"{n}-seed");
        await WhenAsync($"{n}-act");

        if (!first)
        {
            await Task.Delay(300);
            Stop.Cancel();
            await _poller!;
        }
    }

    [Fact] public Task E01a_Poller_outlives_its_test() => Body("e01a");
    [Fact] public Task E01b_Poller_outlives_its_test() => Body("e01b");
}
