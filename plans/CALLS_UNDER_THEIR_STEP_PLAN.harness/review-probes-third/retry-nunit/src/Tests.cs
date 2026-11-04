using System.Net;
using Kronikol;
using Kronikol.NUnit4;
using Kronikol.Tracking;
using NUnit.Framework;

[assembly: TrackSteps]

[SetUpFixture]
public class RunFixture : DiagrammedTestRun
{
    [OneTimeSetUp] public void Start() => Setup();
    [OneTimeTearDown] public void End()
    {
        EndRunTime = DateTime.UtcNow;
        NUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "NUnit" });
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
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Test", CurrentTestInfoFetcher = Kronikol.NUnit4.CurrentTestInfo.Fetcher }) { InnerHandler = new Fake() }) { BaseAddress = new Uri("http://svc.local") };
    public static Task Call(string where) => Client.GetAsync("/" + where);
}

public class Retries : DiagrammedComponentTest
{
    static int _attempts;
    static int _attempts2;
    static int _attempts3;

    [Test, Retry(3)] public async Task N1_Retry_passes_on_second_attempt()
    {
        var n = Interlocked.Increment(ref _attempts);
        await Given1(n);
        await When1(n);
        if (n == 1) Assert.Fail("first attempt fails");
        await Then1(n);
    }
    [GivenStep] async Task Given1(int n) { await Api.Call($"in-Given1-attempt{n}"); await Sub1(n); }
    [Step] async Task Sub1(int n) { await Task.Yield(); await Api.Call($"in-Sub1-attempt{n}"); }
    [WhenStep] async Task When1(int n) { await Api.Call($"in-When1-attempt{n}"); }
    [ThenStep] async Task Then1(int n) { await Api.Call($"in-Then1-attempt{n}"); }

    [Test, Retry(2)] public async Task N2_Retry_fails_inside_a_step_each_time()
    {
        var n = Interlocked.Increment(ref _attempts2);
        await Given2(n);
    }
    [GivenStep] async Task Given2(int n) { await Api.Call($"in-Given2-attempt{n}"); Assert.Fail($"attempt {n} fails inside the step"); }

    [Test, Repeat(2)] public async Task N3_Repeat()
    {
        var n = Interlocked.Increment(ref _attempts3);
        await Given3(n);
    }
    [GivenStep] async Task Given3(int n) { await Api.Call($"in-Given3-attempt{n}"); }

    [SetUp] public void InSetUp() { Before(); }
    [Step] void Before() { Api.Call("in-SetUp-step").GetAwaiter().GetResult(); }
    [TearDown] public void InTearDown() { After(); }
    [Step] void After() { Api.Call("in-TearDown-step").GetAwaiter().GetResult(); }
    [Test] public async Task N4_SetUp_and_TearDown_steps() { await Body4(); }
    [Step] async Task Body4() { await Api.Call("in-Body4"); }
}
