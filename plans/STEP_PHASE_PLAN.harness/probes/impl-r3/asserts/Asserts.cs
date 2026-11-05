using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using PhaseProbe;
using Shouldly;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Xunit;

[assembly: AssemblyFixture(typeof(Probe.Asserts.Setup))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TrackSteps]
[assembly: TrackAssertions]

namespace Probe.Asserts;

public class Setup : IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BDDfyDiagramsConfigurator.Configure();
        BDDfyScenarioCollector.StartRunTime = DateTime.UtcNow;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        BDDfyScenarioCollector.EndRunTime = DateTime.UtcNow;
        Dump.Write("asserts");
        BDDfyReportGenerator.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Probe asserts", SeparateSetup = true });
        return ValueTask.CompletedTask;
    }
}

public class Stories
{
    private readonly Calls C;
    private int _total = 3;

    public Stories()
    {
        Known.Register();
        C = new Calls(
            new HttpClient(new TestTrackingMessageHandler(new BDDfyTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() }),
            new MessageTracker(new MessageTrackerOptions { CallerName = "Probe", ServiceName = "Bus", CurrentTestInfoFetcher = BDDfyTestTrackingMessageHandlerOptions.TestInfoFetcher }));
    }

    [ThenStep] public async Task WovenThen(string l) { await C.Make(l); _total.ShouldBe(3); }
    public void GivenS(string l) => C.Make(l).GetAwaiter().GetResult();
    public async Task WhenA(string l) { await Task.Yield(); await C.Make(l); }
    public async Task ThenAsserts(string l) { _total.ShouldBe(3); await C.Make(l); _total.ShouldBeGreaterThan(1); }
    public async Task ThenFailsLate(string l) { await C.Make(l); _total.ShouldBe(4); await C.Make(l + "-never"); }
    public async Task ThenWovenAssert(string l) { await Task.Yield(); await WovenThen(l + "-sub"); _total.ShouldBe(3); }
    public async Task AndAsserts(string l) { await Task.Yield(); await C.Make(l); _total.ShouldBe(3); }
    public async Task WhenThatCatches(string l) { await Task.Yield(); try { _total.ShouldBe(9); } catch (ShouldAssertException) { } await C.Make(l); }

    [Fact] public void S1_assertions_either_side_of_the_first_await() =>
        this.Given(x => x.GivenS("s1-g")).When(x => x.WhenA("s1-w")).Then(x => x.ThenAsserts("s1-t")).BDDfy();
    [Fact] public void S2_an_assertion_fails_after_the_first_await() =>
        this.Given(x => x.GivenS("s2-g")).When(x => x.WhenA("s2-w")).Then(x => x.ThenFailsLate("s2-t")).And(x => x.AndAsserts("s2-and")).BDDfy();
    [Fact] public void S3_an_assertion_in_a_woven_step_after_the_first_await() =>
        this.Given(x => x.GivenS("s3-g")).When(x => x.WhenA("s3-w")).Then(x => x.ThenWovenAssert("s3-t")).BDDfy();
    [Fact] public void S4_a_failed_assertion_the_step_catches() =>
        this.Given(x => x.GivenS("s4-g")).When(x => x.WhenThatCatches("s4-w")).Then(x => x.ThenAsserts("s4-t")).BDDfy();
}
