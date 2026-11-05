using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: AssemblyFixture(typeof(PhaseProbe.TestRun))]

namespace PhaseProbe
{
    public class TestRun : DiagrammedTestRun, IDisposable
    {
        public TestRun()
        {
            if (Environment.GetEnvironmentVariable("PROBE_REDACT") == "on")
            {
                RequestResponseLogger.Redaction = CaptureRedaction.Secrets();
                RequestResponseLogger.MaxContentLength = 10;
            }
            if (Environment.GetEnvironmentVariable("PROBE_BACKGROUND") == "on")
                RequestResponseLogger.CaptureBackground = true;
            if (Environment.GetEnvironmentVariable("PROBE_WTA") == "off")
                StepCollector.Options = StepCollector.Options with { WhenTriggersAction = false };
            if (Environment.GetEnvironmentVariable("PROBE_BARS") == "off")
                StepCollector.Options = StepCollector.Options with { ShowStepDelimiters = false };
        }

        public void Dispose()
        {
            EndRunTime = DateTime.UtcNow;
            if (Environment.GetEnvironmentVariable("PROBE_STRESS") != "on") Dump.Write("x3");
            if (Environment.GetEnvironmentVariable("PROBE_NDJSON") is { Length: > 0 } ndjson)
            {
                using var writer = new Kronikol.Ingestion.NdjsonInteractionWriter(ndjson);
                foreach (var log in RequestResponseLogger.RequestAndResponseLogs)
                    writer.Log(log);
            }
            XUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions
            {
                SpecificationsTitle = "Phase probe",
                SeparateSetup = Environment.GetEnvironmentVariable("PROBE_SEPARATE") != "off",
                DiagnosticMode = true,
                GenerateMergeableData = Environment.GetEnvironmentVariable("PROBE_MERGEABLE") == "on",
            });
        }
    }

    public static class Env
    {
        public static Func<(string Name, string Id)> Fetcher => XUnitTestTrackingMessageHandlerOptions.TestInfoFetcher;

        public static TestTrackingMessageHandler Handler(Func<string?>? stepTypes = null, bool trackDuringSetup = true, bool trackDuringAction = true, string caller = "Probe") =>
            new(new XUnitTestTrackingMessageHandlerOptions { CallerName = caller, FixedNameForReceivingService = "Shop", CurrentStepTypeFetcher = stepTypes, TrackDuringSetup = trackDuringSetup, TrackDuringAction = trackDuringAction })
            { InnerHandler = new AnswerEverything() };

        public static MessageTracker Tracker(bool trackDuringSetup = true, bool trackDuringAction = true, MessageTrackerVerbosity? setup = null, MessageTrackerVerbosity? action = null) =>
            new(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = Fetcher, TrackDuringSetup = trackDuringSetup, TrackDuringAction = trackDuringAction, SetupVerbosity = setup, ActionVerbosity = action });

        public static Calls NewCalls(Func<string?>? stepTypes = null, bool trackDuringSetup = true, bool trackDuringAction = true) =>
            new(new HttpClient(Handler(stepTypes, trackDuringSetup, trackDuringAction)), Tracker(trackDuringSetup, trackDuringAction));
    }

    public abstract class ProbeBase : DiagrammedComponentTest
    {
        protected static string Id => TestContext.Current.Test!.UniqueID;

        protected ProbeBase() => Dump.Names[Id] = TestContext.Current.TestMethod!.MethodName;

        protected static void Note(string note) => Dump.Note(Id, note);
    }
}
