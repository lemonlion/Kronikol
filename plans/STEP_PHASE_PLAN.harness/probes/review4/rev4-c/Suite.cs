using System.Text;
using Kronikol;
using Kronikol.PlantUml;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev4;
using Xunit;

[assembly: TrackSteps]

namespace Rev4.C;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write("rev4-c");
        var o = new StringBuilder();
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => !l.TrackingIgnore).ToArray();
        foreach (var test in logs.GroupBy(l => l.TestId).OrderBy(g => g.FirstOrDefault(l => !l.IsDiagramMarker)?.TestName ?? g.Key, StringComparer.Ordinal))
        {
            var name = test.FirstOrDefault(l => !l.IsDiagramMarker)?.TestName ?? test.Key;
            if (!name.Contains(".C0")) continue;
            o.AppendLine("## " + Skeleton.Short(name));
            o.AppendLine(" collapse, no partition:");
            Skeleton.AppendDiagrams(o, test, separate: false, collapse: true);
            o.AppendLine(" no collapse, split at 1500:");
            Skeleton.AppendDiagrams(o, test, separate: false, collapse: false, maxEncoded: 1500);
        }
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? ".";
        File.WriteAllText(Path.Combine(dir, "rev4-c-diagrams.txt"), o.ToString());
        var parallel = new StringBuilder();
        foreach (var kv in Soak.Results.OrderBy(k => k.Key, StringComparer.Ordinal))
            parallel.AppendLine($"{kv.Key}: {kv.Value}");
        File.WriteAllText(Path.Combine(dir, "rev4-c-parallel.txt"), parallel.ToString());
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public abstract class Base : DiagrammedComponentTest
{
    public static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true, Func<HttpMessageHandler>? inner = null) =>
        new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            TrackDuringSetup = trackDuringSetup,
            TrackDuringAction = trackDuringAction
        }) { InnerHandler = inner?.Invoke() ?? new Answer() });

    protected readonly HttpClient Http = NewClient();
    protected async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    [GivenStep] public async Task GivenAsync(string label) => await Call(label);
    [WhenStep] public async Task WhenAsync(string label) => await Call(label);
    [WhenStep] public void WhenNoCall() { }
    [ThenStep] public async Task ThenAsync(string label) => await Call(label);
}

[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Held : Base
{
    private readonly MessageTracker _bus = new(new MessageTrackerOptions
    {
        CallerName = "Service",
        ServiceName = "Bus",
        CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher,
        Verbosity = MessageTrackerVerbosity.Detailed,
        SetupVerbosity = MessageTrackerVerbosity.Summarised,
        ActionVerbosity = MessageTrackerVerbosity.Detailed
    });

    // What a service's own flow is: no phase, identity from a scope, the verbosity picked by position.
    private async Task OnTheServicesFlow(Action work)
    {
        var ctx = TestContext.Current.Test!;
        Task t;
        using (ExecutionContext.SuppressFlow())
            t = Task.Run(() =>
            {
                using (TestIdentityScope.Begin(ctx.TestDisplayName, ctx.UniqueID))
                    work();
            });
        await t;
    }

    private void Publish(string queue, string body)
    {
        var uri = new Uri($"bus://probe/{queue}");
        var id = _bus.TrackMessageRequest("bus", "Bus", uri, new { body });
        _bus.TrackMessageResponse("bus", "Bus", uri, id, new { ack = body });
    }

    [Fact]
    public async Task C01_Identical_service_calls_after_the_When()
    {
        await GivenAsync("c01-seed");
        await WhenAsync("c01-act");
        await OnTheServicesFlow(() => { Publish("c01-q", "ACTION-PAYLOAD"); Publish("c01-q", "ACTION-PAYLOAD"); });
    }

    [Fact]
    public async Task C02_Identical_service_calls_across_the_When()
    {
        await GivenAsync("c02-seed");
        await OnTheServicesFlow(() => { Publish("c02-q", "SETUP-PAYLOAD"); Publish("c02-q", "SETUP-PAYLOAD"); });
        WhenNoCall();
        await OnTheServicesFlow(() => { Publish("c02-q", "ACTION-PAYLOAD"); Publish("c02-q", "ACTION-PAYLOAD"); });
    }

    [Fact]
    public async Task C03_One_service_call_each_side_of_the_When()
    {
        await GivenAsync("c03-seed");
        await OnTheServicesFlow(() => Publish("c03-q", "SETUP-PAYLOAD"));
        WhenNoCall();
        await OnTheServicesFlow(() => Publish("c03-q", "ACTION-PAYLOAD"));
    }

    // Delimiters off, a Given whose call fills the diagram, a When with no call.
    [Fact]
    public async Task C04_Big_given_then_a_When_with_no_call_delimiters_off()
    {
        var before = StepCollector.Options;
        StepCollector.Options = before with { ShowStepDelimiters = false };
        try
        {
            var big = NewClient(inner: () => new BigAnswer());
            await GivenBig(big);
            WhenNoCall();
        }
        finally { StepCollector.Options = before; }
    }

    [GivenStep] public async Task GivenBig(HttpClient big) => await big.GetAsync("http://probe.local/c04-big");

    private sealed class BigAnswer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            for (var i = 0; i < 200; i++) text.Append("key").Append(i).Append(" = ").Append(Guid.NewGuid()).Append('\n');
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(text.ToString()) });
        }
    }

    // The recipe in the middle of a test, on the test's flow, and Reset.
    [Fact]
    public async Task R02_Recipe_and_Reset_after_a_When_on_the_flow()
    {
        var quiet = NewClient(trackDuringSetup: false);
        await GivenAsync("r02-given");
        await WhenAsync("r02-when");
        TestPhaseContext.Current = TestPhase.Setup;
        await quiet.GetAsync("http://probe.local/r02-reseed");
        await Task.Run(() => quiet.GetAsync("http://probe.local/r02-reseed-child"));
        TestPhaseContext.Current = TestPhase.Unknown;
        await Call("r02-after-unknown");
        TestPhaseContext.Reset();
        await Call("r02-after-reset");
        await ThenAsync("r02-then");
        await Call("r02-after-then");
        TestPhaseContext.Reset();
        await Call("r02-after-reset-2");
    }
}

// The wiki's recipe exactly, per test (xUnit v3's InitializeAsync has the test's context).
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public sealed class Recipe : Base, IAsyncLifetime
{
    private readonly HttpClient _quiet = NewClient(trackDuringSetup: false);

    public async ValueTask InitializeAsync()
    {
        TestPhaseContext.Current = TestPhase.Setup;
        await _quiet.GetAsync("http://probe.local/r01-clear");
        await _quiet.GetAsync("http://probe.local/r01-seed");
        TestPhaseContext.Current = TestPhase.Unknown;
    }

    public new ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task R01_Wiki_recipe_in_InitializeAsync_then_steps()
    {
        await GivenAsync("r01-given");
        await WhenAsync("r01-when");
    }
}

public static class Soak
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Results = new();

    public static async Task Run(Func<string, Task> given, Func<string, Task> when, string name)
    {
        var read = new StringBuilder();
        void Note(string at) => read.Append(at).Append('=').Append(TestPhaseContext.Current).Append(' ');
        for (var i = 0; i < 20; i++)
        {
            await given(name);
            Note("afterGiven");
            await Task.Yield();
            await when(name);
            Note("afterWhen");
            await Task.Delay(1);
        }
        var wrong = read.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Count(r => r.StartsWith("afterGiven") ? !r.EndsWith("=Setup") : !r.EndsWith("=Action"));
        Results[name] = $"wrong {wrong} of 40";
    }
}

public class P1 : Base { [Fact] public Task P() => Soak.Run(GivenAsync, WhenAsync, "P1"); }
public class P2 : Base { [Fact] public Task P() => Soak.Run(GivenAsync, WhenAsync, "P2"); }
public class P3 : Base { [Fact] public Task P() => Soak.Run(GivenAsync, WhenAsync, "P3"); }
public class P4 : Base { [Fact] public Task P() => Soak.Run(GivenAsync, WhenAsync, "P4"); }
public class P5 : Base { [Fact] public Task P() => Soak.Run(GivenAsync, WhenAsync, "P5"); }
public class P6 : Base { [Fact] public Task P() => Soak.Run(GivenAsync, WhenAsync, "P6"); }
public class P7 : Base { [Fact] public Task P() => Soak.Run(GivenAsync, WhenAsync, "P7"); }
public class P8 : Base { [Fact] public Task P() => Soak.Run(GivenAsync, WhenAsync, "P8"); }
