using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Probe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass, MaxParallelThreads = 8)]
[assembly: AssemblyFixture(typeof(Probe.Parallel.RunFixture))]

namespace Probe.Parallel;

public sealed class RunFixture : IDisposable
{
    public RunFixture() { Track.TestIdResolver ??= () => TestContext.Current.Test?.UniqueID; }
    public void Dispose() => Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "parallel");
}

/// <summary>Every label ends in what the rule says its phase is. A test checks its own records as it ends, so a phase that crosses tests fails it.</summary>
public abstract class Base
{
    protected static readonly HttpClient Shared = new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe", FixedNameForReceivingService = "Shop"
    }) { InnerHandler = new Answer() });

    protected static readonly MessageTracker SharedBus = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher });

    protected string Id => TestContext.Current.Test!.UniqueID;

    protected async Task C(string label)
    {
        (await Shared.GetAsync($"http://probe.local/{Id}/{label}")).EnsureSuccessStatusCode();
        SharedBus.TrackSendEvent("bus", label, new Uri($"bus://probe/{Id}/{label}"));
    }

    [GivenStep] public async Task G(string l) { await C(l); }
    [GivenStep] public void GS(string l) { C(l).GetAwaiter().GetResult(); }
    [WhenStep] public async Task W(string l) { await C(l); }
    [WhenStep] public void WS(string l) { C(l).GetAwaiter().GetResult(); }
    [ThenStep] public async Task T(string l) { await C(l); }

    [WhenStep]
    public async Task WN(string l)
    {
        await C(l + "-a-A");
        await Task.Yield();
        await G(l + "-b-A");
        GS(l + "-c-A");
        await C(l + "-d-A");
        await Task.WhenAll(G(l + "-e-A"), T(l + "-f-A"));
        await C(l + "-g-A");
    }

    [GivenStep]
    public async Task GN(string l)
    {
        await C(l + "-a-S");
        await W(l + "-b-S");
        WS(l + "-c-S");
        await Task.Run(() => T(l + "-d-S"));
        await C(l + "-e-S");
    }

    protected async Task Shape(string name)
    {
        await GN(name + "-1");
        await C(name + "-2-S");
        await WN(name + "-3");
        await C(name + "-4-A");
        await G(name + "-5-S");
        await C(name + "-6-S");
        await T(name + "-7-A");
        await C(name + "-8-A");
        Check();
    }

    private void Check()
    {
        var mine = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == Id && !l.IsDiagramMarker && l.Type == RequestResponseType.Request).ToArray();
        Assert.NotEmpty(mine);
        var wrong = mine.Where(l =>
        {
            var label = l.Uri.AbsolutePath;
            var expected = label.EndsWith("-S") ? TestPhase.Setup : label.EndsWith("-A") ? TestPhase.Action : (TestPhase?)null;
            return expected is not null && l.Phase != expected;
        }).Select(l => $"{l.Uri.AbsolutePath} read {l.Phase}").ToArray();
        Assert.True(wrong.Length == 0, string.Join("; ", wrong));
    }
}

public class P1 : Base { [Theory, InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5), InlineData(6)] public Task S(int n) => Shape($"p1{n}"); }
public class P2 : Base { [Theory, InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5), InlineData(6)] public Task S(int n) => Shape($"p2{n}"); }
public class P3 : Base { [Theory, InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5), InlineData(6)] public Task S(int n) => Shape($"p3{n}"); }
public class P4 : Base { [Theory, InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5), InlineData(6)] public Task S(int n) => Shape($"p4{n}"); }
public class P5 : Base { [Theory, InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5), InlineData(6)] public Task S(int n) => Shape($"p5{n}"); }
public class P6 : Base { [Theory, InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5), InlineData(6)] public Task S(int n) => Shape($"p6{n}"); }
public class P7 : Base { [Theory, InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5), InlineData(6)] public Task S(int n) => Shape($"p7{n}"); }
public class P8 : Base { [Theory, InlineData(1), InlineData(2), InlineData(3), InlineData(4), InlineData(5), InlineData(6)] public Task S(int n) => Shape($"p8{n}"); }
