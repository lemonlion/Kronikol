using Kronikol;
using Kronikol.Tracking;
using PhaseProbe;
using Xunit;

namespace PhaseProbe.X3;

/// <summary>Many tests at once on handlers they share, each fanning out at its first act. Only on with PROBE_STRESS=on, as it floods the dump.</summary>
public class Stress : ProbeBase
{
    private static readonly AsyncLocal<string?> Stage = new();

    private static bool Off => Environment.GetEnvironmentVariable("PROBE_STRESS") != "on";

    private static HttpClient Client(string caller) => new(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
    {
        CallerName = caller,
        FixedNameForReceivingService = "Shop",
        CurrentStepTypeFetcher = () => Stage.Value,
    }) { InnerHandler = new AnswerEverything() });

    [Fact]
    public async Task S01_many_tests_fan_out_at_the_first_act_on_shared_handlers()
    {
        if (Off) return;
        var shared = Enumerable.Range(0, 4).Select(i => Client($"C{i}")).ToArray();
        const int tests = 2000;
        var ids = Enumerable.Range(0, tests).Select(i => $"s01-{i:0000}-{Guid.NewGuid():N}").ToArray();
        using var gate = new SemaphoreSlim(64);
        await Task.WhenAll(ids.Select(id => Task.Run(async () =>
        {
            await gate.WaitAsync();
            try
            {
                using var scope = TestIdentityScope.Begin("s01", id);
                var clients = Environment.GetEnvironmentVariable("PROBE_STRESS_PER_TEST") == "on" ? Enumerable.Range(0, 4).Select(i => Client($"C{i}")).ToArray() : shared;
                Stage.Value = "Given";
                foreach (var c in clients)
                    await c.GetAsync("http://probe.local/given");
                Stage.Value = "When";
                await Task.WhenAll(clients.Select(c => Task.Run(() => c.GetAsync("http://probe.local/when"))));
            }
            finally { gate.Release(); }
        })));

        var byTest = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId != null && l.TestId.StartsWith("s01-")).GroupBy(l => l.TestId).ToArray();
        int oneMarker = 0, notOne = 0, whenBeforeMarker = 0, givenAfterMarker = 0, whenPhaseAction = 0, whenPhaseOther = 0;
        foreach (var test in byTest)
        {
            var logs = test.ToList();
            var markers = logs.Count(l => l.IsActionStart);
            if (markers == 1) oneMarker++; else notOne++;
            var at = logs.FindIndex(l => l.IsActionStart);
            var when = logs.Select((l, i) => (l, i)).Where(x => x.l.Type == RequestResponseType.Request && !x.l.IsDiagramMarker && x.l.Uri.AbsolutePath == "/when").ToList();
            var given = logs.Select((l, i) => (l, i)).Where(x => x.l.Type == RequestResponseType.Request && !x.l.IsDiagramMarker && x.l.Uri.AbsolutePath == "/given").ToList();
            if (at >= 0 && when.Any(x => x.i < at)) whenBeforeMarker++;
            if (at >= 0 && given.Any(x => x.i > at)) givenAfterMarker++;
            whenPhaseAction += when.Count(x => x.l.Phase == TestPhase.Action);
            whenPhaseOther += when.Count(x => x.l.Phase != TestPhase.Action);
        }
        var summary = $"tests={byTest.Length} oneMarker={oneMarker} notOne={notOne} testsWithAWhenCallBeforeTheMarker={whenBeforeMarker} givenAfterMarker={givenAfterMarker} whenCallsReadingAction={whenPhaseAction} whenCallsReadingOther={whenPhaseOther}";
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("PHASE_PROBE_OUT")!, "stress.txt"), summary);
        var all = RequestResponseLogger.RequestAndResponseLogs;
    }
}
