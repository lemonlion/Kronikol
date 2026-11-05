using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;

[assembly: AssemblyFixture(typeof(Probe.Executors.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace Probe.Executors;

/// <summary>A consumer's executor, set before Kronikol's, so Kronikol's wraps it.</summary>
public sealed class ConsumerInner(IStepExecutor inner) : IStepExecutor
{
    public static string Mode = "pass";

    public object Execute(Step step, object testObject)
    {
        switch (Mode)
        {
            case "blocks":
            {
                // waits itself and returns nothing, so BDDfy never sees a task
                var r = inner.Execute(step, testObject);
                if (r is Task t) t.GetAwaiter().GetResult();
                return null!;
            }
            case "wraps":
            {
                var r = inner.Execute(step, testObject);
                return r is Task t ? Wrap(t) : r;
            }
            case "retries":
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        var r = inner.Execute(step, testObject);
                        if (r is Task t) t.GetAwaiter().GetResult();
                        return r;
                    }
                    catch when (attempt < 2)
                    {
                    }
                }
            }
            default:
                return inner.Execute(step, testObject);
        }
    }

    private static async Task Wrap(Task t) => await t;
}

/// <summary>A consumer's executor, set after Kronikol's, so it wraps Kronikol's.</summary>
public sealed class ConsumerOuter(IStepExecutor inner, string mode) : IStepExecutor
{
    public object Execute(Step step, object testObject)
    {
        switch (mode)
        {
            case "typed":
            {
                var r = inner.Execute(step, testObject);
                if (r is Task) Known.Note($"outer saw {r.GetType().Name}{(r.GetType().IsGenericType ? "<" + r.GetType().GetGenericArguments()[0].Name + ">" : "")} for '{step.Title}'");
                return r;
            }
            case "timeout":
            {
                var r = inner.Execute(step, testObject);
                if (r is Task t && !t.Wait(50)) throw new TimeoutException("step timed out: " + step.Title);
                return r;
            }
            case "drops":
                inner.Execute(step, testObject);
                return null!;
            case "retries":
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        var r = inner.Execute(step, testObject);
                        if (r is Task t) t.GetAwaiter().GetResult();
                        return r;
                    }
                    catch when (attempt < 2)
                    {
                    }
                }
            }
            case "forks":
                // runs the step on a thread of its own and hands BDDfy that thread's task
                return Task.Run(() => inner.Execute(step, testObject) is Task t ? t : Task.CompletedTask);
            default:
                return inner.Execute(step, testObject);
        }
    }
}

public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        Configurator.StepExecutor = new ConsumerInner(Configurator.StepExecutor);
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        Dump.Write("executors");
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Probe executors", SeparateSetup = true });
        return ValueTask.CompletedTask;
    }
}

public class Stories
{
    private readonly Calls C;
    private int _attempts;

    public Stories()
    {
        Known.Register();
        C = new Calls(
            new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));
        ConsumerInner.Mode = "pass";
    }

    [GivenStep] public async Task WovenGiven(string l) => await C.Make(l);
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task GivenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task WhenA(string l) { await Task.Yield(); await C.Make(l + "-before"); await WovenGiven(l + "-sub"); await C.Make(l + "-after"); Track.AssertionPassed("when " + l); }
    public async Task ThenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task<int> WhenT(string l) { await Task.Yield(); await C.Make(l); return 42; }
    public async Task WhenSlow(string l) { await Task.Yield(); await C.Make(l + "-before"); await WovenGiven(l + "-sub"); await Task.Delay(300); await C.Make(l + "-after"); }
    public async Task WhenFlaky(string l) { var attempt = ++_attempts; await Task.Yield(); await C.Make($"{l}-attempt{attempt}"); await WovenGiven($"{l}-sub{attempt}"); if (attempt == 1) throw new InvalidOperationException("first attempt"); }

    private static void WithOuter(string mode, Action body)
    {
        var saved = Configurator.StepExecutor;
        Configurator.StepExecutor = new ConsumerOuter(saved, mode);
        try { body(); } finally { Configurator.StepExecutor = saved; }
    }

    private void Story(string l) =>
        this.Given(x => x.GivenA(l + "-g")).When(x => x.WhenA(l + "-w")).Then(x => x.ThenA(l + "-t")).BDDfy();

    [Fact] public void I0_inner_passes_through() => Story("i0");
    [Fact] public void I1_inner_blocks_and_returns_nothing() { ConsumerInner.Mode = "blocks"; Story("i1"); }
    [Fact] public void I2_inner_wraps_the_task() { ConsumerInner.Mode = "wraps"; Story("i2"); }
    [Fact] public void I3_inner_retries_a_step()
    {
        ConsumerInner.Mode = "retries";
        this.Given(x => x.GivenA("i3-g")).When(x => x.WhenFlaky("i3-w")).Then(x => x.ThenA("i3-t")).BDDfy();
    }

    [Fact] public void E1_outer_reads_the_task_type() => WithOuter("typed", () =>
        this.Given(x => x.GivenA("e1-g")).When(x => x.WhenT("e1-w")).Then(x => x.ThenA("e1-t")).BDDfy());

    [Fact] public void E2_outer_gives_a_step_a_time_limit() => WithOuter("timeout", () =>
    {
        try
        {
            this.Given(x => x.GivenA("e2-g")).When(x => x.WhenSlow("e2-w")).Then(x => x.ThenA("e2-t")).TearDownWith(x => x.GivenS("e2-td")).BDDfy();
        }
        finally
        {
            Thread.Sleep(400);
        }
    });

    [Fact] public void E3_outer_drops_the_task()
    {
        WithOuter("drops", () => this.Given(x => x.GivenA("e3-g")).When(x => x.WhenA("e3-w")).Then(x => x.ThenA("e3-t")).BDDfy());
        Thread.Sleep(200);
    }

    [Fact] public void E4_outer_retries_a_step() => WithOuter("retries", () =>
        this.Given(x => x.GivenA("e4-g")).When(x => x.WhenFlaky("e4-w")).Then(x => x.ThenA("e4-t")).BDDfy());

    [Fact] public void E5_outer_runs_each_step_on_a_thread_of_its_own() => WithOuter("forks", () => Story("e5"));
}
