using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit2;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: Xunit.TestFramework("Kronikol.xUnit2.ReportingTestFramework", "Kronikol.xUnit2")]

namespace PhaseProbe
{
    public class TestRun : DiagrammedTestRun, IDisposable
    {
        public TestRun() => ReportLifecycle.Options = new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true };

        public void Dispose()
        {
            EndRunTime = DateTime.UtcNow;
            Dump.Write("xunit2");
        }
    }

    [CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
    public class Collection : ICollectionFixture<TestRun> { }

    public static class ProbeEnv
    {
        public static Func<(string Name, string Id)> Fetcher => XUnit2TestTrackingMessageHandlerOptions.TestInfoFetcher;

        public static Calls NewCalls(Func<string?>? stepTypes = null, bool trackDuringSetup = true) => new(
            new HttpClient(new TestTrackingMessageHandler(new XUnit2TestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", CurrentStepTypeFetcher = stepTypes, TrackDuringSetup = trackDuringSetup }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = Fetcher, TrackDuringSetup = trackDuringSetup }));
    }
}
