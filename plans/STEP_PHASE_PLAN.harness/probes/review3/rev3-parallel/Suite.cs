using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(PhaseProbe.Rev3.Parallel.RunFixture))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerClass, MaxParallelThreads = 8)]

namespace PhaseProbe.Rev3.Parallel;

public sealed class RunFixture : IDisposable
{
    public RunFixture() { Track.TestIdResolver ??= () => TestContext.Current.Test?.UniqueID; }
    public void Dispose() => Dump2.Write("rev3-parallel", withPlantUml: false);
}

/// <summary>Everything shared: one handler per kind, across every test of every class, running in parallel.</summary>
public static class Shared
{
    public static readonly AsyncLocal<string?> StepType = new();

    private static HttpClient Client(bool trackSetup, Func<string?>? fetcher) => new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
    {
        CallerName = "Probe",
        FixedNameForReceivingService = "Shop",
        TrackDuringSetup = trackSetup,
        CurrentStepTypeFetcher = fetcher,
    }) { InnerHandler = new AnswerEverything() });

    private static MessageTracker Tracker() => new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher });

    public static readonly Calls Woven = new(Client(true, null), Tracker());
    public static readonly Calls WovenNoSetup = new(Client(false, null), Tracker());
    public static readonly Calls WithFetcher = new(Client(true, () => StepType.Value), Tracker());
}

public abstract class Base
{
    protected Base() { Dump2.Names[TestContext.Current.Test!.UniqueID] = TestContext.Current.Test!.TestDisplayName; }

    [GivenStep] public async Task GivenCall(string l) { await Task.Delay(Random.Shared.Next(1, 8)); await Shared.Woven.Make(l); }
    [WhenStep] public async Task WhenCall(string l) { await Task.Delay(Random.Shared.Next(1, 8)); await Shared.Woven.Make(l); }
    [ThenStep] public async Task ThenCall(string l) { await Task.Delay(Random.Shared.Next(1, 8)); await Shared.WovenNoSetup.Make(l); }

    protected async Task WovenShape(string prefix)
    {
        await GivenCall(prefix + "-given");
        await WhenCall(prefix + "-when");
        await ThenCall(prefix + "-then");
    }

    // No steps at all: only a handler's fetcher knows, as a custom CurrentStepTypeFetcher does.
    protected static async Task FetcherShape(string prefix)
    {
        Shared.StepType.Value = "GIVEN";
        await Shared.WithFetcher.Make(prefix + "-given");
        Shared.StepType.Value = "WHEN";
        await Shared.WithFetcher.Make(prefix + "-when");
        await Shared.WithFetcher.Make(prefix + "-when2");
    }
}

public class A : Base
{
    [Fact] public Task A1() => WovenShape("a1"); [Fact] public Task A2() => WovenShape("a2"); [Fact] public Task A3() => WovenShape("a3");
    [Fact] public Task A4() => FetcherShape("a4"); [Fact] public Task A5() => FetcherShape("a5");
}
public class B : Base
{
    [Fact] public Task B1() => WovenShape("b1"); [Fact] public Task B2() => WovenShape("b2"); [Fact] public Task B3() => WovenShape("b3");
    [Fact] public Task B4() => FetcherShape("b4"); [Fact] public Task B5() => FetcherShape("b5");
}
public class C : Base
{
    [Fact] public Task C1() => WovenShape("c1"); [Fact] public Task C2() => WovenShape("c2"); [Fact] public Task C3() => WovenShape("c3");
    [Fact] public Task C4() => FetcherShape("c4"); [Fact] public Task C5() => FetcherShape("c5");
}
public class D : Base
{
    [Fact] public Task D1() => WovenShape("d1"); [Fact] public Task D2() => WovenShape("d2"); [Fact] public Task D3() => WovenShape("d3");
    [Fact] public Task D4() => FetcherShape("d4"); [Fact] public Task D5() => FetcherShape("d5");
}
