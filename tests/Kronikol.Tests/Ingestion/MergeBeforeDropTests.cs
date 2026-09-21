using Kronikol.Ingestion;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// #75 section 2 (plans/HISTORY_VERDICT_NOISE_PLAN.md S2): a wire record whose claim is contested used to be
/// dropped before the merger could give it its span twin's test. The merge now runs after the attribution
/// passes and before the drop, and pairs that already agree on their test pair first - which is what keeps
/// a record nobody could identify from taking the twin of one somebody did.
/// </summary>
[Collection("DiagramsFetcher")]
public class MergeBeforeDropTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-mergedrop-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    public MergeBeforeDropTests() { Directory.CreateDirectory(_dir); RequestResponseLogger.Redaction = null; }
    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static InteractionRecord Mongo(string type, string pairId, string testId, string capturedBy, double atMs, string? content = null) => new()
    {
        Type = type,
        Method = capturedBy == "span" ? "Find" : "Find ← Trial",
        Uri = "mongodb:///app-db/Trial",
        ServiceName = "mongo",
        CallerName = "api",
        TestId = testId,
        RequestResponseId = pairId,
        CapturedBy = capturedBy,
        ActivitySpanId = capturedBy == "span" ? "00f067aa0ba902b7" : null,
        Content = content,
        StatusCode = type == "Response" ? "OK" : null,
        Timestamp = T0.AddSeconds(10).AddMilliseconds(atMs),
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_wire_record_whose_span_twin_is_attributed_survives_a_contested_claim(bool sweepOverlaps)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Report");
        options.GenerateComponentDiagram = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions =
            [
                Mongo("Request", "span-1", "worker-a", "span", 0),
                Mongo("Response", "span-1", "worker-a", "span", 2),
                Mongo("Request", "wire-1", "session", "wire", 0, """{ "CustomerId" : "cust-111" }"""),
                Mongo("Response", "wire-1", "session", "wire", 2, """{ "CustomerId" : "cust-111", "Status" : "Active" }"""),
            ],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "worker-a", TestName = "worker-a", Timestamp = T0, Claims = ["cust-111"] },
                new TestRunRecord { Event = "end", TestId = "worker-a", Status = "passed", Timestamp = T0.AddSeconds(20) },
                new TestRunRecord { Event = "start", TestId = "sweep", TestName = "sweep", Timestamp = T0.AddSeconds(sweepOverlaps ? 1 : 30), Claims = ["cust-111", "cust-222"] },
                new TestRunRecord { Event = "end", TestId = "sweep", Status = "passed", Timestamp = T0.AddSeconds(60) },
            ],
            Options = options,
            AttributeByClaims = true,
            AttributeByTestWindow = true,
            WindowAttribution = WindowAttributionMode.ExclusiveOnly,
            WindowAttributionFallbackId = "session",
            MergeDuplicateInteractions = true,
            DropUnattributed = record => record.ServiceName == "mongo",
        });

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));
        var workerA = document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == "worker-a");
        var request = workerA.GetProperty("httpInteractions").EnumerateArray()
            .Single(i => i.GetProperty("serviceName").GetString() == "mongo" && i.GetProperty("type").GetString() == "Request");

        Assert.Contains("cust-111", request.GetRawText());
    }

    /// <summary>The plan's READ claim: a wire record that knows its test loses it to a span twin that does not.</summary>
    [Fact]
    public void A_merge_does_not_trade_a_known_test_for_the_fallback()
    {
        var merged = InteractionMerger.Merge(
        [
            Mongo("Request", "span-1", "session", "span", 0),
            Mongo("Response", "span-1", "session", "span", 2),
            Mongo("Request", "wire-1", "worker-a", "wire", 0, """{ "CustomerId" : "cust-111" }"""),
            Mongo("Response", "wire-1", "worker-a", "wire", 2, """{ "Status" : "Active" }"""),
        ], InteractionMerger.DefaultOverlapThreshold, fallbackTestId: "session");

        Assert.All(merged, record => Assert.Equal("worker-a", record.TestId));
    }

    /// <summary>F1, pinned as what it is: a contested wire record with no span twin has nothing to be rescued by.</summary>
    [Fact]
    public void A_contested_wire_record_with_no_span_twin_is_still_dropped()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Report");
        options.GenerateComponentDiagram = false;
        InteractionRecord Redis(string type, double atMs) => new()
        {
            Type = type, Method = "GET", Uri = "redis:///app:_Report-Dates_cust-111", ServiceName = "redis", CallerName = "api",
            TestId = "session", RequestResponseId = "redis-1", CapturedBy = "wire", Content = type == "Request" ? "GET app:_Report-Dates_cust-111" : "(hit)",
            StatusCode = type == "Response" ? "OK" : null, Timestamp = T0.AddSeconds(10).AddMilliseconds(atMs),
        };

        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions =
            [
                Mongo("Request", "span-1", "worker-a", "span", 0), Mongo("Response", "span-1", "worker-a", "span", 2),
                Redis("Request", 3), Redis("Response", 4),
            ],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "worker-a", TestName = "worker-a", Timestamp = T0, Claims = ["cust-111"] },
                new TestRunRecord { Event = "end", TestId = "worker-a", Status = "passed", Timestamp = T0.AddSeconds(20) },
                new TestRunRecord { Event = "start", TestId = "sweep", TestName = "sweep", Timestamp = T0.AddSeconds(1), Claims = ["cust-111", "cust-222"] },
                new TestRunRecord { Event = "end", TestId = "sweep", Status = "passed", Timestamp = T0.AddSeconds(60) },
            ],
            Options = options, AttributeByClaims = true, AttributeByTestWindow = true,
            WindowAttribution = WindowAttributionMode.ExclusiveOnly, WindowAttributionFallbackId = "session",
            MergeDuplicateInteractions = true, DropUnattributed = record => record.ServiceName is "mongo" or "redis",
        });

        Assert.DoesNotContain("\"redis\"", File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));
    }

    /// <summary>
    /// The plan's own claim under test: merging earlier "changes which records survive, not which pair".
    /// A stranger's wire record - no claim of worker-a's, inside two test windows, so dropped today before
    /// the merger ever sees it - overlaps worker-a's span as well as the true twin does, and starts closer.
    /// </summary>
    [Fact]
    public void A_record_that_would_have_been_dropped_does_not_take_another_records_span_twin()
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Report");
        options.GenerateComponentDiagram = false;

        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions =
            [
                Mongo("Request", "span-1", "worker-a", "span", 0),
                Mongo("Response", "span-1", "worker-a", "span", 10),
                Mongo("Request", "twin", "session", "wire", 1, """{ "CustomerId" : "cust-111" }"""),
                Mongo("Response", "twin", "session", "wire", 9, """{ "CustomerId" : "cust-111", "Status" : "Active" }"""),
                Mongo("Request", "stranger", "session", "wire", 0, """{ "CustomerId" : "cust-999" }"""),
                Mongo("Response", "stranger", "session", "wire", 10, """{ "CustomerId" : "cust-999", "Status" : "Seeded" }"""),
            ],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "worker-a", TestName = "worker-a", Timestamp = T0, Claims = ["cust-111"] },
                new TestRunRecord { Event = "end", TestId = "worker-a", Status = "passed", Timestamp = T0.AddSeconds(20) },
                new TestRunRecord { Event = "start", TestId = "worker-b", TestName = "worker-b", Timestamp = T0.AddSeconds(1), Claims = ["cust-222"] },
                new TestRunRecord { Event = "end", TestId = "worker-b", Status = "passed", Timestamp = T0.AddSeconds(30) },
            ],
            Options = options,
            AttributeByClaims = true,
            AttributeByTestWindow = true,
            WindowAttribution = WindowAttributionMode.ExclusiveOnly,
            WindowAttributionFallbackId = "session",
            MergeDuplicateInteractions = true,
            DropUnattributed = record => record.ServiceName == "mongo",
        });

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));
        var requests = document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == "worker-a")
            .GetProperty("httpInteractions").EnumerateArray()
            .Where(i => i.GetProperty("serviceName").GetString() == "mongo" && i.GetProperty("type").GetString() == "Request")
            .Select(i => i.GetRawText()).ToArray();

        Assert.Single(requests);
        Assert.Contains("cust-111", requests[0]);
        Assert.DoesNotContain("cust-999", requests[0]);
    }

    private IngestRequest Contested(InteractionRecord[] interactions, bool merge = true)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, "Report");
        options.GenerateComponentDiagram = false;
        return new IngestRequest
        {
            Interactions = interactions,
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = "worker-a", TestName = "worker-a", Timestamp = T0, Claims = ["cust-111"] },
                new TestRunRecord { Event = "end", TestId = "worker-a", Status = "passed", Timestamp = T0.AddSeconds(20) },
                new TestRunRecord { Event = "start", TestId = "sweep", TestName = "sweep", Timestamp = T0.AddSeconds(1), Claims = ["cust-111", "cust-222"] },
                new TestRunRecord { Event = "end", TestId = "sweep", Status = "passed", Timestamp = T0.AddSeconds(60) },
            ],
            Options = options,
            AttributeByClaims = true,
            AttributeByTestWindow = true,
            WindowAttribution = WindowAttributionMode.ExclusiveOnly,
            WindowAttributionFallbackId = "session",
            MergeDuplicateInteractions = merge,
            DropUnattributed = record => record.ServiceName == "mongo",
        };
    }

    /// <summary>#75 section 2b: finding the cause took a hand correlation of test windows. The diagnostic names it.</summary>
    [Fact]
    public void The_contested_claims_diagnostic_names_the_tests_and_the_claim()
    {
        var result = IngestPipeline.Run(Contested(
        [
            Mongo("Request", "span-1", "worker-a", "span", 0),
            Mongo("Response", "span-1", "worker-a", "span", 2),
            Mongo("Request", "wire-1", "session", "wire", 0, """{ "CustomerId" : "cust-111" }"""),
            Mongo("Response", "wire-1", "session", "wire", 2, """{ "CustomerId" : "cust-111", "Status" : "Active" }"""),
        ]));

        var contested = Assert.Single(result.Diagnostics, d => d.Message.Contains("matched the claims of more than one in-flight test"));
        Assert.Contains("most contested: sweep × worker-a on \"cust-111\" (2)", contested.Message);
    }

    /// <summary>A rescued record no longer needs the passes that could not place it, and the numbers say so.</summary>
    [Fact]
    public void A_rescued_record_is_not_counted_as_dropped_or_unattributed()
    {
        var result = IngestPipeline.Run(Contested(
        [
            Mongo("Request", "span-1", "worker-a", "span", 0),
            Mongo("Response", "span-1", "worker-a", "span", 2),
            Mongo("Request", "wire-1", "session", "wire", 0, """{ "CustomerId" : "cust-111" }"""),
            Mongo("Response", "wire-1", "session", "wire", 2, """{ "CustomerId" : "cust-111", "Status" : "Active" }"""),
        ]));

        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("dropped by DropUnattributed"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains("could not be attributed"));
        Assert.Equal(2, result.InteractionCount);
    }

    /// <summary>
    /// Where no claim is contested the move changes nothing: merging inside the pipeline gives what merging
    /// the sorted input by hand and ingesting that gives, which is what the pipeline did before the move.
    /// </summary>
    [Fact]
    public void Without_a_contest_the_pipeline_merges_exactly_as_the_merger_alone_does()
    {
        InteractionRecord[] records =
        [
            Mongo("Request", "span-1", "worker-a", "span", 0), Mongo("Response", "span-1", "worker-a", "span", 2),
            Mongo("Request", "wire-1", "worker-a", "wire", 0, """{ "CustomerId" : "cust-111" }"""),
            Mongo("Response", "wire-1", "worker-a", "wire", 2, """{ "Status" : "Active" }"""),
            Mongo("Request", "span-2", "worker-a", "span", 40), Mongo("Response", "span-2", "worker-a", "span", 44),
            Mongo("Request", "wire-2", "worker-a", "wire", 41, """{ "CustomerId" : "cust-111", "n" : 2 }"""),
            Mongo("Response", "wire-2", "worker-a", "wire", 44, """{ "Status" : "Gone" }"""),
        ];

        string Interactions(IngestRequest request)
        {
            var result = IngestPipeline.Run(request);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));
            return string.Join("\n", document.RootElement.GetProperty("features").EnumerateArray()
                .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
                .SelectMany(s => s.GetProperty("httpInteractions").EnumerateArray()
                    .Select(i => $"{s.GetProperty("id").GetString()} {i.GetProperty("type").GetString()} {i.GetProperty("method").GetString()} {(i.TryGetProperty("content", out var c) ? c.GetString() : null)}")));
        }

        var inPipeline = Interactions(Contested(records));
        var byHand = Interactions(Contested([.. InteractionMerger.Merge(records)], merge: false));

        Assert.Equal(byHand, inPipeline);
        Assert.Equal(4, inPipeline.Split('\n').Length);
    }

    /// <summary>When the span has no identity either, the wire record's name stands with its id.</summary>
    [Fact]
    public void A_wire_record_that_keeps_its_test_keeps_its_name()
    {
        var merged = InteractionMerger.Merge(
        [
            Mongo("Request", "span-1", "session", "span", 0) with { TestName = "Unknown" },
            Mongo("Response", "span-1", "session", "span", 2) with { TestName = "Unknown" },
            Mongo("Request", "wire-1", "worker-a", "wire", 0, """{ "CustomerId" : "cust-111" }""") with { TestName = "Worker A" },
            Mongo("Response", "wire-1", "worker-a", "wire", 2, """{ "Status" : "Active" }""") with { TestName = "Worker A" },
        ], InteractionMerger.DefaultOverlapThreshold, fallbackTestId: "session");

        Assert.All(merged, record => Assert.Equal("Worker A", record.TestName));
        Assert.All(merged, record => Assert.Equal(InteractionMerger.MergedSource, record.CapturedBy));
    }
}
