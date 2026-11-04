using Kronikol;
using Kronikol.TUnit;
using Kronikol.Tracking;
using PhaseProbe;
using TUnit.Core;

[assembly: TrackSteps]

namespace PhaseProbe
{
    public class TestRun : DiagrammedTestRun
    {
        [Before(Assembly)]
        public static void GlobalSetup(AssemblyHookContext context) => Setup();

        [After(Assembly)]
        public static void GlobalTeardown(AssemblyHookContext context)
        {
            EndRunTime = DateTime.UtcNow;
            Dump.Write(Environment.GetEnvironmentVariable("PHASE_PROBE_NAME") ?? "tunit");
            TUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
        }
    }

    public static class ProbeEnv
    {
        public static Func<(string Name, string Id)> Fetcher => TUnitTestTrackingMessageHandlerOptions.TestInfoFetcher;

        public static Calls NewCalls(Func<string?>? stepTypes = null, bool trackDuringSetup = true) => new(
            new HttpClient(new TestTrackingMessageHandler(new TUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", CurrentStepTypeFetcher = stepTypes, TrackDuringSetup = trackDuringSetup }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = Fetcher, TrackDuringSetup = trackDuringSetup }));
    }
}
