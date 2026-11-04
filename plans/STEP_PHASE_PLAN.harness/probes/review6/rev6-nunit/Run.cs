using Kronikol;
using Kronikol.NUnit4;
using Kronikol.Tracking;
using NUnit.Framework;
using Rev5;

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
        Skeleton.Write(Environment.GetEnvironmentVariable("PROBE_NAME") ?? "rev6-nunit");
    }
}

namespace Rev6.NUnit
{
    public abstract class NBase : DiagrammedComponentTest
    {
        protected static HttpClient NewClient(bool trackDuringSetup = true, bool trackDuringAction = true) =>
            new(new TestTrackingMessageHandler(new NUnitTestTrackingMessageHandlerOptions
            {
                CallerName = "Probe",
                FixedNameForReceivingService = "Shop",
                TrackDuringSetup = trackDuringSetup,
                TrackDuringAction = trackDuringAction
            }) { InnerHandler = new Answer() });

        protected readonly HttpClient Http = NewClient();
        protected async Task Call(string l) => (await Http.GetAsync($"http://probe.local/{l}")).EnsureSuccessStatusCode();

        [GivenStep] public async Task GivenAsync(string l) => await Call(l);
        [WhenStep] public async Task WhenAsync(string l) => await Call(l);
        [ThenStep] public async Task ThenAsync(string l) => await Call(l);
        [WhenStep] public async Task WhenMarks(string l) { await Call(l + "-arrange"); TrackingDiagramOverride.StartAction(); await Call(l); }
        [ThenStep] public async Task ThenMarks(string l) { TrackingDiagramOverride.StartAction(); await Call(l); }
    }

    [TestFixture]
    public class N01_Retry_plain_Given_When_Then : NBase
    {
        private static int _attempts;

        [Test, Retry(2)]
        public async Task Run()
        {
            var a = ++_attempts;
            await GivenAsync($"n01-seed{a}");
            await WhenAsync($"n01-act{a}");
            await ThenAsync($"n01-then{a}");
            if (a == 1) Assert.Fail("first attempt");
        }
    }

    [TestFixture]
    public class N02_Retry_a_When_that_marks_after_a_call : NBase
    {
        private static int _attempts;

        [Test, Retry(2)]
        public async Task Run()
        {
            var a = ++_attempts;
            await GivenAsync($"n02-seed{a}");
            await WhenMarks($"n02-act{a}");
            await ThenAsync($"n02-then{a}");
            if (a == 1) Assert.Fail("first attempt");
        }
    }

    [TestFixture]
    public class N03_Retry_a_Then_that_marks : NBase
    {
        private static int _attempts;

        [Test, Retry(2)]
        public async Task Run()
        {
            var a = ++_attempts;
            await GivenAsync($"n03-seed{a}");
            await WhenAsync($"n03-act{a}");
            await ThenMarks($"n03-then{a}");
            if (a == 1) Assert.Fail("first attempt");
        }
    }

    [TestFixture]
    public class N04_Retry_the_first_attempt_fails_before_any_When : NBase
    {
        private static int _attempts;

        [Test, Retry(2)]
        public async Task Run()
        {
            var a = ++_attempts;
            await GivenAsync($"n04-seed{a}");
            if (a == 1) Assert.Fail("first attempt");
            await WhenAsync($"n04-act{a}");
            await ThenAsync($"n04-then{a}");
        }
    }
}
