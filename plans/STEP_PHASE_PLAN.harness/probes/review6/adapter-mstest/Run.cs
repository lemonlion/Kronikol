using Kronikol;
using Kronikol.MSTest;
using Kronikol.Tracking;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PhaseProbe;

[assembly: TrackSteps]
[assembly: DoNotParallelize]

namespace PhaseProbe
{
    [TestClass]
    public class TestRun : DiagrammedTestRun
    {
        [AssemblyInitialize]
        public static void GlobalSetup(TestContext context) => Setup();

        [AssemblyCleanup]
        public static void GlobalTeardown()
        {
            EndRunTime = DateTime.UtcNow;
            Dump.Write("mstest");
            MSTestReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
        }
    }

    public static class ProbeEnv
    {
        public static Func<(string Name, string Id)> Fetcher => MSTestTestTrackingMessageHandlerOptions.TestInfoFetcher;

        public static Calls NewCalls(Func<string?>? stepTypes = null, bool trackDuringSetup = true) => new(
            new HttpClient(new TestTrackingMessageHandler(new MSTestTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", CurrentStepTypeFetcher = stepTypes, TrackDuringSetup = trackDuringSetup }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = Fetcher, TrackDuringSetup = trackDuringSetup }));
    }
}
