using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using PhaseProbe;
using TestStack.BDDfy;
using Xunit;

[assembly: AssemblyFixture(typeof(PhaseProbe.Rev3.Bddfy.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]

namespace PhaseProbe.Rev3.Bddfy;

public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        Dump2.Write("rev3-bddfy");
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Rev3 probe", SeparateSetup = true });
        return ValueTask.CompletedTask;
    }
}

public class Scenarios
{
    private readonly Calls C;
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _worker;
    private int _number;
    public int Number { get => _number; set => _number = value; }

    public Scenarios()
    {
        C = new(
            new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));
        Dump2.Names[Xunit.TestContext.Current.Test!.UniqueID] = Xunit.TestContext.Current.Test!.TestDisplayName;
    }

    // ---- woven ----
    [Step] public async Task WorkerStep(string label) { await C.Make(label + "-worker-start"); await _gate.Task; await C.Make(label + "-worker-end"); }
    [Step] public async Task PlainWoven(string label) => await C.Make(label);
    [GivenStep] public async Task WovenGiven(string label) => await C.Make(label);
    [WhenStep] public async Task WovenWhen(string label) => await C.Make(label);
    public bool Always => true;
    [Step(SkipIf = nameof(Always), SkipReason = "skip")] public async Task SkippedWoven() { await Task.Yield(); }

    // ---- BDDfy steps ----
    public void Given_a_call(string label) => C.Make(label).GetAwaiter().GetResult();
    public void When_a_call(string label) => C.Make(label).GetAwaiter().GetResult();
    public void Then_a_call(string label) => C.Make(label).GetAwaiter().GetResult();
    public void Given_nothing() { }
    public async Task When_async(string label) { await Task.Yield(); await C.Make(label); }
    public async Task Then_async(string label) { await Task.Yield(); await C.Make(label); }
    public async Task Given_async(string label) { await Task.Yield(); await C.Make(label); }

    public void Given_a_sync_step_leaves_a_worker_running(string label) { _worker = WorkerStep(label); }
    public async Task Given_an_async_step_leaves_a_worker_running(string label) { await Task.Yield(); _worker = WorkerStep(label); await C.Make(label + "-given-call"); }
    public void Then_the_worker_is_released() { _gate.SetResult(); _worker!.GetAwaiter().GetResult(); }

    public async Task Given_async_with_substeps(string label)
    {
        await Task.Yield();
        await PlainWoven(label + "-plain-sub");
        await WovenWhen(label + "-when-sub");
        await C.Make(label + "-after");
    }

    public async Task When_async_fails(string label)
    {
        await C.Make(label);
        throw new InvalidOperationException("async step failed");
    }

    public void When_sync_fails(string label)
    {
        C.Make(label).GetAwaiter().GetResult();
        throw new InvalidOperationException("sync step failed");
    }

    public async Task<int> When_async_of_T(string label) { await Task.Yield(); await C.Make(label); return 42; }
    public async void When_async_void(string label) { await Task.Yield(); await C.Make(label); }

    public async Task Given_async_with_a_skipped_sub_step(string label)
    {
        await SkippedWoven();
        await C.Make(label);
    }

    public Task When_nonasync_task_returns_completed(string label) => Task.CompletedTask;
    public Task When_nonasync_task_throws(string label) => throw new InvalidOperationException("thrown by the stub");

    [Fact] public void Z01_sync_Given_leaves_a_worker_running()
    {
        this.Given(x => x.Given_a_sync_step_leaves_a_worker_running("z01"))
            .When(x => x.When_a_call("z01-when"))
            .Then(x => x.Then_a_call("z01-then"))
            .And(x => x.Then_the_worker_is_released())
            .BDDfy();
    }

    [Fact] public void Z02_async_Given_leaves_a_worker_running()
    {
        this.Given(x => x.Given_an_async_step_leaves_a_worker_running("z02"))
            .When(x => x.When_a_call("z02-when"))
            .Then(x => x.Then_a_call("z02-then"))
            .And(x => x.Then_the_worker_is_released())
            .BDDfy();
    }

    [Fact] public void Z03_async_Given_with_woven_sub_steps()
    {
        this.Given(x => x.Given_async_with_substeps("z03"))
            .When(x => x.When_a_call("z03-when"))
            .BDDfy();
    }

    [Fact] public void Z04_async_step_fails_scenario_reports_it()
    {
        try
        {
            this.Given(x => x.Given_a_call("z04-given"))
                .When(x => x.When_async_fails("z04-when"))
                .Then(x => x.Then_a_call("z04-then"))
                .BDDfy();
        }
        catch (InvalidOperationException) { }
    }

    [Fact] public void Z05_sync_step_fails_scenario_reports_it()
    {
        try
        {
            this.Given(x => x.Given_a_call("z05-given"))
                .When(x => x.When_sync_fails("z05-when"))
                .Then(x => x.Then_a_call("z05-then"))
                .BDDfy();
        }
        catch (InvalidOperationException) { }
    }

    [Fact] public void Z06_a_Task_of_T_step()
    {
        this.Given(x => x.Given_a_call("z06-given"))
            .When(x => x.When_async_of_T("z06-when"))
            .Then(x => x.Then_a_call("z06-then"))
            .BDDfy();
    }

    [Fact] public void Z07_an_async_void_step()
    {
        this.Given(x => x.Given_a_call("z07-given"))
            .When(x => x.When_async_void("z07-when"))
            .Then(x => x.Then_a_call("z07-then"))
            .BDDfy();
    }

    [Fact] public void Z08_a_skipped_sub_step_in_an_async_Given()
    {
        this.Given(x => x.Given_async_with_a_skipped_sub_step("z08-given"))
            .When(x => x.When_a_call("z08-when"))
            .BDDfy();
    }

    [Fact] public void Z09_two_examples_in_one_test()
    {
        this.Given(x => x.Given_a_call("z09-given"))
            .When(x => x.When_a_call("z09-when"))
            .WithExamples(new ExampleTable("Number") { { 1 }, { 2 } })
            .BDDfy();
    }

    [Fact] public void Z10_Given_with_no_calls_then_When()
    {
        this.Given(x => x.Given_nothing())
            .When(x => x.When_a_call("z10-when"))
            .BDDfy();
    }

    [Fact] public void Z11_nonasync_task_steps()
    {
        try
        {
            this.Given(x => x.Given_a_call("z11-given"))
                .When(x => x.When_nonasync_task_returns_completed("z11-when"))
                .And(x => x.When_nonasync_task_throws("z11-when2"))
                .Then(x => x.Then_a_call("z11-then"))
                .BDDfy();
        }
        catch (InvalidOperationException) { }
    }

    // A BDDfy scenario inside a woven parent: the executor's sync failure path, and the phase across it.
    [Step] public void Scenario_inside_a_step(string label)
    {
        try
        {
            this.Given(x => x.Given_a_call(label + "-given"))
                .When(x => x.When_sync_fails(label + "-when"))
                .BDDfy();
        }
        catch (InvalidOperationException) { }
    }

    [Fact] public async Task Z12_a_failing_sync_scenario_in_a_woven_parent()
    {
        Scenario_inside_a_step("z12");
        await PlainWoven("z12-after");
    }

    [Step] public void Async_scenario_inside_a_step(string label)
    {
        try
        {
            this.Given(x => x.Given_a_call(label + "-given"))
                .When(x => x.When_async_fails(label + "-when"))
                .BDDfy();
        }
        catch (InvalidOperationException) { }
    }

    [Fact] public async Task Z13_a_failing_async_scenario_in_a_woven_parent()
    {
        Async_scenario_inside_a_step("z13");
        await PlainWoven("z13-after");
    }

    [GivenStep] public void Arrange_with_a_failing_scenario(string label)
    {
        try
        {
            this.Given(x => x.Given_a_call(label + "-inner-given"))
                .When(x => x.When_sync_fails(label + "-inner-when"))
                .TearDownWith(x => x.A_teardown(label + "-inner-teardown"))
                .BDDfy();
        }
        catch (InvalidOperationException) { }
        C.Make(label + "-after-inner").GetAwaiter().GetResult();
        PlainWoven(label + "-sub-after-inner").GetAwaiter().GetResult();
    }

    [Fact] public async Task Z15_a_failing_sync_scenario_with_a_teardown_in_a_woven_Given_then_a_When()
    {
        Arrange_with_a_failing_scenario("z15");
        await WovenWhen("z15-when");
    }

    // TearDown after an async step, a call after .BDDfy() in the test.
    public void A_teardown(string label) => C.Make(label).GetAwaiter().GetResult();

    [Fact] public async Task Z14_woven_step_and_call_after_the_scenario_in_the_test()
    {
        this.Given(x => x.Given_a_call("z14-given"))
            .When(x => x.When_async("z14-when"))
            .TearDownWith(x => x.A_teardown("z14-teardown"))
            .BDDfy();
        await C.Make("z14-raw-after-bddfy");
        await PlainWoven("z14-woven-after-bddfy");
        await C.Make("z14-raw-after-woven");
    }
}
