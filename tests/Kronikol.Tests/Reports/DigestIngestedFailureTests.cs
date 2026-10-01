using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>Failures.md</c> for an ingested run (<c>plans/INGEST_FIDELITY_PLAN.md</c> T10): the digest printed a failing step
/// with its own source location and failure message, and an ingested step carried neither. Its error went to the step's
/// comments, which the HTML renders and the digest does not read, so the failing step appeared with no reason under it.
/// </summary>
[Collection("DiagramsFetcher")]
public class DigestIngestedFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-digest-ingested-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    public DigestIngestedFailureTests()
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
    public void An_ingested_failing_step_is_printed_with_its_message_and_where_it_is_written()
    {
        const string id = "digest-ingested-step";
        TestRunRecord[] tests =
        [
            new() { Event = "start", TestId = id, TestName = "charges a card", Feature = "charge", SourceFile = "src/charge.test.ts", SourceLine = 12, Timestamp = T0 },
            new() { Event = "step", TestId = id, Text = "a saved card", Keyword = "Given", Status = "passed", SourceFile = "src/charge.test.ts", SourceLine = 14, DurationMs = 900, Timestamp = T0.AddSeconds(1) },
            new() { Event = "step", TestId = id, Text = "the card is charged", Keyword = "When", Status = "failed", Error = "expected 201, received 502", SourceFile = "src/charge.test.ts", SourceLine = 18, DurationMs = 900, Timestamp = T0.AddSeconds(2) },
            new() { Event = "end", TestId = id, Status = "failed", Error = "expected 201, received 502", Timestamp = T0.AddSeconds(3) },
        ];
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = _dir;
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;

        IngestPipeline.Run(new IngestRequest { TestRecords = tests, Options = options, AllowEmpty = true });

        var digest = File.ReadAllText(Path.Combine(_dir, "Failures.md"));
        Assert.Contains("written at `src/charge.test.ts:12`", digest);
        var failing = digest[digest.IndexOf("**Failing step**", StringComparison.Ordinal)..];
        Assert.Contains("When the card is charged (charge.test.ts:18)", failing);
        Assert.Contains("expected 201, received 502", failing.Split("\n\n", 3)[1]);
    }

    /// <summary>
    /// T14's digest half (§11 Q9): the calls table said a call failed (<c>!TypeError</c>) and never why. Each call whose
    /// response carries an error now gets one line under the table, its message chain on one line, capped as
    /// <c>query http</c> caps it. In-process runs get the line too: a failed send has carried <c>Error</c> since 3.18.0.
    /// </summary>
    [Fact]
    public void A_failed_calls_error_is_printed_under_the_calls_table()
    {
        const string id = "digest-call-error";
        var (request, _) = InteractionRecord.Pair(id, null, "POST", "http://localhost:5000/charges", "psp", "web",
            requestTimestamp: T0.AddSeconds(1), responseTimestamp: T0.AddSeconds(1.1), requestResponseId: "charge-1");
        var failed = new InteractionRecord
        {
            Type = "Response", Method = "POST", Uri = "http://localhost:5000/charges", ServiceName = "psp", CallerName = "web",
            RequestResponseId = "charge-1", Timestamp = T0.AddSeconds(1.1), TestId = id,
            StatusCode = "!TypeError", Error = "fetch failed\nCaused by: connect ECONNREFUSED 127.0.0.1:443 " + new string('x', 600),
        };
        var (okRequest, okResponse) = InteractionRecord.Pair(id, null, "GET", "http://localhost:5000/cards/1", "cards", "web",
            statusCode: "200", requestTimestamp: T0.AddSeconds(2), responseTimestamp: T0.AddSeconds(2.1));
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = _dir;
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;

        IngestPipeline.Run(new IngestRequest
        {
            Interactions = [request, failed, okRequest, okResponse],
            TestRecords =
            [
                new TestRunRecord { Event = "start", TestId = id, TestName = "reports a provider outage", Timestamp = T0 },
                new TestRunRecord { Event = "end", TestId = id, Status = "failed", Error = "expected 502, received 500", Timestamp = T0.AddSeconds(3) },
            ],
            Options = options,
        });

        var digest = File.ReadAllText(Path.Combine(_dir, "Failures.md"));
        var line = Assert.Single(digest.Split('\n'), l => l.Contains("ECONNREFUSED", StringComparison.Ordinal));
        Assert.StartsWith("- `s0/i0` !TypeError: ", line);
        Assert.Contains("fetch failed Caused by: connect ECONNREFUSED 127.0.0.1:443", line);
        Assert.True(line.Length < 460, "The error is capped at 400 characters: " + line.Length);
        // The call that answered gets no line.
        Assert.DoesNotContain(digest.Split('\n'), l => l.StartsWith("- `s0/i2`", StringComparison.Ordinal));
    }
}
