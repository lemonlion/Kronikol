using System.Net;
using Kronikol;
using Kronikol.MSTest;
using Kronikol.Tracking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace ProbeMSTest;

[TestClass]
public class TestRun : DiagrammedTestRun
{
    [AssemblyInitialize]
    public static void AssemblySetup(TestContext context) => Setup();

    [AssemblyCleanup]
    public static void AssemblyTeardown()
    {
        EndRunTime = DateTime.UtcNow;
        MSTestReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions { ReportsFolderPath = Environment.GetEnvironmentVariable("PROBE_REPORTS") ?? "Reports" });
    }
}

[TestClass]
public class RowsTests : DiagrammedComponentTest
{
    private sealed class Ok : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private Task Call(string path)
    {
        var handler = new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Probe", FixedNameForReceivingService = "Target", CurrentTestInfoFetcher = CurrentTestInfo.Fetcher,
        }) { InnerHandler = new Ok() };
        return new HttpClient(handler).GetAsync("http://target" + path);
    }

    private void Print(string kind) =>
        Console.WriteLine($"PROBE {kind}: TestName={TestContext.TestName} | TestDisplayName={TestContext.TestDisplayName} | " +
            $"TestData={(TestContext.TestData is null ? "null" : "[" + string.Join(",", TestContext.TestData) + "]")} | Fetcher id={CurrentTestInfo.Fetcher().Id}");

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task Row(int n)
    {
        Print($"row {n}");
        await Call($"/rows/{n}");
        Assert.AreNotEqual(2, n, "row 2 fails on purpose");
    }

    [TestMethod(DisplayName = "A plain test with a display name")]
    public async Task Plain()
    {
        Print("plain");
        await Call("/plain");
    }

    // Two methods whose rows share a DisplayName of their own: under 4.13.3's rule both rows were {class}.small.
    [TestMethod]
    [DataRow(1, DisplayName = "small")]
    public async Task Place(int n)
    {
        Print("place small");
        await Call("/place/small");
    }

    [TestMethod]
    [DataRow(1, DisplayName = "small")]
    public async Task Cancel(int n)
    {
        Print("cancel small");
        await Call("/cancel/small");
        Assert.Fail("cancel fails on purpose");
    }

    public static IEnumerable<object[]> Letters => [["a"], ["b"]];

    [TestMethod]
    [DynamicData(nameof(Letters))]
    public async Task Dynamic(string letter)
    {
        Print($"dynamic {letter}");
        await Call($"/letters/{letter}");
    }
}
