using Kronikol;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.Rev3.Readers;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Dump2.Write("rev3-readers");
        XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime,
            new ReportConfigurationOptions { SpecificationsTitle = "Readers probe", SeparateSetup = bool.Parse(Environment.GetEnvironmentVariable("PROBE_SEPARATE_SETUP") ?? "false"), DiagnosticMode = true });
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public class Suite : DiagrammedComponentTest
{
    private readonly Calls C = new(
        new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
        new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher }));

    public Suite() { Dump2.Names[TestContext.Current.Test!.UniqueID] = TestContext.Current.Test!.TestDisplayName; }

    [GivenStep] public async Task A_customer_exists(string label) => await C.Make(label);
    [GivenStep] public void No_calls_in_this_given() { }
    [WhenStep] public async Task The_customer_orders(string label) => await C.Make(label);
    [ThenStep] public async Task The_order_is_shipped(string label) { await C.Make(label); }
    [ThenStep] public async Task The_order_fails(string label) { await C.Make(label); Assert.Equal(1, 2); }
    [Step] public async Task A_keyword_free_step(string label) => await C.Make(label);
    [WhenStep] public void When_nothing() { }

    [Fact] public async Task R01_passes()
    {
        await A_customer_exists("r01-given");
        await The_customer_orders("r01-when");
        await The_order_is_shipped("r01-then");
    }

    [Fact] public async Task R02_fails_in_the_Then()
    {
        await A_customer_exists("r02-given");
        await The_customer_orders("r02-when");
        await The_order_fails("r02-then");
    }

    [Fact] public async Task R03_fails_with_a_Given_with_no_calls()
    {
        No_calls_in_this_given();
        await The_customer_orders("r03-when");
        await The_order_fails("r03-then");
    }

    [Fact] public async Task R04_steps_but_no_calls_at_all()
    {
        No_calls_in_this_given();
        When_nothing();
        await Task.CompletedTask;
    }

    [GivenStep] public void Arrange_with_a_manual_skipped_sub(string label)
    {
        var id = TestContext.Current.Test!.UniqueID;
        StepCollector.StartStep(id, null, label + "-manual-sub", null, null);
        StepCollector.BypassStep(id, "not on this tenant");
    }

    [Fact] public async Task R07_fails_after_a_manual_bypass_under_a_Given()
    {
        Arrange_with_a_manual_skipped_sub("r07");
        await The_customer_orders("r07-when");
        await The_order_fails("r07-then");
    }

    [Fact] public async Task R05_keyword_free()
    {
        await A_keyword_free_step("r05-plain");
    }

    [Fact] public async Task R06_fails_with_no_steps()
    {
        await C.Make("r06-raw");
        Assert.Equal(1, 2);
    }
}
