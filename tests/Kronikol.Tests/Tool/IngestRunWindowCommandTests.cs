using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The ingest command's run-window flags (plans/RUN_WINDOW_PLAN.md F13): through 5.2.5 no fact covered
/// <c>--run-window</c>, <c>--run-start</c> or <c>--run-end</c>.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestRunWindowCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-run-window-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    public IngestRunWindowCommandTests()
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

    /// <summary>
    /// A capture holding the stack's start-up (09:47), the run's own test call (10:00:02) and the next run's call
    /// (10:00:30), with a tests file whose markers bound the run from 10:00:00 to 10:00:10. Returns the capture folder.
    /// </summary>
    private string Captures()
    {
        var captures = Path.Combine(_dir, "captures-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(captures);
        var lines = new List<string>();
        void Add(string testId, string uri, DateTimeOffset at)
        {
            var (request, response) = InteractionRecord.Pair(testId, null, "GET", uri, "orders", "browser", statusCode: "200",
                requestTimestamp: at, responseTimestamp: at.AddMilliseconds(100));
            lines.Add(request.ToJson());
            lines.Add(response.ToJson());
        }
        Add("session", "http://orders/seed", T0.AddMinutes(-13));
        Add("b1", "http://orders/mine", T0.AddSeconds(2));
        Add("session", "http://orders/next-run", T0.AddSeconds(30));
        File.WriteAllLines(Path.Combine(captures, "capture.ndjson"), lines);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "testrun", TestId = "__run__", Status = "started", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "start", TestId = "b1", TestName = "Orders › lists orders", Timestamp = T0.AddSeconds(1) }.ToJson(),
            new TestRunRecord { Event = "end", TestId = "b1", Status = "passed", Timestamp = T0.AddSeconds(5) }.ToJson(),
            new TestRunRecord { Event = "testrun", TestId = "__run__", Status = "passed", Timestamp = T0.AddSeconds(10) }.ToJson(),
        ]);
        return captures;
    }

    private (int Exit, string Out, string Error, string Output) Ingest(string captures, params string[] flags)
    {
        var output = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N")[..6]);
        var @out = new StringWriter();
        var err = new StringWriter();
        var exit = IngestCommand.Run(
            [captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output, "--fold-unknown", "Traffic outside any test", .. flags],
            @out, err);
        return (exit, @out.ToString(), err.ToString(), output);
    }

    /// <summary>The URIs of the requests the report's data file holds.</summary>
    private static string[] RequestUris(string output)
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(output, "TestRunReport.json")));
        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .SelectMany(s => ReportFile.Items(s, "httpInteractions"))
            .Where(i => i.GetProperty("type").GetString() == "Request")
            .Select(i => i.GetProperty("uri").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void Without_a_window_flag_every_record_is_kept()
    {
        var (exit, @out, error, output) = Ingest(Captures());

        Assert.True(exit == 0, error);
        Assert.Equal(["http://orders/mine", "http://orders/next-run", "http://orders/seed"], RequestUris(output));
        // b1 and the fold, which is no test: the pointer after this line counts one
        Assert.Contains("into 2 scenario(s), 1 of them not a test.", @out);
    }

    [Fact]
    public void Run_window_takes_the_window_from_the_tests_file()
    {
        var (exit, @out, error, output) = Ingest(Captures(), "--run-window");

        Assert.True(exit == 0, error);
        Assert.Equal(["http://orders/mine"], RequestUris(output));
        Assert.Contains("4 interaction record(s) outside the run window", @out);
    }

    [Fact]
    public void Run_start_and_run_end_set_the_window_and_imply_it()
    {
        // From 09:40 the start-up is inside; to 10:00:20 the next run is still outside.
        var (exit, @out, error, output) = Ingest(Captures(), "--run-start", "2026-10-21T09:40:00Z", "--run-end", "2026-10-21T10:00:20Z");

        Assert.True(exit == 0, error);
        Assert.Equal(["http://orders/mine", "http://orders/seed"], RequestUris(output));
        Assert.Contains("2 interaction record(s) outside the run window", @out);
    }

    [Fact]
    public void Run_start_alone_moves_the_start_and_keeps_the_tests_file_end()
    {
        var (exit, _, error, output) = Ingest(Captures(), "--run-start", "2026-10-21T09:40:00Z");

        Assert.True(exit == 0, error);
        Assert.Equal(["http://orders/mine", "http://orders/seed"], RequestUris(output));
    }

    [Fact]
    public void Run_end_alone_moves_the_end_and_keeps_the_tests_file_start()
    {
        var (exit, _, error, output) = Ingest(Captures(), "--run-end", "2026-10-21T10:00:40Z");

        Assert.True(exit == 0, error);
        Assert.Equal(["http://orders/mine", "http://orders/next-run"], RequestUris(output));
    }

    [Fact]
    public void A_run_end_before_the_run_start_is_refused()
    {
        var (exit, _, error, _) = Ingest(Captures(), "--run-start", "2026-10-21T10:00:10Z", "--run-end", "2026-10-21T10:00:00Z");

        Assert.Equal(2, exit);
        Assert.Contains("--run-end (2026-10-21T10:00:00.0000000+00:00) is before --run-start (2026-10-21T10:00:10.0000000+00:00)", error);
    }

    [Fact]
    public void A_run_start_that_is_no_instant_is_refused()
    {
        var (exit, _, error, _) = Ingest(Captures(), "--run-start", "yesterday");

        Assert.Equal(2, exit);
        Assert.Contains("--run-start expects an ISO-8601 instant, got 'yesterday'", error);
    }
}
