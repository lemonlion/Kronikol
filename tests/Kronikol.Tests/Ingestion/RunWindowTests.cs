using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// The run window (<see cref="IngestRequest.DropOutsideRunWindow"/>) and what is said about the records outside it
/// (plans/RUN_WINDOW_PLAN.md, #187).
/// </summary>
[Collection("DiagramsFetcher")] // shares the process-global tracking store with every class that runs the pipeline
public class RunWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-run-window-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    public RunWindowTests()
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

    private ReportConfigurationOptions Options(string name)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, name);
        options.GenerateComponentDiagram = false;
        return options;
    }

    private static InteractionRecord Record(string testId, string pairId, double atSecond, string type = "Request", string service = "orders") => new()
    {
        Type = type,
        Method = "GET",
        Uri = $"http://{service}/op",
        ServiceName = service,
        CallerName = "browser",
        TestId = testId,
        RequestResponseId = pairId,
        Timestamp = T0.AddSeconds(atSecond),
    };

    private static InteractionRecord[] Pair(string testId, string pairId, double atSecond, string service = "orders") =>
        [Record(testId, pairId, atSecond, service: service), Record(testId, pairId, atSecond + 0.1, "Response", service)];

    private static readonly TestRunRecord[] OneTest =
    [
        new() { Event = "testrun", TestId = "__run__", Status = "started", Timestamp = T0 },
        new() { Event = "start", TestId = "b1", TestName = "b1", Timestamp = T0.AddSeconds(1) },
        new() { Event = "end", TestId = "b1", Status = "passed", Timestamp = T0.AddSeconds(5) },
        new() { Event = "testrun", TestId = "__run__", Status = "passed", Timestamp = T0.AddSeconds(10) },
    ];

    /// <summary>The interaction records the report's data file holds for a scenario.</summary>
    private static int Calls(IngestResult result, string scenarioId)
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));
        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Where(s => s.GetProperty("id").GetString() == scenarioId)
            .Sum(s => ReportFile.Items(s, "httpInteractions").Count());
    }

    // ------------------------------------------------------------------------------------------
    // R1: a window that cannot be a window (section 3.9)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Harness P2b: on 5.2.3 an explicit window that ends before it starts dropped every record, the test's own call
    /// included, and printed the inverted window as if it were one.
    /// </summary>
    [Fact]
    public void An_explicit_window_that_ends_before_it_starts_drops_nothing_and_says_why()
    {
        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = [.. Pair("b1", "mine", 2), .. Pair("session", "late", 30)],
            TestRecords = OneTest,
            Options = Options("Inverted"),
            RunStartedAt = T0.AddSeconds(10),
            RunEndedAt = T0,
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test", "session"),
        });

        Assert.Equal(2, Calls(result, "b1"));
        Assert.Equal(2, Calls(result, "session"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == DiagnosticKind.DroppedOutsideRunWindow);
        var entry = Assert.Single(result.Diagnostics, d => d.Kind == DiagnosticKind.Other && d.Message.StartsWith("DropOutsideRunWindow:", StringComparison.Ordinal));
        Assert.Equal("DropOutsideRunWindow: the run window ends before it starts (2026-10-21T10:00:10.0000000+00:00 → 2026-10-21T10:00:00.0000000+00:00); nothing was dropped.",
            entry.Message);
    }

    [Fact]
    public void An_explicit_start_after_the_derived_end_is_not_applied_either()
    {
        // The tests file closes the run at +10 s; a host that starts the window at +20 s has given no window at all.
        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = [.. Pair("b1", "mine", 2)],
            TestRecords = OneTest,
            Options = Options("StartAfterEnd"),
            RunStartedAt = T0.AddSeconds(20),
        });

        Assert.Equal(2, Calls(result, "b1"));
        Assert.Contains(result.Diagnostics, d => d.Kind == DiagnosticKind.Other
            && d.Message == "DropOutsideRunWindow: the run window ends before it starts (2026-10-21T10:00:20.0000000+00:00 → 2026-10-21T10:00:10.0000000+00:00); nothing was dropped.");
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == DiagnosticKind.DroppedOutsideRunWindow);
    }

    [Fact]
    public void A_window_of_one_instant_is_a_window()
    {
        // Bounds are inclusive: a run that starts and ends at the same instant keeps what began then, and nothing is refused.
        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = [.. Pair("b1", "mine", 2), .. Pair("b1", "late", 3)],
            TestRecords = OneTest,
            Options = Options("Instant"),
            RunStartedAt = T0.AddSeconds(2),
            RunEndedAt = T0.AddSeconds(2),
        });

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("ends before it starts", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Kind == DiagnosticKind.DroppedOutsideRunWindow);
    }
}
