using System.Text;
using Kronikol;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PhaseProbe.Rev3.Collapse;

public class TestRun : DiagrammedTestRun, IDisposable
{
    public void Dispose()
    {
        EndRunTime = DateTime.UtcNow;
        Dump2.Write("rev3-collapse");
        // The same tracked logs with SeparateSetup off: a marker is a marker to the collapser whatever that option says.
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        var uml = new StringBuilder();
        var tracked = RequestResponseLogger.RequestAndResponseLogs.Where(l => !l.TrackingIgnore).ToArray();
        foreach (var test in PlantUmlCreator.GetPlantUmlImageTagsPerTestId(tracked, separateSetup: false, clientSideSplitting: true, maxEncodedDiagramLength: int.MaxValue,
                     collapseConsecutiveIdenticalCalls: true).OrderBy(t => t.TestName, StringComparer.Ordinal))
        {
            uml.AppendLine($"## {(Dump2.Names.TryGetValue(test.TestId, out var n) ? n : test.TestId)}");
            foreach (var diagram in test.PlantUmls)
                foreach (var raw in diagram.PlainText.Replace("\r", "").Split('\n'))
                {
                    var l = raw.Trim();
                    if (l.StartsWith("partition") || l == "end" || l.StartsWith("hnote") || l.Contains("->") || l.Contains("-->") || l.Contains('×') || l.Contains(" x"))
                        uml.AppendLine("  " + (l.Length > 110 ? l[..110] : l));
                }
            uml.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, "rev3-collapse.separate-off.txt"), uml.ToString());
    }
}

[CollectionDefinition(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class Collection : ICollectionFixture<TestRun> { }

public class Suite : DiagrammedComponentTest
{
    private readonly HttpClient _http = new(new TestTrackingMessageHandler(new XUnitTestTrackingMessageHandlerOptions { CallerName = "Probe", FixedNameForReceivingService = "Shop" }) { InnerHandler = new AnswerEverything() });

    public Suite() { Dump2.Names[TestContext.Current.Test!.UniqueID] = TestContext.Current.Test!.TestDisplayName; }

    private Task Same() => _http.GetAsync("http://probe.local/same");

    [GivenStep] public async Task Given_two_same() { await Same(); await Same(); }
    [WhenStep] public async Task When_two_same() { await Same(); await Same(); }
    [GivenStep] public async Task Given_one_same() { await Same(); }
    [WhenStep] public async Task When_one_same() { await Same(); }

    private static async Task DelimitersOff(Func<Task> body)
    {
        var saved = StepCollector.Options;
        StepCollector.Options = saved with { ShowStepDelimiters = false };
        try { await body(); } finally { StepCollector.Options = saved; }
    }

    [Fact] public Task K01_two_same_in_a_Given_two_same_in_a_When_delimiters_off() =>
        DelimitersOff(async () => { await Given_two_same(); await When_two_same(); });

    [Fact] public Task K02_one_same_in_a_Given_one_same_in_a_When_delimiters_off() =>
        DelimitersOff(async () => { await Given_one_same(); await When_one_same(); });

    // Finding 3's trial marks steps started through the ambient overload as the stub's: is a hand-made pair through it still swallowed?
    [GivenStep] public void Given_with_ambient_manual_bypass()
    {
        StepCollector.StartStep(null, "k04-manual-sub", null, null);
        StepCollector.BypassStep("manual");
    }
    [WhenStep] public async Task When_k04() { await _http.GetAsync("http://probe.local/k04-when"); }
    [ThenStep] public async Task Then_k04() { await _http.GetAsync("http://probe.local/k04-then"); }

    [Fact] public async Task K04_ambient_manual_bypass_under_a_sync_Given()
    {
        Given_with_ambient_manual_bypass();
        await When_k04();
        await Then_k04();
    }

    [Fact] public async Task K03_control_with_delimiters_on()
    {
        await Given_two_same();
        await When_two_same();
    }
}
