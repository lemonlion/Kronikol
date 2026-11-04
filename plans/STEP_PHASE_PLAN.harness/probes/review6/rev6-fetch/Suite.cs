using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev5;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TestCaseOrderer(typeof(Rev6.Fetch.ByName))]

namespace Rev6.Fetch;

public class ByName : ITestCaseOrderer
{
    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases) where TTestCase : notnull, ITestCase =>
        testCases.OrderBy(t => t.TestCaseDisplayName, StringComparer.Ordinal).ToArray();
}

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "rev6-fetch");
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public static class StepName
{
    public static readonly AsyncLocal<string?> Current = new();
}

/// <summary>T4b: a handler shared by tests, with a custom fetcher, asks again per test. A fetcher that throws what LightBDD's does not.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Shared : DiagrammedComponentTest
{
    private static HttpClient NewClient(Func<string?> fetcher) =>
        new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions
        {
            CallerName = "Probe",
            FixedNameForReceivingService = "Shop",
            CurrentStepTypeFetcher = fetcher
        }) { InnerHandler = new Answer() });

    private static readonly HttpClient Fragile = NewClient(() => StepName.Current.Value!.ToUpperInvariant());
    private static readonly HttpClient Plain = NewClient(() => StepName.Current.Value?.ToUpperInvariant());

    private static async Task Call(HttpClient c, string l) => (await c.GetAsync($"http://probe.local/{l}")).EnsureSuccessStatusCode();

    [Fact]
    public async Task F1_a_first_test_that_reaches_the_boundary()
    {
        StepName.Current.Value = "Given";
        await Call(Fragile, "f1-given");
        await Call(Plain, "f1-given-plain");
        StepName.Current.Value = "When";
        await Call(Fragile, "f1-when");
        await Call(Plain, "f1-when-plain");
        StepName.Current.Value = null;
        await Call(Fragile, "f1-after-step");
    }

    [Fact]
    public async Task F2_a_later_test_with_a_call_outside_any_step()
    {
        StepName.Current.Value = null;
        await Call(Fragile, "f2-outside-any-step");
        StepName.Current.Value = "Given";
        await Call(Fragile, "f2-given");
        StepName.Current.Value = "When";
        await Call(Fragile, "f2-when");
    }

    [Fact]
    public async Task F3_a_later_test_on_the_plain_fetcher()
    {
        StepName.Current.Value = "Given";
        await Call(Plain, "f3-given");
        StepName.Current.Value = "When";
        await Call(Plain, "f3-when");
        StepName.Current.Value = null;
    }
}
