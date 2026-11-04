using System.Net;
using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Probe;

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Coll : ICollectionFixture<TestRun> { }

public class TestRun : DiagrammedTestRun, IDisposable
{
    public TestRun()
    {
        if (Environment.GetEnvironmentVariable("PROBE_NO_BARS") == "1")
            StepCollector.Options = new StepTrackingOptions { ShowStepDelimiters = false };
    }

    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        var options = new ReportConfigurationOptions { SpecificationsTitle = "Flows" };
        if (Environment.GetEnvironmentVariable("PROBE_MERGEABLE") == "1")
            options.GenerateMergeableData = true;
        if (Environment.GetEnvironmentVariable("PROBE_FORMAT") is { Length: > 0 } format)
        {
            options.TestRunReportDataFormat = Enum.Parse<Kronikol.Reports.DataFormat>(format);
            options.CompressTestRunReport = false;
            options.GenerateTestRunReportSchema = true;
        }
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, options);
    }
}

public sealed class Fake : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(5, ct);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
    }
}

public static class Api
{
    private static readonly HttpClient Client = new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { FixedNameForReceivingService = "Svc", CallerName = "Test" }) { InnerHandler = new Fake() }) { BaseAddress = new Uri("http://svc.local") };

    /// <summary>The URL is the ground truth: it says where the call was really made.</summary>
    public static Task Call(string where) => Client.GetAsync("/" + where);

    /// <summary>What a service's own thread does: names the test, shares no flow with it.</summary>
    public static void FromElsewhere(string name, string id, string where)
    {
        RequestResponseLogger.LogPair(name, id, HttpMethod.Get, new Uri("http://svc.local/" + where), "Svc", "Svc2");
    }

    public static (string Name, string Id) Me => (TestContext.Current.Test!.TestDisplayName, TestContext.Current.Test!.UniqueID);

    /// <summary>A copy of the test's identity for a thread with no flow.</summary>
    public static (string Name, string Id) Me0 { get => TestIdentityScope.Current ?? Me; set { } }
}
