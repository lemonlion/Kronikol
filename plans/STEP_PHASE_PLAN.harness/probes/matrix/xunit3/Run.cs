using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe
{
    public class TestRun : DiagrammedTestRun, IDisposable
    {
        public void Dispose()
        {
            EndRunTime = DateTime.UtcNow;
            Dump.Write("xunit3");
            XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
        }
    }

    [CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
    public class Collection : ICollectionFixture<TestRun> { }

    public static class ProbeEnv
    {
        public static Func<(string Name, string Id)> Fetcher => XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher;

        public static Calls NewCalls(Func<string?>? stepTypes = null, bool trackDuringSetup = true) => new(
            new HttpClient(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", CurrentStepTypeFetcher = stepTypes, TrackDuringSetup = trackDuringSetup }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = Fetcher, TrackDuringSetup = trackDuringSetup }));
    }
}
