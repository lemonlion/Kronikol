using Kronikol;
using Kronikol.NUnit4;
using Kronikol.Tracking;
using NUnit.Framework;
using PhaseProbe;

[assembly: TrackSteps]

// Outside any namespace, so OneTimeSetUp and OneTimeTearDown cover the whole assembly.
[SetUpFixture]
public class TestRun : DiagrammedTestRun
{
    [OneTimeSetUp]
    public static void GlobalSetup() => Setup();

    [OneTimeTearDown]
    public static void GlobalTeardown()
    {
        EndRunTime = DateTime.UtcNow;
        foreach (var context in TestContexts)
            Dump.Names[context.Test.ID] = context.Test.Name;
        Dump.Write("nunit4");
        NUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true });
    }
}

namespace PhaseProbe
{
    public static class ProbeEnv
    {
        public static Func<(string Name, string Id)> Fetcher => NUnitTestTrackingMessageHandlerOptions.TestInfoFetcher;

        public static Calls NewCalls(Func<string?>? stepTypes = null, bool trackDuringSetup = true) => new(
            new HttpClient(new TestTrackingMessageHandler(new NUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", CurrentStepTypeFetcher = stepTypes, TrackDuringSetup = trackDuringSetup }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = Fetcher, TrackDuringSetup = trackDuringSetup }));
    }
}
