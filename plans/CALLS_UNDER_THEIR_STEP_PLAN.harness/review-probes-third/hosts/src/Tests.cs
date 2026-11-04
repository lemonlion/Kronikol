using System.Net;
using System.Threading.Channels;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = false, MaxParallelThreads = 8)]
[assembly: AssemblyFixture(typeof(hostsprobe.Run))]

namespace hostsprobe;

public class Run : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "Hosts" });
    }
}

/// <summary>Like DiagrammedComponentTest, but free to run in parallel with others.</summary>
public abstract class Base : IDisposable
{
    protected Base() { Track.TestIdResolver ??= () => TestContext.Current.Test?.UniqueID; }
    public void Dispose()
    {
        if (TestContext.Current.Test?.UniqueID is { } id) DiagrammedTestRun.TestEnds[id] = DateTimeOffset.UtcNow;
        DiagrammedTestRun.TestContexts.Enqueue(TestContext.Current);
    }
}

public sealed class Fake : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(3, ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
    }
}

public static class Api
{
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Test" }) { InnerHandler = new Fake() }) { BaseAddress = new Uri("http://svc.local") };
    public static Task Call(string where) => Client.GetAsync("/" + where);
}

public sealed class Sut : IAsyncDisposable
{
    public WebApplication App = null!;
    public HttpClient Client = null!;
    public readonly Channel<(string Name, string Id, string Label)> Queue = Channel.CreateUnbounded<(string, string, string)>();

    public static async Task<Sut> Start(bool preserve, bool loop = false, bool consumer = false)
    {
        var sut = new Sut();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer(o => o.PreserveExecutionContext = preserve);
        builder.Services.AddTestTrackingContextPropagation();
        builder.Services.AddHttpClient("down").AddHttpMessageHandler(() => new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Down", CallerName = "Sut" })).ConfigurePrimaryHttpMessageHandler(() => new Fake());
        if (loop) builder.Services.AddHostedService(sp => new Loop(sp.GetRequiredService<IHttpClientFactory>()));
        if (consumer) builder.Services.AddHostedService(sp => new Consumer(sp.GetRequiredService<IHttpClientFactory>(), sut.Queue));
        var app = builder.Build();
        app.MapGet("/stepped/{label}", async (string label, IHttpClientFactory f) =>
        {
            await new FakeSteps(f).HandleInAStep(label);
            return "ok";
        });
        app.MapGet("/do/{label}", async (string label, IHttpClientFactory f) =>
        {
            await f.CreateClient("down").GetAsync("http://down.local/downstream-" + label);
            await Task.Delay(1);
            await f.CreateClient("down").GetAsync("http://down.local/downstream2-" + label);
            return "ok";
        });
        await app.StartAsync();
        sut.App = app;
        sut.Client = new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Sut", CallerName = "Test" }) { InnerHandler = app.GetTestServer().CreateHandler() }) { BaseAddress = new Uri("http://sut.local") };
        return sut;
    }

    public async ValueTask DisposeAsync() { await App.StopAsync(); await App.DisposeAsync(); }
}

public sealed class FakeSteps(IHttpClientFactory f)
{
    /// <summary>A step method in a test double the service calls.</summary>
    [Step] public async Task HandleInAStep(string label) { await f.CreateClient("down").GetAsync("http://down.local/in-HandleInAStep-" + label); }
    [Step] public void HandleInASyncStep(string label) { f.CreateClient("down").GetAsync("http://down.local/in-HandleInASyncStep-" + label).GetAwaiter().GetResult(); }
}

public sealed class Loop(IHttpClientFactory f) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var i = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await f.CreateClient("down").GetAsync("http://down.local/loop-tick-" + i++, stoppingToken);
                await Task.Delay(40, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
    }
}

public sealed class Consumer(IHttpClientFactory f, Channel<(string Name, string Id, string Label)> q) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var m in q.Reader.ReadAllAsync(stoppingToken))
            {
                using (TestIdentityScope.Begin(m.Name, m.Id))
                    await f.CreateClient("down").GetAsync("http://down.local/consumed-" + m.Label, stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
    }
}

[Collection("hosts-serial")]
public class Hosts : Base
{
    // ---- PreserveExecutionContext on: the service runs in the caller's flow
    [Fact] public async Task H01_Preserve_on_parallel_kids() { await using var s = await Sut.Start(true); await Parent1(s); }
    [Step] async Task Parent1(Sut s) { await Task.WhenAll(K1a(s), K1b(s)); }
    [Step] async Task K1a(Sut s) { await Task.Delay(5); await s.Client.GetAsync("/do/K1a"); await Task.Delay(30); }
    [Step] async Task K1b(Sut s) { await Task.Delay(15); await s.Client.GetAsync("/do/K1b"); await Task.Delay(30); }

    // ---- off: the service has its own flow; only the identity header ties it to the test
    [Fact] public async Task H02_Preserve_off_parallel_kids() { await using var s = await Sut.Start(false); await Parent1(s); }

    [Fact] public async Task H03_Preserve_off_sequential() { await using var s = await Sut.Start(false); await Parent3(s); await Next3(s); }
    [Step] async Task Parent3(Sut s) { await K3a(s); await K3b(s); }
    [Step] async Task K3a(Sut s) { await s.Client.GetAsync("/do/K3a"); }
    [Step] async Task K3b(Sut s) { await s.Client.GetAsync("/do/K3b"); }
    [Step] async Task Next3(Sut s) { await s.Client.GetAsync("/do/Next3"); }

    [Fact] public async Task H04_Preserve_on_sequential() { await using var s = await Sut.Start(true); await Parent3(s); await Next3(s); }

    // ---- a BackgroundService started in a step
    [Fact] public async Task H05_Host_started_in_a_step() { Sut? s = null; try { s = await StartIn5(); await Wait5("B"); await Wait5("C"); } finally { if (s is not null) await s.DisposeAsync(); } }
    [Step] async Task<Sut> StartIn5() { var s = await Sut.Start(false, loop: true); await Task.Delay(60); return s; }
    [Step] async Task Wait5(string n) { await Task.Delay(100); await Api.Call("in-Wait5-" + n); }

    // ---- started in a sub-step of a long step
    [Fact] public async Task H06_Host_started_in_a_sub_step() { await Long6(); await After6(); await _s6!.DisposeAsync(); }
    Sut? _s6;
    [Step] async Task Long6() { _s6 = await StartIn6(); await Other6(); await Task.Delay(100); }
    [Step] async Task<Sut> StartIn6() { var s = await Sut.Start(false, loop: true); await Task.Delay(50); return s; }
    [Step] async Task Other6() { await Task.Delay(100); await Api.Call("in-Other6"); }
    [Step] async Task After6() { await Task.Delay(100); await Api.Call("in-After6"); }

    // ---- started in the test body, before any step
    [Fact] public async Task H07_Host_started_before_any_step() { await using var s = await Sut.Start(false, loop: true); await Task.Delay(60); await A7(); await Parallel7(); }
    [Step] async Task A7() { await Task.Delay(100); await Api.Call("in-A7"); }
    [Step] async Task Parallel7() { await Task.WhenAll(B7(), C7()); }
    [Step] async Task B7() { await Task.Delay(120); await Api.Call("in-B7"); }
    [Step] async Task C7() { await Task.Delay(120); await Api.Call("in-C7"); }

    // ---- a consumer loop handling correlated messages later
    [Fact] public async Task H08_Consumer_handles_message_from_earlier_step()
    {
        await using var s = await Sut.Start(false, consumer: true);
        var me = (TestContext.Current.Test!.TestDisplayName, TestContext.Current.Test!.UniqueID);
        await Publish8(s, me);
        await Next8(s, me);
        await Task.Delay(50);
    }
    [Step] async Task Publish8(Sut s, (string, string) me) { await Task.Delay(10); s.Queue.Writer.TryWrite((me.Item1, me.Item2, "published-in-Publish8")); await Task.Delay(50); }
    [Step] async Task Next8(Sut s, (string, string) me) { await Task.Delay(50); await Api.Call("in-Next8"); }

    [Fact] public async Task H09_Consumer_while_two_kids_run()
    {
        await using var s = await Sut.Start(false, consumer: true);
        var me = (TestContext.Current.Test!.TestDisplayName, TestContext.Current.Test!.UniqueID);
        await Parent9(s, me);
    }
    [Step] async Task Parent9(Sut s, (string, string) me) { await Task.WhenAll(K9a(s, me), K9b()); }
    [Step] async Task K9a(Sut s, (string, string) me) { await Task.Delay(10); s.Queue.Writer.TryWrite((me.Item1, me.Item2, "published-in-K9a")); await Task.Delay(100); }
    [Step] async Task K9b() { await Task.Delay(120); }

    // ---- a step method in the service's own flow: what is it under
    [Fact] public async Task H11_Step_method_in_a_service_preserve_off() { await using var s = await Sut.Start(false); await Parent11(s); await Next11(); }
    [Fact] public async Task H12_Step_method_in_a_service_preserve_on() { await using var s = await Sut.Start(true); await Parent11(s); await Next11(); }
    [Step] async Task Parent11(Sut s) { await Kid11(s); await Api.Call("in-Parent11-after-kid"); }
    [Step] async Task Kid11(Sut s) { await s.Client.GetAsync("/stepped/from-Kid11"); await Api.Call("in-Kid11-after-service"); }
    [Step] async Task Next11() { await Api.Call("in-Next11"); }

    // ---- consumer started in a step, keeps its flow's step, message correlates to the same test
    [Fact] public async Task H10_Consumer_started_in_a_step_flow_is_stale()
    {
        var me = (TestContext.Current.Test!.TestDisplayName, TestContext.Current.Test!.UniqueID);
        var s = await StartConsumer10();
        await Publish10(s, me);
        await Task.Delay(50);
        await s.DisposeAsync();
    }
    [Step] async Task<Sut> StartConsumer10() => await Sut.Start(false, consumer: true);
    [Step] async Task Publish10(Sut s, (string, string) me) { s.Queue.Writer.TryWrite((me.Item1, me.Item2, "published-in-Publish10")); await Task.Delay(50); }
}

// ---- parallel tests with parallel kids; each call's label says which step it belongs to
public abstract class ParBase : Base
{
    protected async Task Scenario(string n)
    {
        await Top(n);
        await Second(n);
    }
    [Step] async Task Top(string n) { await Api.Call(n + "-top"); await Task.WhenAll(KidA(n), KidB(n)); await Api.Call(n + "-top2"); }
    [Step] async Task KidA(string n) { await Task.Delay(Random.Shared.Next(1, 20)); await Api.Call(n + "-kidA"); await SubA(n); }
    [Step] async Task KidB(string n) { await Task.Delay(Random.Shared.Next(1, 20)); await Api.Call(n + "-kidB"); }
    [Step] async Task SubA(string n) { await Task.Delay(Random.Shared.Next(1, 20)); await Api.Call(n + "-subA"); }
    [Step] async Task Second(string n) { await Api.Call(n + "-second"); }
}
[Collection("p1")] public class P1 : ParBase { [Fact] public Task T1() => Scenario("p1-t1"); [Fact] public Task T2() => Scenario("p1-t2"); [Fact] public Task T3() => Scenario("p1-t3"); }
[Collection("p2")] public class P2 : ParBase { [Fact] public Task T1() => Scenario("p2-t1"); [Fact] public Task T2() => Scenario("p2-t2"); [Fact] public Task T3() => Scenario("p2-t3"); }
[Collection("p3")] public class P3 : ParBase { [Fact] public Task T1() => Scenario("p3-t1"); [Fact] public Task T2() => Scenario("p3-t2"); [Fact] public Task T3() => Scenario("p3-t3"); }
[Collection("p4")] public class P4 : ParBase { [Fact] public Task T1() => Scenario("p4-t1"); [Fact] public Task T2() => Scenario("p4-t2"); [Fact] public Task T3() => Scenario("p4-t3"); }
[Collection("p5")] public class P5 : ParBase { [Fact] public Task T1() => Scenario("p5-t1"); [Fact] public Task T2() => Scenario("p5-t2"); [Fact] public Task T3() => Scenario("p5-t3"); }
[Collection("p6")] public class P6 : ParBase { [Fact] public Task T1() => Scenario("p6-t1"); [Fact] public Task T2() => Scenario("p6-t2"); [Fact] public Task T3() => Scenario("p6-t3"); }
