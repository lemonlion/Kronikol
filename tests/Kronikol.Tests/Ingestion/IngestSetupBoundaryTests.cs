using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// <c>plans/INGEST_FIDELITY_PLAN.md</c> S4 (T2 to T4): with <see cref="ReportConfigurationOptions.SeparateSetup"/> on, an
/// ingested test gets the Setup/Action boundary its run would have drawn in-process, from its steps first and else from
/// its calls' phases, unless the capture carries its own. <c>--phase-from-steps</c> promised the partition from 3.0.45
/// and only ever tagged the calls: the partition is drawn at a <c>Phase</c> marker, which ingest made only from a raw
/// <c>kind: marker</c> record.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestSetupBoundaryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-setup-boundary-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
    private const string Partition = "partition #F6F6F6 Setup";

    public IngestSetupBoundaryTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void The_boundary_comes_from_the_steps_and_the_partition_holds_the_given_call_only()
    {
        // The P1 probe's input: a Given step with a call in it, then a When step with a call in it. A second test whose
        // first step is a When shows the boundary is per test and needs a Setup step before the Action one.
        const string charged = "charges-a-card";
        const string noSetup = "declines-a-card";
        var tests = new List<TestRunRecord>(GivenWhen(charged, "charges a card"));
        tests.AddRange(WhenThen(noSetup, "declines a card"));
        var calls = Calls(charged, ("GET", "/cards/1", "cards", 1500), ("POST", "/charges", "psp", 3500))
            .Concat(Calls(noSetup, ("GET", "/cards/2", "cards", 1500), ("POST", "/charges", "psp", 3500)))
            .ToArray();

        var separated = Ingest("steps-separated", calls, tests, separateSetup: true);

        var setup = PartitionOf(separated[charged]);
        Assert.Contains("GET: /cards/1", setup);
        Assert.Contains("Given a saved card", setup);
        Assert.DoesNotContain("/charges", setup);
        Assert.DoesNotContain("When the card is charged", setup);
        Assert.Equal(1, Count(separated[charged], "partition"));
        Assert.Equal(0, Count(separated[noSetup], "partition"));

        // Without SeparateSetup nothing is added, whether or not the phases were tagged: the diagrams are what they were.
        var plain = Ingest("steps-plain", calls, tests, separateSetup: false);
        var phased = Ingest("steps-phased", calls, tests, separateSetup: false, phaseFromSteps: true);
        Assert.Equal(0, Count(plain[charged], "partition"));
        Assert.Equal(plain[charged], phased[charged]);
        Assert.Equal(plain[noSetup], phased[noSetup]);

        // --phase-from-steps beside it changes nothing in the drawing: the steps place the boundary either way.
        var both = Ingest("steps-both", calls, tests, separateSetup: true, phaseFromSteps: true);
        Assert.Equal(separated[charged], both[charged]);
    }

    [Fact]
    public void Without_steps_the_boundary_comes_from_the_calls_phases()
    {
        // No steps: a capturer that phased its own calls, Setup then Action. The first two calls are identical, so without
        // SeparateSetup they collapse into one loop, which a boundary between them would stop: the proof that nothing is
        // added when the option is off.
        const string id = "phased-by-the-capturer";
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = id, TestName = "charges a saved card", Feature = "charge.test.ts", Timestamp = T0 },
            new() { Event = "end", TestId = id, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) },
        ];
        InteractionRecord[] phasedCalls =
        [
            .. Call(id, "GET", "/cards/1", "cards", 1000, "Setup"),
            .. Call(id, "GET", "/cards/1", "cards", 2000, "Action"),
            .. Call(id, "POST", "/charges", "psp", 3000, "Action"),
        ];
        var unphasedCalls = phasedCalls.Select(r => r with { Phase = null }).ToArray();

        var separated = Ingest("phases-separated", phasedCalls, tests, separateSetup: true)[id];
        var setup = PartitionOf(separated);
        Assert.Equal(1, Count(setup, "GET: /cards/1"));
        Assert.DoesNotContain("/charges", setup);
        Assert.Equal(2, Count(separated, "GET: /cards/1"));
        Assert.DoesNotContain("loop ×", separated);

        var plain = Ingest("phases-plain", phasedCalls, tests, separateSetup: false)[id];
        Assert.Contains("loop ×2", plain);
        Assert.Equal(0, Count(plain, "partition"));
        Assert.Equal(Ingest("unphased-plain", unphasedCalls, tests, separateSetup: false)[id], plain);

        // Unphased calls say nothing, so nothing is drawn even with the option on.
        Assert.Equal(0, Count(Ingest("unphased-separated", unphasedCalls, tests, separateSetup: true)[id], "partition"));
    }

    [Fact]
    public void A_first_step_that_is_a_when_draws_no_boundary()
    {
        // The in-process rule (TestTrackingMessageHandlerTests: no marker when the first step is a When): a boundary only
        // after something happened in setup. --phase-from-steps tags both calls Action, so the phases say nothing either.
        const string id = "starts-with-a-when";
        var calls = Calls(id, ("GET", "/cards/1", "cards", 1500), ("POST", "/charges", "psp", 3500));

        foreach (var phaseFromSteps in new[] { false, true })
        {
            var diagram = Ingest($"when-first-{phaseFromSteps}", calls, WhenThen(id, "declines a card"), separateSetup: true, phaseFromSteps)[id];
            Assert.Equal(0, Count(diagram, "partition"));
        }
    }

    [Fact]
    public void A_captured_phase_marker_is_the_only_boundary_drawn()
    {
        // The capture's own boundary sits after the When step's first call, where the steps would have put it before
        // that call. The drawing uses the first marker, so a second one added from the steps would move the partition's
        // end up and leave GET /risk/1 out of it.
        const string id = "capture-wins";
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = id, TestName = "charges after a risk check", Feature = "charge.test.ts", Timestamp = T0 },
            new() { Event = "step", TestId = id, Text = "a saved card", Keyword = "Given", Status = "passed", DurationMs = 1000, Timestamp = T0.AddMilliseconds(1000) },
            new() { Event = "step", TestId = id, Text = "the card is charged", Keyword = "When", Status = "passed", DurationMs = 3000, Timestamp = T0.AddMilliseconds(2000) },
            new() { Event = "end", TestId = id, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) },
        ];
        var boundary = new InteractionRecord
        {
            Kind = "marker", MarkerKind = "Phase", Type = "Request", Uri = "http://override.com/", ServiceName = "", CallerName = "",
            TestId = id, Timestamp = T0.AddMilliseconds(3000),
        };
        InteractionRecord[] records =
        [
            .. Call(id, "GET", "/cards/1", "cards", 1500),
            .. Call(id, "GET", "/risk/1", "risk", 2500),
            boundary,
            .. Call(id, "POST", "/charges", "psp", 4000),
        ];

        var diagram = Ingest("capture-wins", records, tests, separateSetup: true)[id];

        Assert.Equal(1, Count(diagram, "partition"));
        var setup = PartitionOf(diagram);
        Assert.Contains("GET: /cards/1", setup);
        Assert.Contains("GET: /risk/1", setup);
        Assert.DoesNotContain("/charges", setup);
    }

    private static TestRunRecord[] GivenWhen(string id, string name) =>
    [
        new() { Event = "start", TestId = id, TestName = name, Feature = "charge.test.ts", Timestamp = T0 },
        new() { Event = "step", TestId = id, Text = "a saved card", Keyword = "Given", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(1000) },
        new() { Event = "step", TestId = id, Text = "the card is charged", Keyword = "When", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(3000) },
        new() { Event = "end", TestId = id, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) },
    ];

    private static TestRunRecord[] WhenThen(string id, string name) =>
    [
        new() { Event = "start", TestId = id, TestName = name, Feature = "charge.test.ts", Timestamp = T0 },
        new() { Event = "step", TestId = id, Text = "the card is charged", Keyword = "When", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(1000) },
        new() { Event = "step", TestId = id, Text = "the charge is declined", Keyword = "Then", Status = "passed", DurationMs = 2000, Timestamp = T0.AddMilliseconds(3000) },
        new() { Event = "end", TestId = id, Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(6000) },
    ];

    private static InteractionRecord[] Call(string testId, string method, string path, string service, double startMs, string? phase = null)
    {
        var (request, response) = InteractionRecord.Pair(testId, null, method, "http://localhost:5000" + path, service, "superpay-graphql",
            requestContent: "{}", responseContent: """{"ok":true}""", statusCode: "200",
            requestTimestamp: T0.AddMilliseconds(startMs), responseTimestamp: T0.AddMilliseconds(startMs + 100),
            requestResponseId: $"{testId}-{path}-{startMs}", phase: phase);
        return [request, response];
    }

    private static InteractionRecord[] Calls(string testId, params (string Method, string Path, string Service, double StartMs)[] calls) =>
        calls.SelectMany(c => Call(testId, c.Method, c.Path, c.Service, c.StartMs)).ToArray();

    /// <summary>Ingests and returns each scenario's diagram source by test id.</summary>
    private Dictionary<string, string> Ingest(string name, IEnumerable<InteractionRecord> records, IEnumerable<TestRunRecord> tests,
        bool separateSetup, bool phaseFromSteps = false)
    {
        var capture = Path.Combine(_dir, name + ".ndjson");
        File.WriteAllLines(capture, records.Select(r => r.ToJson()));
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, name);
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        options.SeparateSetup = separateSetup;

        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestRecords = tests.ToArray(),
            Options = options,
            PhaseFromSteps = phaseFromSteps,
        });

        Assert.True(result.Generated);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.ReportsFolderPath, "TestRunReport.json")));
        return json.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => ReportPayloadText.Of(s.GetProperty("diagrams")[0])!);
    }

    /// <summary>The lines inside the diagram's Setup partition, which closes at the first bare <c>end</c>.</summary>
    internal static string PartitionOf(string diagram)
    {
        var lines = diagram.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var open = Array.FindIndex(lines, l => l.StartsWith(Partition, StringComparison.Ordinal));
        Assert.True(open >= 0, "No Setup partition in:\n" + diagram);
        var close = Array.FindIndex(lines, open + 1, l => l.Trim() == "end");
        Assert.True(close > open, "The Setup partition never closes in:\n" + diagram);
        return string.Join('\n', lines[(open + 1)..close]);
    }

    internal static int Count(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
