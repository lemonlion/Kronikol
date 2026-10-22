using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The ingest command's <c>--setup-window</c>, <c>--setup-name</c> and <c>--other-run</c> (plans/RUN_WINDOW_PLAN.md R2,
/// #187): what each maps to, the line each prints after <c>Replayed</c>, and each refusal.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestSetupWindowCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-setup-window-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    public IngestSetupWindowCommandTests()
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
    /// A capture holding the stack's start-up (09:47), the run's own test call (10:00:02), a device run's call (10:00:15)
    /// and a call after the device run (10:00:40), with a tests file whose markers bound the run from 10:00:00 to 10:00:11.
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
        Add("session", "http://orders/device", T0.AddSeconds(15));
        Add("session", "http://orders/later", T0.AddSeconds(40));
        File.WriteAllLines(Path.Combine(captures, "capture.ndjson"), lines);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "testrun", TestId = "__run__", Status = "started", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "start", TestId = "b1", TestName = "Orders › lists orders", Timestamp = T0.AddSeconds(1) }.ToJson(),
            new TestRunRecord { Event = "end", TestId = "b1", Status = "passed", Timestamp = T0.AddSeconds(5) }.ToJson(),
            new TestRunRecord { Event = "testrun", TestId = "__run__", Status = "passed", Timestamp = T0.AddSeconds(11) }.ToJson(),
        ]);
        return captures;
    }

    private (int Exit, string Out, string Error, string Output) Ingest(params string[] flags)
    {
        var captures = Captures();
        var output = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N")[..6]);
        var @out = new StringWriter();
        var err = new StringWriter();
        var exit = IngestCommand.Run(
            [captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", output, "--fold-unknown", "Traffic outside any test", .. flags],
            @out, err);
        return (exit, @out.ToString(), err.ToString(), output);
    }

    /// <summary>The URIs of the requests the report's data file holds, by scenario id.</summary>
    private static Dictionary<string, string[]> Requests(string output)
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(output, "TestRunReport.json")));
        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => ReportFile.Items(s, "httpInteractions")
                .Where(i => i.GetProperty("type").GetString() == "Request").Select(i => i.GetProperty("uri").GetString()!).ToArray());
    }

    /// <summary>The lines from <c>Replayed</c> on, up to the first line that is neither of the windows' nor Replayed itself.</summary>
    private static string[] LinesAfterReplayed(string @out)
    {
        var lines = @out.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var replayed = Array.FindIndex(lines, l => l.StartsWith("Replayed ", StringComparison.Ordinal));
        Assert.True(replayed >= 0, @out);
        return lines.Skip(replayed + 1).TakeWhile(l => l.StartsWith("--setup-window:", StringComparison.Ordinal) || l.StartsWith("--other-run:", StringComparison.Ordinal)).ToArray();
    }

    [Fact]
    public void The_setup_window_and_another_run_keep_and_leave_and_each_says_how_many_right_after_Replayed()
    {
        var (exit, @out, error, output) = Ingest("--setup-window", "2026-10-21T09:40:00Z/2026-10-21T09:50:00Z",
            "--other-run", "device=2026-10-21T10:00:11.6Z/2026-10-21T10:00:21Z");

        Assert.True(exit == 0, error);
        Assert.Equal(
        [
            "--setup-window: 2 interaction record(s) outside the run window kept in \"Stack start-up\".",
            "--other-run: 2 interaction record(s) outside the run window left to other runs: device 2.",
        ], LinesAfterReplayed(@out));
        var requests = Requests(output);
        Assert.Equal(["http://orders/seed"], requests["stack-start-up"]);
        Assert.Equal(["http://orders/mine"], requests["b1"]);
        Assert.DoesNotContain("http://orders/device", requests.Values.SelectMany(v => v));
        Assert.Contains("2 interaction record(s) outside the run window dropped that no other window explains: 0 before the run began, 2 after it ended.", @out);
    }

    [Fact]
    public void The_setup_name_names_the_scenario()
    {
        var (exit, @out, error, output) = Ingest("--setup-window", "2026-10-21T09:40:00Z/2026-10-21T09:50:00Z", "--setup-name", "Compose up");

        Assert.True(exit == 0, error);
        Assert.Equal(["--setup-window: 2 interaction record(s) outside the run window kept in \"Compose up\"."], LinesAfterReplayed(@out));
        Assert.Equal(["http://orders/seed"], Requests(output)["stack-start-up"]);
        using var document = System.Text.Json.JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(output, "TestRunReport.json")));
        Assert.Equal("Compose up", document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .Single(s => s.GetProperty("id").GetString() == "stack-start-up").GetProperty("name").GetString());
    }

    [Fact]
    public void Another_run_with_no_end_holds_everything_after_its_start_and_a_repeated_flag_declares_another()
    {
        var (exit, @out, error, _) = Ingest("--other-run", "device=2026-10-21T10:00:11.6Z/2026-10-21T10:00:21Z", "--other-run", "nightly=2026-10-21T10:00:30Z/",
            "--other-run", "idle=2026-10-21T11:00:00Z/");

        Assert.True(exit == 0, error);
        Assert.Equal(["--other-run: 4 interaction record(s) outside the run window left to other runs: device 2, nightly 2, idle 0."], LinesAfterReplayed(@out));
        // The start-up, before the run and in no declared window, is dropped as before.
        Assert.Contains("2 interaction record(s) outside the run window dropped that no other window explains: 2 before the run began, 0 after it ended.", @out);
    }

    [Fact]
    public void Each_flag_implies_the_run_window_and_without_them_neither_line_is_printed()
    {
        var setup = Ingest("--setup-window", "2026-10-21T09:40:00Z/2026-10-21T09:50:00Z");
        var plain = Ingest();

        Assert.True(setup.Exit == 0, setup.Error);
        Assert.DoesNotContain("http://orders/device", Requests(setup.Output).Values.SelectMany(v => v));
        Assert.True(plain.Exit == 0, plain.Error);
        Assert.Empty(LinesAfterReplayed(plain.Out));
        Assert.DoesNotContain("--setup-window:", plain.Out);
        Assert.DoesNotContain("--other-run:", plain.Out);
    }

    [Theory]
    [InlineData("--setup-window", "2026-10-21T09:50:00Z/2026-10-21T09:40:00Z", "--setup-window ends before it starts (2026-10-21T09:50:00.0000000+00:00 → 2026-10-21T09:40:00.0000000+00:00)")]
    [InlineData("--setup-window", "2026-10-21T09:40:00Z", "--setup-window expects <start>/<end>, two ISO-8601 instants, got '2026-10-21T09:40:00Z'")]
    [InlineData("--setup-window", "nonsense/2026-10-21T09:40:00Z", "--setup-window expects <start>/<end>, two ISO-8601 instants, got 'nonsense/2026-10-21T09:40:00Z'")]
    [InlineData("--setup-window", "2026-10-21T09:40:00Z/", "--setup-window expects <start>/<end>, two ISO-8601 instants, got '2026-10-21T09:40:00Z/'")]
    [InlineData("--other-run", "device", "--other-run expects <name>=<start>/[<end>], got 'device'")]
    [InlineData("--other-run", "device=2026-10-21T10:00:11.6Z", "--other-run expects <name>=<start>/[<end>], got 'device=2026-10-21T10:00:11.6Z'")]
    [InlineData("--other-run", "device=bad/", "--other-run expects <name>=<start>/[<end>], got 'device=bad/'")]
    [InlineData("--other-run", " =2026-10-21T10:00:11.6Z/", "--other-run needs a name before '=', got ' =2026-10-21T10:00:11.6Z/'")]
    [InlineData("--other-run", "device=2026-10-21T10:00:21Z/2026-10-21T10:00:11.6Z", "--other-run device ends before it starts (2026-10-21T10:00:21.0000000+00:00 → 2026-10-21T10:00:11.6000000+00:00)")]
    public void A_window_that_is_no_window_is_refused(string flag, string value, string message)
    {
        var (exit, _, error, _) = Ingest(flag, value);

        Assert.Equal(2, exit);
        Assert.Equal(message, error.Trim());
    }

    [Fact]
    public void A_setup_name_needs_a_setup_window_and_a_name()
    {
        var alone = Ingest("--setup-name", "Compose up");
        var blank = Ingest("--setup-window", "2026-10-21T09:40:00Z/2026-10-21T09:50:00Z", "--setup-name", " ");
        var missing = Ingest("--setup-window");

        Assert.Equal((2, "--setup-name needs --setup-window"), (alone.Exit, alone.Error.Trim()));
        Assert.Equal((2, "--setup-name must not be blank"), (blank.Exit, blank.Error.Trim()));
        Assert.Equal((2, "Missing value for --setup-window"), (missing.Exit, missing.Error.Trim()));
    }

    [Fact]
    public void The_help_lists_the_three_flags()
    {
        var @out = new StringWriter();
        IngestCommand.Run(["--help"], @out, new StringWriter());

        Assert.Contains("  --setup-window <start>/<end>", @out.ToString());
        Assert.Contains("  --setup-name <name>", @out.ToString());
        Assert.Contains("  --other-run <name>=<start>/[<end>]", @out.ToString());
    }
}
