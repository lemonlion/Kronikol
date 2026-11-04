using System.Net;
using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Tracking;
using TestStack.BDDfy;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(Probe.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Probe;

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
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "BDDfy" });
        return ValueTask.CompletedTask;
    }
}

public sealed class Fake : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(2, ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
    }
}

public static class Api
{
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Test" }) { InnerHandler = new Fake() }) { BaseAddress = new Uri("http://svc.local") };
    public static Task Call(string where) => Client.GetAsync("/" + where);
    public static void CallSync(string where) => Client.GetAsync("/" + where).GetAwaiter().GetResult();
}

// conventions: Setup, TearDown, Given/When/Then by method name, with calls in each
public class ConventionScenario
{
    public void Setup() { Api.CallSync("in-Setup"); InSetupStep(); }
    public void GivenSomething() { Api.CallSync("in-Given"); InGivenStep(); }
    public void WhenAction() { Api.CallSync("in-When"); }
    public void ThenResult() { Api.CallSync("in-Then"); }
    public void TearDown() { Api.CallSync("in-TearDown"); InTearDownStep(); }

    [Step] void InSetupStep() { Api.CallSync("in-SetupSub"); }
    [Step] void InGivenStep() { Api.CallSync("in-GivenSub"); }
    [Step] void InTearDownStep() { Api.CallSync("in-TearDownSub"); }
}

public class Tests
{
    int _v;
    bool _skip = true;

    [Fact] public void B01_Convention_scenario_with_setup_and_teardown() => new ConventionScenario().BDDfy("Convention");

    [Fact] public void B02_Fluent_baseline()
    {
        this.Given(_ => GivenA()).When(_ => WhenB()).Then(_ => ThenC()).BDDfy("Fluent baseline");
    }
    void GivenA() { Api.CallSync("in-A"); Sub(); }
    void WhenB() { Api.CallSync("in-B"); }
    void ThenC() { Api.CallSync("in-C"); }
    [Step] void Sub() { Api.CallSync("in-Sub"); }

    [Fact] public void B03_Async_steps()
    {
        this.Given(_ => GivenAsync()).When(_ => WhenAsync()).Then(_ => ThenC()).BDDfy("Async steps");
    }
    async Task GivenAsync() { await Api.Call("in-GivenAsync"); await SubAsync(); await Api.Call("in-GivenAsync-after"); }
    async Task WhenAsync() { await Task.Delay(1); await Api.Call("in-WhenAsync"); }
    [Step] async Task SubAsync() { await Task.Delay(1); await Api.Call("in-SubAsync"); }

    [Fact] public void B04_Failing_step()
    {
        this.Given(_ => GivenA()).When(_ => WhenBoom()).Then(_ => ThenC()).BDDfy("Failing step");
    }
    void WhenBoom() { Api.CallSync("in-Boom"); throw new InvalidOperationException("boom in When"); }

    [Fact] public void B05_Failing_sub_step()
    {
        this.Given(_ => GivenA()).When(_ => WhenSubBoom()).Then(_ => ThenC()).BDDfy("Failing sub step");
    }
    void WhenSubBoom() { Api.CallSync("in-WhenSubBoom"); SubBoom(); }
    [Step] void SubBoom() { Api.CallSync("in-SubBoom"); throw new InvalidOperationException("boom in sub-step"); }

    [Fact] public void B06_Example_table()
    {
        this.Given(_ => GivenValue(_v))
            .When(_ => WhenRowAction(_v))
            .Then(_ => ThenRowResult(_v))
            .WithExamples(new ExampleTable("_v") { 1, 2, 3 })
            .BDDfy("Example table");
    }
    void GivenValue(int v) { Api.CallSync("in-GivenValue-" + _v); RowSub(); }
    void WhenRowAction(int v) { Api.CallSync("in-WhenRow-" + _v); }
    void ThenRowResult(int v) { Api.CallSync("in-ThenRow-" + _v); }
    [Step] void RowSub() { Api.CallSync("in-RowSub-" + _v); }

    [Fact] public void B07_Step_method_outside_any_framework_step()
    {
        Outside();
        this.Given(_ => GivenA()).Then(_ => ThenC()).BDDfy("Step outside");
    }
    [Step] void Outside() { Api.CallSync("in-Outside"); }

    [Fact] public void B08_Bypass_inside_a_step()
    {
        this.Given(_ => GivenBypassing()).Then(_ => ThenC()).BDDfy("Bypass inside");
    }
    void GivenBypassing() { Api.CallSync("in-Bypassing-before"); Maybe(); Api.CallSync("in-Bypassing-after"); Sub(); }
    [Step(SkipIf = nameof(_skip), SkipReason = "no")] void Maybe() { Api.CallSync("never"); }

    [Fact] public void B09_Async_bypass_inside_a_step()
    {
        this.Given(_ => GivenBypassingAsync()).Then(_ => ThenC()).BDDfy("Async bypass inside");
    }
    async Task GivenBypassingAsync() { await Api.Call("in-BypassingAsync-before"); await MaybeAsync(); await Api.Call("in-BypassingAsync-after"); await SubAsync(); }
    [Step(SkipIf = nameof(_skip), SkipReason = "no")] async Task MaybeAsync() { await Api.Call("never"); }

    [Fact] public void B10_Parallel_sub_steps_in_a_step()
    {
        this.Given(_ => GivenParallel()).Then(_ => ThenC()).BDDfy("Parallel subs");
    }
    async Task GivenParallel() { await Task.WhenAll(P1(), P2()); }
    [Step] async Task P1() { await Task.Delay(5); await Api.Call("in-P1"); await Task.Delay(30); }
    [Step] async Task P2() { await Task.Delay(15); await Api.Call("in-P2"); await Task.Delay(30); }

    [Fact] public void B11_Detached_task_into_next_step()
    {
        this.Given(_ => Start()).When(_ => Release()).Then(_ => ThenC()).BDDfy("Detached");
        _t!.GetAwaiter().GetResult();
    }
    Task? _t; readonly TaskCompletionSource _go = new();
    void Start() { _t = Task.Run(async () => { await _go.Task; await Api.Call("detached-from-Start"); }); }
    void Release() { _go.SetResult(); Thread.Sleep(50); Api.CallSync("in-Release"); }

    [Fact] public void B12_Hidden_setup_failure()
    {
        new SetupThrows().BDDfy("Setup throws");
    }

    [Fact] public void B13_Example_table_second_row_fails()
    {
        this.Given(_ => GivenValue(_v))
            .When(_ => WhenRowFails(_v))
            .Then(_ => ThenRowResult(_v))
            .WithExamples(new ExampleTable("_v") { 1, 2, 3 })
            .BDDfy("Example table second row fails");
    }
    void WhenRowFails(int v) { Api.CallSync("in-WhenRowFails-" + _v); if (_v == 2) throw new InvalidOperationException("row 2 failed"); }

    [Fact] public void B14_Async_step_with_assertion_and_attachment_after_await()
    {
        this.Given(_ => GivenAsyncAsserts()).Then(_ => ThenC()).BDDfy("Async asserts");
    }
    async Task GivenAsyncAsserts() { await Task.Delay(5); Track.That(() => Assert.True(true), "assert-after-await"); var p = Path.Combine(Path.GetTempPath(), "bd-att.txt"); File.WriteAllText(p, "x"); Track.Attachment(p, "attach-after-await"); await Api.Call("in-GivenAsyncAsserts"); }

    [Fact] public void B15_Async_step_fails_after_a_sub_step()
    {
        this.Given(_ => GivenAsyncFails()).Then(_ => ThenC()).BDDfy("Async fails");
    }
    async Task GivenAsyncFails() { await Task.Delay(5); await SubAsync(); await Api.Call("in-GivenAsyncFails"); throw new InvalidOperationException("async step failed"); }

    [Fact] public void B16_Sync_step_runs_async_sub_step_blocking()
    {
        this.Given(_ => GivenBlocks()).Then(_ => ThenC()).BDDfy("Blocking");
    }
    void GivenBlocks() { SubAsync().GetAwaiter().GetResult(); Api.CallSync("in-GivenBlocks-after"); }
}

public class SetupThrows
{
    public void Setup() { Api.CallSync("in-SetupThrows"); throw new InvalidOperationException("setup threw"); }
    public void GivenSomething() { Api.CallSync("in-Given-never"); }
    public void ThenResult() { Api.CallSync("in-Then-never"); }
}
