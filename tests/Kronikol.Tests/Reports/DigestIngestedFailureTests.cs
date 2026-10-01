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
}
