using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// <c>plans/PHASE_FROM_STEPS_PLAN.md</c> (#130): <c>--phase-from-steps</c> on a healthy run. Until R1 it recorded how many
/// records it phased as a diagnostic of kind <c>Other</c>, so every query answer carried a <c>!</c> line and, with history
/// off, the line alone wrote the labs page; and it phased each half of a call on its own timestamp, so a response
/// answered after its step ended took no phase while its request took the step's.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestPhaseFromStepsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-phase-from-steps-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);
    private const string TestId = "places-an-order";

    public IngestPhaseFromStepsTests()
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
    public void A_healthy_phase_from_steps_ingest_records_no_diagnostic()
    {
        var (result, _) = Ingest("diagnostics");

        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == DiagnosticKind.Other);
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("phase", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, result.PhasedRecords);
    }

    [Fact]
    public void Both_halves_of_a_late_call_carry_one_phase_in_the_data_file()
    {
        var (_, reports) = Ingest("data-file");

        var calls = Interactions(reports);
        Assert.Equal(4, calls.Length);
        Assert.All(calls, c => Assert.Equal(nameof(TestPhase.Action), c.GetProperty("phase").GetString()));
    }

    [Fact]
    public void A_healthy_phase_from_steps_run_writes_no_labs_page_with_history_off()
    {
        // The test projects run with KRONIKOL_HISTORY=off, so a diagnostic is the only thing that could write the page.
        var (_, reports) = Ingest("labs");

        Assert.True(File.Exists(Path.Combine(reports, "TestRunReport.html")));
        Assert.False(File.Exists(Path.Combine(reports, "TestRunReport.labs.html")));
    }

    [Fact]
    public void A_phase_from_steps_report_heads_no_query_answer()
    {
        var (_, reports) = Ingest("query");

        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["failures", reports], output, new StringWriter()));

        var lines = output.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains("nothing failed", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith('!'));
    }

    [Fact]
    public void The_response_halfs_address_shows_its_calls_phase()
    {
        var (_, reports) = Ingest("address");

        // The late call's response half: the database read answered two seconds after its step ended.
        var calls = Interactions(reports);
        var late = Array.FindIndex(calls, c => c.GetProperty("uri").GetString() == "http://db/orders/1"
                                               && c.GetProperty("type").GetString() == "Response");
        Assert.True(late >= 0);

        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["http", reports, $"s0/i{late}"], output, new StringWriter()));
        Assert.Contains("/orders/1", output.ToString());
        Assert.Contains("phase Action", output.ToString());
    }

    /// <summary>#130's repro: one test whose 10 ms When step makes two calls; the database read is answered 2 s later.</summary>
    private (IngestResult Result, string Reports) Ingest(string name)
    {
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = TestId, TestName = "places an order", Feature = "orders.test.ts", Timestamp = T0 },
            new() { Event = "step", TestId = TestId, Keyword = "When", Text = "the order is placed", DurationMs = 10, Timestamp = T0 },
            new() { Event = "end", TestId = TestId, Status = "passed", DurationMs = 10, Timestamp = T0.AddMilliseconds(10) },
        ];
        InteractionRecord[] calls =
        [
            Half("Request", "POST", "http://api/orders", "api", "a", 1),
            Half("Request", "GET", "http://db/orders/1", "db", "b", 3),
            Half("Response", "POST", "http://api/orders", "api", "a", 8),
            Half("Response", "GET", "http://db/orders/1", "db", "b", 2000),
        ];

        var capture = Path.Combine(_dir, name + ".ndjson");
        File.WriteAllLines(capture, calls.Select(r => r.ToJson()));
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, name);
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestRecords = tests,
            Options = options,
            PhaseFromSteps = true,
        });

        Assert.True(result.Generated);
        return (result, options.ReportsFolderPath);
    }

    private static InteractionRecord Half(string type, string method, string uri, string service, string pairId, double atMs) => new()
    {
        Type = type,
        Method = method,
        Uri = uri,
        ServiceName = service,
        CallerName = "orders-test",
        TestId = TestId,
        RequestResponseId = pairId,
        Timestamp = T0.AddMilliseconds(atMs),
    };

    private static JsonElement[] Interactions(string reports)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(reports, "TestRunReport.json")));
        return json.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0]
            .GetProperty("httpInteractions").EnumerateArray()
            .Where(c => c.TryGetProperty("uri", out var uri) && uri.GetString() is { } u && u.StartsWith("http://", StringComparison.Ordinal))
            .Select(c => c.Clone())
            .ToArray();
    }
}
