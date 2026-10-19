using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// A call's two halves through the whole ingest, the principle #130 applied to phases: an empty pair id pairs nothing,
/// as a missing one does, and a response read before its request goes to its request's test. Both were found by the
/// audit of <c>plans/PHASE_FROM_STEPS_PLAN.md</c> (2026-10-12).
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestCallPairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-call-pair-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    public IngestCallPairTests()
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
    public void Calls_with_an_empty_pair_id_are_read_as_calls_with_none()
    {
        // Two calls whose four records carry requestResponseId "". The replay gave all four one pair, so the second call
        // (404, "second-body") read as answered by the first call's response (OK, "first-body"), with no warning. With
        // the ids left out, each request is reported unpaired; an empty id must read the same way.
        var empty = QueryInteractions(IngestTwoCalls("empty", "\"requestResponseId\":\"\","));
        var missing = QueryInteractions(IngestTwoCalls("missing", ""));

        Assert.DoesNotContain(empty, l => l.Contains("/orders/2", StringComparison.Ordinal) && l.Contains(" OK ", StringComparison.Ordinal));
        Assert.Equal(missing, empty);
    }

    [Fact]
    public void A_response_read_before_its_request_is_attributed_and_phased_with_it()
    {
        // One database read with no test id, made inside the first test's When step and answered inside the second
        // test's window, with its response line first in the file. The window pass judged the response on its own
        // timestamp and gave it to the second test, unphased, so the first test's call showed no answer.
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = "first", TestName = "places an order", Feature = "orders.test.ts", Timestamp = T0 },
            new() { Event = "step", TestId = "first", Keyword = "When", Text = "the order is placed", DurationMs = 10, Timestamp = T0 },
            new() { Event = "end", TestId = "first", Status = "passed", DurationMs = 10, Timestamp = T0.AddMilliseconds(10) },
            new() { Event = "start", TestId = "second", TestName = "lists orders", Feature = "orders.test.ts", Timestamp = T0.AddSeconds(1) },
            new() { Event = "step", TestId = "second", Keyword = "Given", Text = "an order exists", DurationMs = 5000, Timestamp = T0.AddSeconds(1) },
            new() { Event = "end", TestId = "second", Status = "passed", DurationMs = 5000, Timestamp = T0.AddSeconds(6) },
        ];
        var capture = Path.Combine(_dir, "response-first.ndjson");
        File.WriteAllLines(capture,
        [
            """{"type":"Response","testId":"","requestResponseId":"b","method":"GET","uri":"http://db/orders/1","serviceName":"db","callerName":"api","statusCode":"200","timestamp":"2026-10-12T10:00:02.000Z"}""",
            """{"type":"Request","testId":"","requestResponseId":"b","method":"GET","uri":"http://db/orders/1","serviceName":"db","callerName":"api","timestamp":"2026-10-12T10:00:00.003Z"}""",
        ]);
        var options = Options("response-first");

        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestRecords = tests,
            Options = options,
            AttributeByTestWindow = true,
            PhaseFromSteps = true,
        });

        Assert.True(result.Generated);
        Assert.Equal(2, result.WindowAttributedRecords);
        Assert.Equal(2, result.PhasedRecords);
        var holding = HalvesByScenario(options.ReportsFolderPath!, "http://db/orders/1").Where(s => s.Length > 0).ToArray();
        var halves = Assert.Single(holding);
        Assert.Equal(new[] { "Request", "Response" }, halves.Select(h => h.Type).Order(StringComparer.Ordinal));
        Assert.All(halves, h => Assert.Equal(nameof(TestPhase.Action), h.Phase));
    }

    [Fact]
    public void A_call_made_between_two_tests_is_not_given_half_to_the_second()
    {
        // The request is in no test's window, the response inside the second test's: the second test showed a lone
        // response of a call it never made, while its request went to no test.
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = "first", TestName = "places an order", Feature = "orders.test.ts", Timestamp = T0 },
            new() { Event = "end", TestId = "first", Status = "passed", DurationMs = 10, Timestamp = T0.AddMilliseconds(10) },
            new() { Event = "start", TestId = "second", TestName = "lists orders", Feature = "orders.test.ts", Timestamp = T0.AddSeconds(1) },
            new() { Event = "end", TestId = "second", Status = "passed", DurationMs = 5000, Timestamp = T0.AddSeconds(6) },
        ];
        var capture = Path.Combine(_dir, "between-tests.ndjson");
        File.WriteAllLines(capture,
        [
            """{"type":"Request","testId":"","requestResponseId":"g","method":"GET","uri":"http://db/orders/1","serviceName":"db","callerName":"api","timestamp":"2026-10-12T10:00:00.500Z"}""",
            """{"type":"Response","testId":"","requestResponseId":"g","method":"GET","uri":"http://db/orders/1","serviceName":"db","callerName":"api","statusCode":"200","timestamp":"2026-10-12T10:00:02.000Z"}""",
        ]);
        var options = Options("between-tests");

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [capture], TestRecords = tests, Options = options, AttributeByTestWindow = true });

        Assert.True(result.Generated);
        Assert.Equal(0, result.WindowAttributedRecords);
        Assert.All(HalvesByScenario(options.ReportsFolderPath!, "http://db/orders/1"), Assert.Empty);
    }

    /// <summary>One test making two calls, GET /orders/1 (OK, "first-body") then GET /orders/2 (404, "second-body").</summary>
    private string IngestTwoCalls(string name, string pairIdJson)
    {
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = "t", TestName = "shows two orders", Feature = "orders.test.ts", Timestamp = T0 },
            new() { Event = "end", TestId = "t", Status = "passed", DurationMs = 10, Timestamp = T0.AddMilliseconds(10) },
        ];
        var capture = Path.Combine(_dir, name + ".ndjson");
        File.WriteAllLines(capture,
        [
            "{\"type\":\"Request\",\"testId\":\"t\"," + pairIdJson + "\"method\":\"GET\",\"uri\":\"http://api/orders/1\",\"serviceName\":\"api\",\"callerName\":\"web\",\"timestamp\":\"2026-10-12T10:00:00.001Z\"}",
            "{\"type\":\"Response\",\"testId\":\"t\"," + pairIdJson + "\"method\":\"GET\",\"uri\":\"http://api/orders/1\",\"serviceName\":\"api\",\"callerName\":\"web\",\"statusCode\":\"200\",\"content\":\"first-body\",\"timestamp\":\"2026-10-12T10:00:00.002Z\"}",
            "{\"type\":\"Request\",\"testId\":\"t\"," + pairIdJson + "\"method\":\"GET\",\"uri\":\"http://api/orders/2\",\"serviceName\":\"api\",\"callerName\":\"web\",\"timestamp\":\"2026-10-12T10:00:00.003Z\"}",
            "{\"type\":\"Response\",\"testId\":\"t\"," + pairIdJson + "\"method\":\"GET\",\"uri\":\"http://api/orders/2\",\"serviceName\":\"api\",\"callerName\":\"web\",\"statusCode\":\"404\",\"content\":\"second-body\",\"timestamp\":\"2026-10-12T10:00:00.004Z\"}",
        ]);
        var options = Options(name);

        var result = IngestPipeline.Run(new IngestRequest { InteractionFiles = [capture], TestRecords = tests, Options = options });

        Assert.True(result.Generated);
        return options.ReportsFolderPath!;
    }

    private ReportConfigurationOptions Options(string name)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, name);
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        return options;
    }

    private static string[] QueryInteractions(string reports)
    {
        var output = new StringWriter();
        Assert.Equal(0, QueryCommand.Run(["interactions", reports], output, new StringWriter()));
        return output.ToString().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
    }

    /// <summary>For each scenario of the data file, the type and phase of each of its records with this URI.</summary>
    private static (string Type, string? Phase)[][] HalvesByScenario(string reports, string uri)
    {
        using var json = JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(reports, "TestRunReport.json")));
        return json.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Select(s => ReportFile.Items(s, "httpInteractions")
                .Where(c => c.TryGetProperty("uri", out var u) && u.GetString() == uri)
                .Select(c => (c.GetProperty("type").GetString()!, c.TryGetProperty("phase", out var p) ? p.GetString() : null))
                .ToArray())
            .ToArray();
    }
}
