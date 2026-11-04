using System.Net;
using Kronikol;
using Kronikol.TUnit;
using Kronikol.Tracking;
using TUnit.Core;
using TUnit.Assertions;

[assembly: TrackSteps]

namespace tuprobe;

public class TestRun : DiagrammedTestRun
{
    [Before(Assembly)] public static void Start(AssemblyHookContext context) => Setup();
    [After(Assembly)] public static void End(AssemblyHookContext context)
    {
        EndRunTime = DateTime.UtcNow;
        TUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "TUnit" });
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
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Test", CurrentTestInfoFetcher = Kronikol.TUnit.CurrentTestInfo.Fetcher }) { InnerHandler = new Fake() }) { BaseAddress = new Uri("http://svc.local") };
    public static Task Call(string where) => Client.GetAsync("/" + where);
}

[NotInParallel]
public class Retries : DiagrammedComponentTest
{
    static int _a1, _a2;

    [Test, Retry(3)] public async Task T1_Retry_passes_second_attempt()
    {
        var n = Interlocked.Increment(ref _a1);
        await Given1(n);
        await When1(n);
        if (n == 1) throw new InvalidOperationException("first attempt fails");
        await Then1(n);
    }
    [GivenStep] async Task Given1(int n) { await Api.Call($"in-Given1-attempt{n}"); await Sub1(n); }
    [Step] async Task Sub1(int n) { await Task.Yield(); await Api.Call($"in-Sub1-attempt{n}"); }
    [WhenStep] async Task When1(int n) { await Api.Call($"in-When1-attempt{n}"); }
    [ThenStep] async Task Then1(int n) { await Api.Call($"in-Then1-attempt{n}"); }

    [Test, Retry(2)] public async Task T2_Retry_fails_in_a_step_each_time()
    {
        var n = Interlocked.Increment(ref _a2);
        await Given2(n);
    }
    [GivenStep] async Task Given2(int n) { await Api.Call($"in-Given2-attempt{n}"); throw new InvalidOperationException($"attempt {n} fails inside the step"); }

    [Test] public async Task T3_Parallel_kids() { await Parent3(); await Next3(); }
    [Step] async Task Parent3() { await Task.WhenAll(K3("a", 5), K3("b", 15)); await Api.Call("in-Parent3-after"); }
    [Step] async Task K3(string n, int d) { await Task.Delay(d); await Api.Call("in-K3-" + n); await Task.Delay(30); }
    [Step] async Task Next3() { await Api.Call("in-Next3"); }
}

[NotInParallel]
public class Hooks : DiagrammedComponentTest
{
    [Before(Test)] public void SyncBefore() { InSyncBefore(); }
    [Step] void InSyncBefore() { Api.Call("in-SyncBefore").GetAwaiter().GetResult(); }
    [Before(Test)] public async Task AsyncBefore() { await InAsyncBefore(); }
    [Step] async Task InAsyncBefore() { await Task.Yield(); await Api.Call("in-AsyncBefore"); }
    [After(Test)] public void SyncAfter() { InSyncAfter(); }
    [Step] void InSyncAfter() { Api.Call("in-SyncAfter").GetAwaiter().GetResult(); }
    [Test] public async Task T4_Steps_in_hooks() { await Body4(); }
    [Step] async Task Body4() { await Api.Call("in-Body4"); await Sub4(); }
    [Step] async Task Sub4() { await Api.Call("in-Sub4"); }
}

// parallel by default
public class Par : DiagrammedComponentTest
{
    [Test, Arguments("a"), Arguments("b"), Arguments("c"), Arguments("d"), Arguments("e"), Arguments("f")]
    public async Task T5_Parallel(string n) { await Top5(n); await Second5(n); }
    [Step] async Task Top5(string n) { await Api.Call(n + "-top"); await Task.WhenAll(Kid5(n, "1"), Kid5(n, "2")); }
    [Step] async Task Kid5(string n, string k) { await Task.Delay(Random.Shared.Next(1, 15)); await Api.Call(n + "-kid" + k); }
    [Step] async Task Second5(string n) { await Api.Call(n + "-second"); }
}
