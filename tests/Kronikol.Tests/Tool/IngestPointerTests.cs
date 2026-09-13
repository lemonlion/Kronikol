using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// `kronikol ingest` writes a report and then says so, and the line it says it with is the <b>only</b>
/// pointer an ingest user ever sees — the library's own run-end pointer is off for this path. So the two
/// things the pointer exists to carry have to be in it.
///
/// <para>They were not. The first line — the one naming the files <b>with their sizes</b> — was dropped
/// with a <c>Skip(1)</c>, on the grounds that the two lines printed above it already named the directory.
/// They named the directory; they did not carry the sizes, and the size is the whole warning. A reader who
/// is never told <c>TestRunReport.json 48.2 MB</c> has no reason not to open it, which is the single
/// behaviour every other part of this design exists to prevent.</para>
///
/// <para>And no <c>::notice</c> was emitted, on any runner. `ingest` runs as a CI step and owns its
/// stdout, so it is the one channel where a workflow annotation is guaranteed to survive — the library
/// pointer under VSTest is not.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestPointerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-ingest-pointer-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    public IngestPointerTests()
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

    private string Ingest(string status)
    {
        const string testId = "b17c651916cd43dd8448eb211c80319c";
        var captures = Path.Combine(_dir, "captures");
        Directory.CreateDirectory(captures);

        var (req, resp) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/charge", "payments", "web",
            requestContent: "{}", responseContent: "{}", statusCode: "500",
            requestTimestamp: T0, responseTimestamp: T0.AddMilliseconds(30));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [req.ToJson(), resp.ToJson()]);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "checkout › pays", Feature = "checkout.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = status, DurationMs = 100, Timestamp = T0.AddSeconds(1) }.ToJson(),
        ]);

        var output = Path.Combine(_dir, "out-" + status);
        var @out = new StringWriter();
        var err = new StringWriter();
        var exit = IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output], @out, err);
        Assert.Equal(0, exit);
        return @out.ToString();
    }

    [Fact]
    public void The_pointer_names_the_files_and_their_sizes()
    {
        var console = Ingest("failed");

        var pointer = console.Split('\n').FirstOrDefault(l => l.StartsWith("Kronikol: reports written to", StringComparison.Ordinal));
        Assert.NotNull(pointer);

        // The files, and the size that is the reason not to open one of them.
        Assert.Contains("TestRunReport.html", pointer!, StringComparison.Ordinal);
        Assert.Contains("TestRunReport.json", pointer!, StringComparison.Ordinal);
        Assert.Matches(@"TestRunReport\.json \d", pointer!);
    }

    [Fact]
    public void A_failing_ingest_still_says_what_failed_and_how_to_ask()
    {
        var console = Ingest("failed");

        Assert.Contains("1 failed", console, StringComparison.Ordinal);
        Assert.Contains("kronikol query failures", console, StringComparison.Ordinal);
    }

    [Fact]
    public void The_directory_is_not_announced_three_times()
    {
        // The Skip(1) existed because the two lines above the pointer already named the directory. The
        // answer is to stop printing those, not to drop the line that carries the sizes.
        var console = Ingest("passed");

        var mentions = console.Split('\n').Count(l => l.Contains("out-passed", StringComparison.Ordinal) && !l.Contains("kronikol query", StringComparison.Ordinal));
        Assert.True(mentions <= 1, $"the reports directory is announced {mentions} times:\n{console}");
    }
}
