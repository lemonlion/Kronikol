using System.Collections.Concurrent;
using Kronikol;
using Kronikol.NUnit4;
using Kronikol.Tracking;
using NUnit.Framework;
using PhaseProbe;

[assembly: TrackSteps]
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]

[SetUpFixture]
public class TestRun : DiagrammedTestRun
{
    [OneTimeSetUp] public static void GlobalSetup() => Setup();

    [OneTimeTearDown]
    public static void GlobalTeardown()
    {
        EndRunTime = DateTime.UtcNow;
        foreach (var context in TestContexts)
            Dump.Names[context.Test.ID] = context.Test.Name;
        Dump.Write("nu");
        NUnitReportGenerator.CreateStandardReportsWithDiagrams(TestContexts, StartRunTime, EndRunTime, new ReportConfigurationOptions { SpecificationsTitle = "Phase probe", SeparateSetup = true, DiagnosticMode = true });
    }
}

namespace PhaseProbe.Nu
{
    public static class Env
    {
        public static Func<(string Name, string Id)> Fetcher => NUnitTestTrackingMessageHandlerOptions.TestInfoFetcher;
        public static Calls NewCalls(bool trackDuringSetup = true) => new(
            new HttpClient(new TestTrackingMessageHandler(new NUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop", TrackDuringSetup = trackDuringSetup }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = Fetcher, TrackDuringSetup = trackDuringSetup }));
        public static readonly Calls Shared = NewCalls();
    }

    public abstract class Steps : DiagrammedComponentTest
    {
        protected Calls C = Env.NewCalls();
        [GivenStep] public async Task GivenCall(string l) => await C.Make(l);
        [GivenStep] public void GivenCallSync(string l) => C.Make(l).GetAwaiter().GetResult();
        [WhenStep] public async Task WhenCall(string l) => await C.Make(l);
        [ThenStep] public async Task ThenCall(string l) => await C.Make(l);
        [ThenStep] public void ThenCallSync(string l) => C.Make(l).GetAwaiter().GetResult();
    }

    public class SetUps : Steps
    {
        [SetUp] public async Task Before() => await GivenCall("setup-" + TestContext.CurrentContext.Test.MethodName);
        [TearDown] public void After() => ThenCallSync("teardown-" + TestContext.CurrentContext.Test.MethodName);

        [Test] public async Task N01_given_in_setup_when_in_test() => await WhenCall("n01-w");
        [Test] public async Task N02_no_when() => await C.Make("n02-loose");
    }

    public class OneTime : Steps
    {
        [OneTimeSetUp] public async Task Once() { var c = Env.NewCalls(); C = c; await GivenCall("onetime-g"); await WhenCall("onetime-w"); }
        [Test] public async Task N10_after_onetime() { await GivenCall("n10-g"); await WhenCall("n10-w"); }
        [Test] public async Task N11_when_only_after_onetime() { await WhenCall("n11-w"); }
    }

    public class Repeats : Steps
    {
        private static readonly ConcurrentDictionary<string, int> Attempts = new();
        [Test, Repeat(2)]
        public async Task N20_repeat()
        {
            var n = Attempts.AddOrUpdate(nameof(N20_repeat), 1, (_, v) => v + 1);
            await GivenCall($"n20-a{n}-g");
            await WhenCall($"n20-a{n}-w");
        }

        [Test, Retry(3)]
        public async Task N21_retry()
        {
            var n = Attempts.AddOrUpdate(nameof(N21_retry), 1, (_, v) => v + 1);
            await GivenCall($"n21-a{n}-g");
            await WhenCall($"n21-a{n}-w");
            if (n == 1) Assert.Fail("first attempt fails");
            await ThenCall($"n21-a{n}-t");
        }

        [TestCase("a")]
        [TestCase("b")]
        public async Task N22_cases(string row) { await GivenCall($"n22-{row}-g"); await WhenCall($"n22-{row}-w"); }
    }

    public class ParallelOne : Steps
    {
        [SetUp] public void UseShared() => C = Env.Shared;
        [Test] public async Task P1() { await GivenCall("n-p1-g"); await WhenCall("n-p1-w"); }
        [Test] public async Task P2() { await GivenCall("n-p2-g"); await WhenCall("n-p2-w"); }
    }

    public class ParallelTwo : Steps
    {
        [SetUp] public void UseShared() => C = Env.Shared;
        [Test] public async Task P3() { await GivenCall("n-p3-g"); await WhenCall("n-p3-w"); }
        [Test] public async Task P4() { await GivenCall("n-p4-g"); await WhenCall("n-p4-w"); }
    }
}
