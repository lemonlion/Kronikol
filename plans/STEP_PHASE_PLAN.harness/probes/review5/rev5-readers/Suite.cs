using Kronikol;
using Kronikol.Extensions.Otlp;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Rev5;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rev5.Readers;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Skeleton.Write("rev5-readers");
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? ".";
        var logs = RequestResponseLogger.RequestAndResponseLogs;
        var batch = OtlpSpanMapper.MapAll(logs, new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318") }, DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(dir, "rev5-readers.otlp.txt"),
            $"logs={logs.Length} markers={logs.Count(l => l.IsActionStart)} requests={logs.Count(l => l.Type == RequestResponseType.Request && !l.IsDiagramMarker)} spans={batch.Spans.Count} skipped={batch.SkippedRecords} orphans={batch.OrphanSpans}\n");
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions { SpecificationsTitle = "Readers probe", SeparateSetup = true, CollapseConsecutiveIdenticalCalls = true });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public class Suite : DiagrammedComponentTest
{
    private readonly HttpClient Http = new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new Answer() });
    private async Task Call(string label) => (await Http.GetAsync($"http://probe.local/{label}")).EnsureSuccessStatusCode();

    [GivenStep] public async Task A_customer_exists(string label) => await Call(label);
    [GivenStep] public void No_calls_in_this_given() { }
    [WhenStep] public async Task The_customer_orders(string label) => await Call(label);
    [WhenStep] public async Task The_customer_orders_after_marking(string label) { await Call(label + "-arrange"); TrackingDiagramOverride.StartAction(); await Call(label); }
    [ThenStep] public async Task The_order_is_shipped(string label) => await Call(label);
    [ThenStep] public async Task The_order_fails(string label) { await Call(label); Assert.Equal(1, 2); }
    [WhenStep] public async Task Identical_calls(int n) { for (var i = 0; i < n; i++) await Call("same"); }
    [GivenStep] public async Task Identical_seeds(int n) { for (var i = 0; i < n; i++) await Call("same"); }
    [WhenStep] public void When_nothing() { }

    [Fact] public async Task T01_passes()
    {
        await A_customer_exists("t01-given");
        await The_customer_orders("t01-when");
        await The_order_is_shipped("t01-then");
    }

    [Fact] public async Task T02_fails_in_the_Then()
    {
        await A_customer_exists("t02-given");
        await The_customer_orders("t02-when");
        await The_order_fails("t02-then");
    }

    [Fact] public async Task T03_two_markers_fails_in_the_Then()
    {
        await A_customer_exists("t03-given");
        await The_customer_orders_after_marking("t03-when");
        await The_order_fails("t03-then");
    }

    [Fact] public async Task T04_steps_no_calls()
    {
        No_calls_in_this_given();
        When_nothing();
        await Task.CompletedTask;
    }

    [Fact] public async Task T05_identical_calls_across_the_boundary()
    {
        await Identical_seeds(3);
        await Identical_calls(3);
    }

    [Fact] public async Task T06_fails_in_the_Given_no_marker()
    {
        await A_customer_exists("t06-given");
        Assert.Equal(1, 2);
    }
}
