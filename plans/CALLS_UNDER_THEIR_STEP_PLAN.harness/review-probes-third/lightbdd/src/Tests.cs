using System.Net;
using Kronikol;
using Kronikol.LightBDD;
using Kronikol.LightBDD.xUnit3;
using Kronikol.Tracking;
using LightBDD.Core.Configuration;
using LightBDD.Core.Execution;
using LightBDD.Core.Extensibility.Execution;
using LightBDD.Framework;
using LightBDD.Framework.Configuration;
using LightBDD.Framework.Scenarios;
using LightBDD.XUnit3;
using Xunit.v3;
using Xunit;

[assembly: TrackSteps]
[assembly: TestPipelineStartup(typeof(lbprobe.Scope))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace lbprobe;

public class Scope : LightBddScope
{
    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "LB" });
        configuration.ProgressNotifierConfiguration().Clear();
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
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new LightBddTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Test" }) { InnerHandler = new Fake() }) { BaseAddress = new Uri("http://svc.local") };
    public static Task Call(string where) => Client.GetAsync("/" + where);
    public static void CallSync(string where) => Client.GetAsync("/" + where).GetAwaiter().GetResult();
}

/// <summary>A scenario decorator that runs a step method and a call around the scenario.</summary>
public class AroundScenarioAttribute : Attribute, IScenarioDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IScenario scenario, Func<Task> scenarioInvocation)
    {
        await Api.Call("in-scenario-decorator-before");
        await DecoratorStep();
        await scenarioInvocation();
        await Api.Call("in-scenario-decorator-after");
    }
    [Step] static async Task DecoratorStep() { await Api.Call("in-DecoratorStep"); }
}

/// <summary>A step decorator that runs a call around each step.</summary>
public class AroundStepsAttribute : Attribute, IStepDecoratorAttribute
{
    public int Order => 0;
    public async Task ExecuteAsync(IStep step, Func<Task> stepInvocation)
    {
        await Api.Call("in-step-decorator-before");
        await stepInvocation();
        await Api.Call("in-step-decorator-after");
    }
}

public class Features : FeatureFixture
{
    public bool Yes => true;

    [Scenario] public async Task L01_Baseline() => await Runner.RunScenarioAsync(given => A(), when => B(), then => C());
    async Task A() { await Api.Call("in-A"); await SubA(); await Api.Call("in-A-after"); }
    async Task B() { await Api.Call("in-B"); }
    async Task C() { await Api.Call("in-C"); }
    [Step] async Task SubA() { await Task.Delay(1); await Api.Call("in-SubA"); }

    [Scenario] public async Task L02_Composite() => await Runner.RunScenarioAsync(given => A(), when => Comp(), then => C());
    async Task<CompositeStep> Comp()
    {
        await Api.Call("in-Comp-builder");
        return CompositeStep.DefineNew().AddAsyncSteps(_ => Kid1(), _ => Kid2()).Build();
    }
    async Task Kid1() { await Api.Call("in-Kid1"); await SubKid(); }
    async Task Kid2() { await Api.Call("in-Kid2"); }
    [Step] async Task SubKid() { await Api.Call("in-SubKid"); }

    [Scenario] public async Task L03_Composite_nested_twice() => await Runner.RunScenarioAsync(given => A(), when => CompOuter());
    Task<CompositeStep> CompOuter() => Task.FromResult(CompositeStep.DefineNew().AddAsyncSteps(_ => Comp(), _ => Kid2()).Build());

    [Scenario] public async Task L04_Composite_child_fails() => await Runner.RunScenarioAsync(given => A(), when => CompF(), then => C());
    Task<CompositeStep> CompF() => Task.FromResult(CompositeStep.DefineNew().AddAsyncSteps(_ => Kid2(), _ => KidBad(), _ => Kid1()).Build());
    async Task KidBad() { await Api.Call("in-KidBad"); throw new InvalidOperationException("composite child failed"); }

    [Scenario] public async Task L05_Sub_step_fails() => await Runner.RunScenarioAsync(given => A(), when => Boom(), then => C());
    async Task Boom() { await Api.Call("in-Boom"); await BadSub(); }
    [Step] async Task BadSub() { await Api.Call("in-BadSub"); throw new InvalidOperationException("sub step failed"); }

    [Scenario] public async Task L06_Step_method_before_scenario_and_after() { await Outside("before"); await Runner.RunScenarioAsync(given => A(), then => C()); await Outside("after"); }
    [Step] async Task Outside(string w) { await Api.Call("in-Outside-" + w); }

    [Scenario, AroundScenario] public async Task L07_Scenario_decorator_runs_a_step_method() => await Runner.RunScenarioAsync(given => A(), then => C());

    [Scenario, AroundSteps] public async Task L08_Step_decorator_makes_calls() => await Runner.RunScenarioAsync(given => A(), then => C());

    [Scenario] public async Task L09_Parallel_sub_steps() => await Runner.RunScenarioAsync(given => Par(), then => C());
    async Task Par() { await Task.WhenAll(P1(), P2()); await Api.Call("in-Par-after"); }
    [Step] async Task P1() { await Task.Delay(5); await Api.Call("in-P1"); await Task.Delay(30); }
    [Step] async Task P2() { await Task.Delay(15); await Api.Call("in-P2"); await Task.Delay(30); }

    [Scenario] public async Task L10_Sync_bypass_in_a_step() => await Runner.RunScenarioAsync(given => WithBypass(), then => C());
    async Task WithBypass() { await Api.Call("in-WithBypass-before"); MaybeS(); await Api.Call("in-WithBypass-after"); await SubA(); await Api.Call("in-WithBypass-end"); }
    [Step(SkipIf = nameof(Yes), SkipReason = "no")] void MaybeS() { Api.CallSync("never"); }

    [Scenario] public async Task L11_Async_bypass_in_a_step() => await Runner.RunScenarioAsync(given => WithBypassA(), then => C());
    async Task WithBypassA() { await Api.Call("in-WithBypassA-before"); await MaybeA(); await Api.Call("in-WithBypassA-after"); await SubA(); await Api.Call("in-WithBypassA-end"); }
    [Step(SkipIf = nameof(Yes), SkipReason = "no")] async Task MaybeA() { await Api.Call("never"); }

    [Scenario] public async Task L12_Detached_into_the_next_step() => await Runner.RunScenarioAsync(given => StartT(), when => Release(), then => C());
    Task? _t; readonly TaskCompletionSource _go = new();
    async Task StartT() { _t = Task.Run(async () => { await _go.Task; await Api.Call("detached-from-StartT"); }); await Task.Yield(); }
    async Task Release() { _go.SetResult(); await _t!; await Api.Call("in-Release"); }

    [Scenario] public async Task L13_Sub_step_outlives_its_step() => await Runner.RunScenarioAsync(given => LeavesRunning(), when => Waits(), then => C());
    async Task LeavesRunning() { _t = LeftRunning(); await Api.Call("in-LeavesRunning"); }
    [Step] async Task LeftRunning() { await _go.Task; await Api.Call("in-LeftRunning"); }
    async Task Waits() { _go.SetResult(); await _t!; await Api.Call("in-Waits"); }

    [Scenario] public async Task L14_Ignored_step() => await Runner.RunScenarioAsync(given => A(), when => Ignored(), then => C());
    Task Ignored() { LightBDD.Framework.StepExecution.Current.IgnoreScenario("not today"); return Task.CompletedTask; }

    [Scenario] public async Task L15_Attachments_and_assertions() => await Runner.RunScenarioAsync(given => Att(), then => C());
    async Task Att() { Track.That(() => Assert.True(true), "assert-in-framework-step"); await SubAtt(); Track.Attachment(Temp("lb-step"), "lb-step"); Track.That(() => Assert.True(true), "assert-in-framework-step-after"); }
    [Step] async Task SubAtt() { await Task.Yield(); Track.That(() => Assert.True(true), "assert-in-sub"); Track.Attachment(Temp("lb-sub"), "lb-sub"); }
    static string Temp(string n) { var p = Path.Combine(Path.GetTempPath(), n + ".txt"); File.WriteAllText(p, "x"); return p; }

    [Scenario] public async Task L16_Call_in_scenario_body_before_runner() { await Api.Call("in-body-before"); await Runner.RunScenarioAsync(given => A(), then => C()); await Api.Call("in-body-after"); }
}
