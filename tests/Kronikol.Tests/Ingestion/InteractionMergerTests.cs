using Kronikol.Ingestion;
using Kronikol.Reports;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// The ingest-time reconciliation of the two capture paths: a wire tap that sees the protocol but guesses
/// the test, and an OTLP span tap that knows the test but not the payload. One call, one arrow.
/// </summary>
[Collection("DiagramsFetcher")]
public class InteractionMergerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    private static IEnumerable<InteractionRecord> Wire(
        string method, string uri, double startMs, double durationMs,
        string testId = "outside-any-test", string caller = "data-insights", string service = "mongo",
        string? content = "{ \"filter\": { \"_id\": 1 } }", string status = "OK", string? capturedBy = null)
    {
        var (request, response) = InteractionRecord.Pair(
            testId, null, method, uri, service, caller,
            requestContent: content,
            responseContent: "[{ \"_id\": 1 }]",
            statusCode: status,
            requestTimestamp: T0.AddMilliseconds(startMs),
            responseTimestamp: T0.AddMilliseconds(startMs + durationMs),
            requestResponseId: Guid.NewGuid().ToString("N"),
            dependencyCategory: "MongoDB");
        yield return request with { CapturedBy = capturedBy };
        yield return response with { CapturedBy = capturedBy };
    }

    private static IEnumerable<InteractionRecord> Span(
        string method, string uri, double startMs, double durationMs,
        string testId = "4bf92f3577b34da6a3ce929d0e0e4736", string caller = "data-insights", string service = "mongo",
        string? spanId = null, string? capturedBy = null)
    {
        spanId ??= Guid.NewGuid().ToString("N")[..16];
        var (request, response) = InteractionRecord.Pair(
            testId, null, method, uri, service, caller,
            statusCode: "OK",
            requestTimestamp: T0.AddMilliseconds(startMs),
            responseTimestamp: T0.AddMilliseconds(startMs + durationMs),
            requestResponseId: spanId,
            traceId: testId,
            dependencyCategory: "MongoDB",
            activityTraceId: testId,
            activitySpanId: spanId);
        yield return request with { CapturedBy = capturedBy };
        yield return response with { CapturedBy = capturedBy };
    }

    [Fact]
    public void A_wire_record_and_its_span_twin_become_one_arrow_with_the_span_identity_and_the_wire_body()
    {
        List<InteractionRecord> records =
        [
            .. Wire("Find ← Trial", "mongodb:///data-insights/Trial", 0, 12),
            .. Span("Find ← Trial", "mongodb:///data-insights-Development/Trial", 1, 10),
        ];

        var merged = InteractionMerger.Merge(records);

        Assert.Equal(2, merged.Count);
        var request = merged[0];
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", request.TestId);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", request.ActivityTraceId);
        Assert.NotNull(request.ActivitySpanId);
        Assert.Equal("{ \"filter\": { \"_id\": 1 } }", request.Content); // the wire's fidelity survives
        Assert.Equal("mongodb:///data-insights/Trial", request.Uri);
        Assert.Equal(InteractionMerger.MergedSource, request.CapturedBy);
        Assert.Contains(request.Headers!, h => h.Key == InteractionMerger.CapturedByHeader && h.Value == "wire + span");

        var response = merged[1];
        Assert.Equal("Response", response.Type);
        Assert.Equal("[{ \"_id\": 1 }]", response.Content);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", response.TestId);
        Assert.Equal(InteractionMerger.MergedSource, response.CapturedBy);
    }

    [Fact]
    public void Explicit_capture_stamps_are_honoured_and_the_wire_label_wins()
    {
        // A wire record that happens to carry a span id, and a span record that happens to carry content:
        // only the explicit capturedBy stamps make the pairing possible.
        var wire = Wire("Get (Hit)", "redis://db0/insights:1", 0, 5, service: "redis", capturedBy: InteractionMerger.WireSource).ToList();
        var span = Span("GET", "redis://db0/insights:1", 1, 3, service: "redis", capturedBy: InteractionMerger.SpanSource).ToList();

        var merged = InteractionMerger.Merge([.. wire, .. span]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("Get (Hit)", merged[0].Method); // the wire's hit/miss label wins
    }

    [Fact]
    public void An_overlap_below_the_threshold_is_a_different_call()
    {
        List<InteractionRecord> records =
        [
            .. Wire("Find ← Trial", "mongodb:///db/Trial", 0, 10),
            .. Span("Find ← Trial", "mongodb:///db/Trial", 7, 10),
        ];

        Assert.Equal(4, InteractionMerger.Merge(records).Count);       // 30 % of the shorter interval
        Assert.Equal(2, InteractionMerger.Merge(records, 0.3).Count);  // …but a lenient threshold pairs them
    }

    [Fact]
    public void Bursts_pair_off_one_to_one_by_best_overlap()
    {
        List<InteractionRecord> records =
        [
            .. Wire("Find ← Trial", "mongodb:///db/Trial", 0, 10),
            .. Wire("Find ← Trial", "mongodb:///db/Trial", 100, 10),
            .. Wire("Find ← Trial", "mongodb:///db/Trial", 200, 10),
            .. Span("Find ← Trial", "mongodb:///db/Trial", 201, 8),
            .. Span("Find ← Trial", "mongodb:///db/Trial", 1, 8),
        ];

        var merged = InteractionMerger.Merge(records);

        // Two of the three wire calls found a twin; the third stays wire-only, and no span is reused.
        Assert.Equal(6, merged.Count);
        Assert.Equal(2, merged.Count(r => r.CapturedBy == InteractionMerger.MergedSource && r.Type == "Request"));
        Assert.DoesNotContain(merged, r => r.CapturedBy == InteractionMerger.SpanSource);
        var unmerged = merged.Single(r => r.Type == "Request" && r.CapturedBy != InteractionMerger.MergedSource);
        Assert.Equal(T0.AddMilliseconds(100), unmerged.Timestamp);
    }

    [Fact]
    public void One_span_never_merges_with_two_wire_records()
    {
        List<InteractionRecord> records =
        [
            .. Wire("Find ← Trial", "mongodb:///db/Trial", 0, 10),
            .. Wire("Find ← Trial", "mongodb:///db/Trial", 0, 10),
            .. Span("Find ← Trial", "mongodb:///db/Trial", 0, 10),
        ];

        var merged = InteractionMerger.Merge(records);

        Assert.Equal(4, merged.Count);
        Assert.Single(merged, r => r.Type == "Request" && r.CapturedBy == InteractionMerger.MergedSource);
    }

    [Fact]
    public void Wire_only_and_span_only_traffic_is_left_exactly_as_it_was()
    {
        List<InteractionRecord> wireOnly = [.. Wire("Get (Miss)", "redis://db0/insights:2", 0, 4, service: "redis")];
        Assert.Equal(wireOnly, InteractionMerger.Merge(wireOnly));

        List<InteractionRecord> spanOnly = [.. Span("Find ← Trial", "mongodb:///db/Trial", 0, 4)];
        Assert.Equal(spanOnly, InteractionMerger.Merge(spanOnly));
    }

    [Fact]
    public void Different_participants_methods_or_keys_never_merge()
    {
        var wire = Wire("Find ← Trial", "mongodb:///db/Trial", 0, 10).ToList();

        Assert.Equal(4, InteractionMerger.Merge([.. wire, .. Span("Find ← Trial", "mongodb:///db/Trial", 0, 10, service: "mongo-secondary")]).Count);
        Assert.Equal(4, InteractionMerger.Merge([.. wire, .. Span("Find ← Trial", "mongodb:///db/Trial", 0, 10, caller: "superpay-graphql")]).Count);
        Assert.Equal(4, InteractionMerger.Merge([.. wire, .. Span("Insert → Trial", "mongodb:///db/Trial", 0, 10)]).Count);
        Assert.Equal(4, InteractionMerger.Merge([.. wire, .. Span("Find ← Session", "mongodb:///db/Session", 0, 10)]).Count);
    }

    [Fact]
    public void Markers_user_actions_and_records_without_timestamps_are_untouched()
    {
        List<InteractionRecord> records =
        [
            InteractionRecord.StepMarker("t", "Given a trial", T0),
            InteractionRecord.AssertionMarker("t", "it is accepted", true, T0.AddMilliseconds(1)),
            InteractionRecord.UserAction("t", "Click \"Accept\"", "http://web/", T0.AddMilliseconds(2)),
            .. Wire("Find ← Trial", "mongodb:///db/Trial", 0, 10),
            .. Span("Find ← Trial", "mongodb:///db/Trial", 0, 10),
        ];

        var merged = InteractionMerger.Merge(records);

        Assert.Equal(5, merged.Count);
        Assert.Equal(records[0], merged[0]);
        Assert.Equal(records[1], merged[1]);
        Assert.Equal(records[2], merged[2]);
    }

    [Fact]
    public void The_verb_and_the_key_are_what_identify_a_call()
    {
        Assert.Equal("get", InteractionMerger.Verb("Get (Hit)"));
        Assert.Equal("find", InteractionMerger.Verb("Find ← Trial"));
        Assert.Equal("get", InteractionMerger.Verb("GET"));
        Assert.Equal("", InteractionMerger.Verb(null));

        Assert.Equal("trial", InteractionMerger.LastSegment("mongodb:///data-insights-Development/Trial"));
        Assert.Equal("insights:1", InteractionMerger.LastSegment("redis://db0/insights:1"));
        Assert.Equal("summary", InteractionMerger.LastSegment("http://localhost:9000/api/summary?trial=42"));
        Assert.Equal("", InteractionMerger.LastSegment("redis://db0/"));
    }

    [Fact]
    public void Overlap_is_measured_against_the_shorter_interval()
    {
        var a = (T0, T0.AddMilliseconds(100));
        Assert.Equal(1, InteractionMerger.OverlapRatio(a.Item1, a.Item2, T0.AddMilliseconds(10), T0.AddMilliseconds(20)));
        Assert.Equal(0.5, InteractionMerger.OverlapRatio(a.Item1, a.Item2, T0.AddMilliseconds(90), T0.AddMilliseconds(110)));
        Assert.Equal(0, InteractionMerger.OverlapRatio(a.Item1, a.Item2, T0.AddMilliseconds(200), T0.AddMilliseconds(300)));
        Assert.Equal(1, InteractionMerger.OverlapRatio(a.Item1, a.Item2, T0.AddMilliseconds(50), T0.AddMilliseconds(50)));
        Assert.Equal(0, InteractionMerger.OverlapRatio(a.Item1, a.Item2, T0.AddMilliseconds(500), T0.AddMilliseconds(500)));
    }

    [Fact]
    public void The_ingest_pipeline_merges_only_when_asked_to()
    {
        List<InteractionRecord> records =
        [
            .. Wire("Find ← Trial", "mongodb:///data-insights/Trial", 0, 12),
            .. Span("Find ← Trial", "mongodb:///data-insights-Development/Trial", 1, 10),
        ];

        var directory = Path.Combine(Path.GetTempPath(), "kronikol-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var plain = Run(records, directory, merge: false);
            Assert.Equal(4, plain.InteractionCount);
            Assert.Equal(2, plain.ScenarioCount); // the wire half is its own (mis-attributed) scenario

            var merged = Run(records, directory, merge: true);
            Assert.Equal(2, merged.InteractionCount);
            Assert.Equal(1, merged.ScenarioCount);
        }
        finally
        {
            DefaultDiagramsFetcher.Reset();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static IngestResult Run(IEnumerable<InteractionRecord> records, string directory, bool merge)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = directory;
        options.GenerateComponentDiagram = false;
        return IngestPipeline.Run(new IngestRequest
        {
            Interactions = records,
            Options = options,
            MergeDuplicateInteractions = merge,
        });
    }
}
