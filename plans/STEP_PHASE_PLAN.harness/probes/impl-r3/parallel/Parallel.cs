using System.Diagnostics;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;

[assembly: AssemblyFixture(typeof(Probe.Parallel.Setup))]
#if SINGLE
[assembly: CollectionBehavior(MaxParallelThreads = 1)]
#else
[assembly: CollectionBehavior(MaxParallelThreads = 8)]
#endif
[assembly: TrackSteps]

namespace Probe.Parallel;

public class Setup : IAsyncLifetime
{
    public static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static long TaskRunTicks;
    public ValueTask InitializeAsync()
    {
#if STARVE
        ThreadPool.SetMinThreads(1, 1);
#endif
        Clock.Restart();
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        Dump.Write("parallel");
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        File.WriteAllText(Path.Combine(dir, "parallel-clock.txt"), $"run {Clock.ElapsedMilliseconds} ms\n");
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Probe parallel", SeparateSetup = true });
        return ValueTask.CompletedTask;
    }
}

/// <summary>A static client on one handler, as a fixture would hold it.</summary>
public static class Shared
{
    public static readonly HttpClient Client = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });
    public static readonly MessageTracker Tracker = new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher });
}

public abstract class ParallelStory
{
    private readonly Calls C = new(Shared.Client, Shared.Tracker);

    protected ParallelStory() => Known.Register();

    protected string L => GetType().Name.ToLowerInvariant();

    [GivenStep] public async Task WovenGiven(string l) { await Task.Delay(Random.Shared.Next(1, 8)); await C.Make(l); }

    public async Task GivenA(string l) { await Task.Delay(Random.Shared.Next(1, 8)); await C.Make(l); Track.AssertionPassed("given " + l); }
    public async Task WhenA(string l)
    {
        await Task.Delay(Random.Shared.Next(1, 8));
        await C.Make(l + "-before");
        await WovenGiven(l + "-sub");
        await C.Make(l + "-after");
        Track.AssertionPassed("when " + l);
    }
    public async Task ThenA(string l) { await Task.Delay(Random.Shared.Next(1, 8)); await C.Make(l); Track.AssertionPassed("then " + l); }
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();

    [Fact]
    public void Story() => this.Given(x => x.GivenA("g")).When(x => x.WhenA("w")).Then(x => x.ThenA("t")).BDDfy();

    public Task TaskRunStep() => Task.Run(() =>
    {
        // A sync body on Task.Run: BDDfy's Wait can inline it on a raw task, and cannot on a wrapper.
        Thread.Sleep(5);
    });

    [Fact]
    public void Inlined() => this.Given(x => x.TaskRunStep()).When(x => x.TaskRunStep()).Then(x => x.TaskRunStep()).BDDfy();
}

public class P01 : ParallelStory { }
public class P02 : ParallelStory { }
public class P03 : ParallelStory { }
public class P04 : ParallelStory { }
public class P05 : ParallelStory { }
public class P06 : ParallelStory { }
public class P07 : ParallelStory { }
public class P08 : ParallelStory { }
public class P09 : ParallelStory { }
public class P10 : ParallelStory { }
public class P11 : ParallelStory { }
public class P12 : ParallelStory { }
public class P13 : ParallelStory { }
public class P14 : ParallelStory { }
public class P15 : ParallelStory { }
public class P16 : ParallelStory { }
public class P17 : ParallelStory { }
public class P18 : ParallelStory { }
public class P19 : ParallelStory { }
public class P20 : ParallelStory { }
public class P21 : ParallelStory { }
public class P22 : ParallelStory { }
public class P23 : ParallelStory { }
public class P24 : ParallelStory { }
