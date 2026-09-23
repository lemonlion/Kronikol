using System.Net;
using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// The acceptance harness of <c>plans/INGEST_FEED_PLAN.md</c> (T7). One fixture of logs, every marker kind
/// among them, rendered twice: once in-process from the store, the way an adapter's run end does it, and
/// once written through <see cref="NdjsonInteractionWriter"/> and replayed by <see cref="IngestPipeline"/>.
/// For everything the writer sees, what comes out must equal what went in: <c>httpInteractions</c> member
/// by member, <c>annotations</c>, and the diagram source byte for byte. The differences that remain are
/// enumerated here and must be the only ones.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestRoundTripTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-roundtrip-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    public IngestRoundTripTests()
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_store_projected_through_the_writer_ingests_to_the_report_the_in_process_run_wrote(bool separateSetup)
    {
        // Unique per run: the store is process-wide, shared by the collection, and never cleared here.
        var testId = "roundtrip-" + Guid.NewGuid().ToString("N");
        // The step and the assertion fill the step list on both sides; the drawing is the store's own bar and
        // note, which the ingest keeps as the drawing (a capture carrying step markers draws no tests-file bar).
        TestRunRecord[] testRecords =
        [
            new() { Event = "start", TestId = testId, TestName = "Probe", Feature = "probe.feature", Timestamp = T0 },
            new() { Event = "step", TestId = testId, Text = "a basket", Keyword = "Given", Status = "passed", DurationMs = 6000, Timestamp = T0.AddMilliseconds(1000) },
            new() { Event = "assertion", TestId = testId, Text = "the basket is empty", Status = "passed", Timestamp = T0.AddMilliseconds(4500) },
            new() { Event = "end", TestId = testId, Status = "passed", DurationMs = 8000, Timestamp = T0.AddMilliseconds(8000) },
        ];
        var logs = Fixture(testId);

        // (a) In-process, and first: the logs through the store, the features from the same tests records,
        // the generator over the store. Its data file is read before the ingest half clears the store.
        foreach (var log in logs)
            RequestResponseLogger.Log(log);
        var stored = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        Assert.Equal(logs.Count, stored.Length);
        var synthesised = FeatureSynthesizer.Build(testRecords, stored, "Ingested", ExecutionResult.Passed, null);
        var inProcessDir = Path.Combine(_dir, $"in-process-{separateSetup}");
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(synthesised.Features, synthesised.Start, synthesised.End,
            Options(inProcessDir, separateSetup), RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);
        DefaultDiagramsFetcher.Reset();
        var inProcess = Scenario(inProcessDir, testId);

        // (b) The same logs through the writer and the pipeline, the same tests records, the same options.
        var capture = Path.Combine(_dir, $"projected-{separateSetup}.ndjson");
        using (var writer = new NdjsonInteractionWriter(capture))
            foreach (var log in logs)
                writer.Log(log);
        var ingestDir = Path.Combine(_dir, $"ingest-{separateSetup}");
        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestRecords = testRecords,
            Options = Options(ingestDir, separateSetup),
            CallTreeOrdering = false,
        });
        Assert.True(result.Generated);
        var ingested = Scenario(ingestDir, testId);

        // httpInteractions, member by member. The one difference allowed is the pinned gap of plan §3.2:
        // the failed send's error (RequestResponseLog.Error is not on the wire; 14.1).
        var a = inProcess.GetProperty("httpInteractions").EnumerateArray().ToArray();
        var b = ingested.GetProperty("httpInteractions").EnumerateArray().ToArray();
        Assert.Equal(6, a.Length);
        Assert.Equal(a.Length, b.Length);
        var differences = new List<(int Index, string Member, string InProcess, string Ingested)>();
        for (var i = 0; i < a.Length; i++)
        {
            var names = a[i].EnumerateObject().Select(p => p.Name).Union(b[i].EnumerateObject().Select(p => p.Name));
            foreach (var name in names)
            {
                var x = a[i].TryGetProperty(name, out var pa) ? pa.GetRawText() : "<absent>";
                var y = b[i].TryGetProperty(name, out var pb) ? pb.GetRawText() : "<absent>";
                if (x != y)
                    differences.Add((i, name, x, y));
            }
        }
        var difference = Assert.Single(differences);
        Assert.Equal((5, "error"), (difference.Index, difference.Member));
        Assert.Contains("boom", difference.InProcess);
        Assert.Equal("null", difference.Ingested);

        // The measured durations were believed on both sides, and the step bar attributed on both.
        Assert.Equal(77, a[0].GetProperty("durationMs").GetDouble());
        Assert.Equal(77, b[1].GetProperty("durationMs").GetDouble());
        Assert.All(b, i => Assert.Equal("0", i.GetProperty("stepPath").GetString()));
        Assert.Equal(inProcess.GetProperty("steps").GetRawText(), ingested.GetProperty("steps").GetRawText());
        Assert.Equal("a basket", Assert.Single(result.Features[0].Scenarios[0].Steps!).Text);

        // annotations, and the diagram source byte for byte.
        var annotations = inProcess.GetProperty("annotations");
        Assert.Equal(annotations.GetRawText(), ingested.GetProperty("annotations").GetRawText());
        Assert.Equal(["Row", "Custom"], annotations.EnumerateArray().Select(x => x.GetProperty("kind").GetString()));
        var diagramA = inProcess.GetProperty("diagrams")[0].GetString()!;
        var diagramB = ingested.GetProperty("diagrams")[0].GetString()!;
        Assert.Contains("<<stepDelimiter>>", diagramA);
        Assert.Contains("<<assertionNote>>", diagramA);
        Assert.Contains("cache warmed", diagramA);
        Assert.Contains("Row 3", diagramA);
        Assert.Equal(separateSetup, diagramA.Contains("partition", StringComparison.Ordinal));
        Assert.Equal(diagramA, diagramB);
    }

    /// <summary>
    /// One scenario in enqueue order with strictly increasing, tick-aligned timestamps and no overlapping
    /// calls: a step bar, a pair with a measured duration, a row band, the phase boundary, a custom
    /// fragment, an assertion note, a pair measured on both halves, and a failed send.
    /// </summary>
    private static List<RequestResponseLog> Fixture(string testId)
    {
        var traceA = Guid.NewGuid(); var rrA = Guid.NewGuid();
        var traceB = Guid.NewGuid(); var rrB = Guid.NewGuid();
        var traceC = Guid.NewGuid(); var rrC = Guid.NewGuid();
        var failure = new HttpRequestException("boom");
        return
        [
            Marker(testId, DiagramMarkerKind.Step, InteractionRecord.StepDelimiterPlantUml("Given", "a basket"), isStart: true, T0.AddMilliseconds(1000)),
            Marker(testId, DiagramMarkerKind.Step, null, isStart: false, T0.AddMilliseconds(1001)),
            new("Probe", testId, HttpMethod.Post, "{}", new Uri("http://localhost:8081/sidekick"), [("Content-Type", "application/json")], "graphql", "web", RequestResponseType.Request, traceA, rrA, false) { Timestamp = T0.AddMilliseconds(2000), Phase = TestPhase.Setup },
            new("Probe", testId, HttpMethod.Post, """{"data":{}}""", new Uri("http://localhost:8081/sidekick"), [], "graphql", "web", RequestResponseType.Response, traceA, rrA, false, HttpStatusCode.OK) { Timestamp = T0.AddMilliseconds(2050), DurationMs = 77, Phase = TestPhase.Setup },
            Marker(testId, DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3", isStart: true, T0.AddMilliseconds(3000)),
            Marker(testId, DiagramMarkerKind.Row, null, isStart: false, T0.AddMilliseconds(3001)),
            new("Probe", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true, MarkerKind = DiagramMarkerKind.Phase, Timestamp = T0.AddMilliseconds(3500) },
            Marker(testId, DiagramMarkerKind.Custom, "note over graphql : cache warmed", isStart: true, T0.AddMilliseconds(4000)),
            Marker(testId, DiagramMarkerKind.Custom, null, isStart: false, T0.AddMilliseconds(4001)),
            Marker(testId, DiagramMarkerKind.Assertion, InteractionRecord.AssertionNotePlantUml("the basket is empty", true, null), isStart: true, T0.AddMilliseconds(4500)),
            Marker(testId, DiagramMarkerKind.Assertion, null, isStart: false, T0.AddMilliseconds(4501)),
            new("Probe", testId, HttpMethod.Get, null, new Uri("http://localhost:8081/health"), [], "web", "web", RequestResponseType.Request, traceB, rrB, false) { Timestamp = T0.AddMilliseconds(5000), DurationMs = 5, Phase = TestPhase.Action },
            new("Probe", testId, HttpMethod.Get, """{"ok":true}""", new Uri("http://localhost:8081/health"), [], "web", "web", RequestResponseType.Response, traceB, rrB, false, HttpStatusCode.OK) { Timestamp = T0.AddMilliseconds(5005), DurationMs = 5, Phase = TestPhase.Action },
            new("Probe", testId, HttpMethod.Post, """{"amount":1}""", new Uri("http://localhost:8081/pay"), [], "payments", "web", RequestResponseType.Request, traceC, rrC, false) { Timestamp = T0.AddMilliseconds(6000), Phase = TestPhase.Action },
            new("Probe", testId, HttpMethod.Post, null, new Uri("http://localhost:8081/pay"), [], "payments", "web", RequestResponseType.Response, traceC, rrC, false, FailedSend.Status(failure)) { Timestamp = T0.AddMilliseconds(6100), Error = FailedSend.Describe(failure), Phase = TestPhase.Action },
        ];
    }

    /// <summary>An override half as DefaultTrackingDiagramOverride emits it: the fragment buffered by newlines.</summary>
    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string? plantUml, bool isStart, DateTimeOffset at) =>
        new("Probe", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = isStart,
            IsOverrideEnd = !isStart,
            MarkerKind = kind,
            PlantUml = plantUml is null ? null : $"\n{plantUml}\n\n",
            Timestamp = at,
        };

    private static ReportConfigurationOptions Options(string directory, bool separateSetup)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = directory;
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        options.SeparateSetup = separateSetup;
        return options;
    }

    private static JsonElement Scenario(string directory, string testId)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "TestRunReport.json")));
        return json.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == testId)
            .Clone();
    }
}
