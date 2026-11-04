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

namespace Rev4.B;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write("rev4-b");
        Directory.CreateDirectory(Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? ".");
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions { SpecificationsTitle = "rev4-b", SeparateSetup = true, PlantUmlRendering = PlantUmlRendering.BrowserJs });
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
    protected async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    [GivenStep] public async Task GivenAsync(string label) => await Call(label);
    [WhenStep] public async Task WhenAsync(string label) => await Call(label);
    [ThenStep] public async Task ThenAsync(string label) => await Call(label);
}

/// <summary>A real host's worker under the wiki's DetachHostedServicesFromTestIdentity, started from inside a test.</summary>
public static class RealHost
{
    public sealed record Item(string Name, string Id, string Label, TaskCompletionSource Done);

    public static readonly Channel<Item> Work = Channel.CreateUnbounded<Item>();
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
                using (TestIdentityScope.Begin(item.Name, item.Id))
                {
                    await _client.GetAsync($"http://probe.local/{item.Label}", stoppingToken);
                    await _quiet.GetAsync($"http://probe.local/{item.Label}-quiet", stoppingToken);
                }
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
        var ctx = TestContext.Current.Test!;
        var done = new TaskCompletionSource();
        await Work.Writer.WriteAsync(new Item(ctx.TestDisplayName, ctx.UniqueID, label, done));
        await done.Task;
    }
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Hosted : Base
{
    private async Task Body(string n)
    {
        RealHost.EnsureStarted();
        await GivenAsync($"{n}-seed");
        await RealHost.Handle($"{n}-worker-in-setup");
        await WhenAsync($"{n}-act");
        await RealHost.Handle($"{n}-worker-in-action");
        await ThenAsync($"{n}-then");
    }

    [Fact] public Task H01a_Worker_of_a_host_started_in_a_test() => Body("h01a");
    [Fact] public Task H01b_Worker_of_a_host_started_in_a_test() => Body("h01b");
    [Fact] public Task H01c_Worker_of_a_host_started_in_a_test() => Body("h01c");
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Flows : Base
{
    private readonly TaskCompletionSource _gate = new();

    [GivenStep]
    public async Task BackgroundGiven(string label, Task wait)
    {
        await Call($"{label}-before");
        await wait;
        await Call($"{label}-after");
    }

    [GivenStep]
    public void GivenForks(Action fork) => fork();

    // Rule 3: a Given on a flow that outlived the step it was forked inside "keeps its phase to itself". Its own step, its own write.
    [Fact]
    public async Task G01_Outlived_Given_keeps_its_phase_across_the_When()
    {
        Task? bg = null;
        GivenForks(() => bg = Task.Run(() => BackgroundGiven("g01-bg", _gate.Task)));
        await Task.Delay(30);
        await WhenAsync("g01-act");
        _gate.SetResult();
        await bg!;
        await ThenAsync("g01-then");
    }

    // A flow that reads across When, a later Given and a later When: "goes one way"?
    [Fact]
    public async Task G02_Flow_reads_across_Given_When_Given_When()
    {
        var steps = new[] { new TaskCompletionSource(), new TaskCompletionSource(), new TaskCompletionSource(), new TaskCompletionSource() };
        var acks = new[] { new TaskCompletionSource(), new TaskCompletionSource(), new TaskCompletionSource(), new TaskCompletionSource() };
        var bg = Task.Run(async () =>
        {
            for (var i = 0; i < 4; i++)
            {
                await steps[i].Task;
                await Call($"g02-bg{i + 1}");
                acks[i].SetResult();
            }
        });
        async Task Tick(int i) { steps[i].SetResult(); await acks[i].Task; }

        await GivenAsync("g02-given1");
        await Tick(0);
        await WhenAsync("g02-when1");
        await Tick(1);
        await GivenAsync("g02-given2");
        await Tick(2);
        await WhenAsync("g02-when2");
        await Tick(3);
        await bg;
    }

    // A woven step on a detached flow: rule 3 is about "a flow the framework does not name". Detached, the identity chain says nobody's.
    [Fact]
    public async Task G03_When_on_a_detached_flow_inside_a_test()
    {
        await GivenAsync("g03-seed");
        using (TestIdentityScope.Detach())
            await Task.Run(() => WhenAsync("g03-detached-when"));
        await Call("g03-after-detached");
        await Task.Run(() => Call("g03-child-after-detached"));
        await WhenAsync("g03-act");
    }

    // Tear-down calls: the test's own DisposeAsync reads Action now.
    public sealed class DisposesAsync : Base, IAsyncLifetime
    {
        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        private readonly HttpClient _noAction = NewClient(trackDuringAction: false);

        public new async ValueTask DisposeAsync()
        {
            await _noAction.GetAsync("http://probe.local/g04-cleanup-no-action-tracking");
            await Call("g04-cleanup");
        }

        [Fact]
        public async Task G04_DisposeAsync_after_When()
        {
            await GivenAsync("g04-seed");
            await WhenAsync("g04-act");
        }
    }

    // The test's own forked flow before any step, reading own Unknown, then the action: no explicit write.
    [Fact]
    public async Task G05_Plain_background_reader_with_a_TrackDuringAction_false_client()
    {
        var quiet = NewClient(trackDuringAction: false);
        var go = new TaskCompletionSource();
        var bg = Task.Run(async () =>
        {
            await quiet.GetAsync("http://probe.local/g05-bg-before");
            await go.Task;
            await quiet.GetAsync("http://probe.local/g05-bg-after");
        });
        await GivenAsync("g05-seed");
        await WhenAsync("g05-act");
        go.SetResult();
        await bg;
    }
}
