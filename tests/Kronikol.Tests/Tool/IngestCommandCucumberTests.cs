using Kronikol.Ingestion;
using Kronikol.Tests.Ingestion.Cucumber;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>The <c>--cucumber-messages</c> / <c>--include-hooks</c> flags of <c>kronikol ingest</c>.</summary>
[Collection("DiagramsFetcher")]
public class IngestCommandCucumberTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-cucumber-" + Guid.NewGuid().ToString("N"));

    public IngestCommandCucumberTests()
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

    private static string Usage()
    {
        var writer = new StringWriter();
        IngestCommand.PrintUsage(writer);
        return writer.ToString();
    }

    [Fact]
    public void The_new_flags_are_documented_in_the_usage()
    {
        Assert.Contains("--cucumber-messages", Usage());
        Assert.Contains("--include-hooks", Usage());
    }

    [Fact]
    public void A_messages_file_alone_is_enough_to_produce_a_report()
    {
        var output = Path.Combine(_dir, "out");
        var @out = new StringWriter();
        var err = new StringWriter();

        var exit = IngestCommand.Run(
            ["--cucumber-messages", CucumberFixtures.MessagesPath, "-o", output, "-t", "BDD run"], @out, err);

        Assert.Equal(0, exit);
        Assert.Contains("Cucumber messages:", @out.ToString());
        Assert.Contains("into 6 scenario(s)", @out.ToString());
        var html = File.ReadAllText(Path.Combine(output, "TestRunReport.html"));
        Assert.Contains("BDD run", html);
        Assert.Contains(CucumberFixtures.DemoFeature, html);
        Assert.Contains(CucumberFixtures.Rule, html);
    }

    [Fact]
    public void The_flag_is_repeatable()
    {
        var second = CucumberFixtures.WriteSubset(Path.Combine(_dir, "retry-only.ndjson"), CucumberFixtures.FlakyScenario);
        var first = CucumberFixtures.WriteSubset(Path.Combine(_dir, "simple-only.ndjson"), CucumberFixtures.SimpleScenario);
        var output = Path.Combine(_dir, "repeat");
        var @out = new StringWriter();

        var exit = IngestCommand.Run(
            ["--cucumber-messages", first, "--cucumber-messages", second, "-o", output], @out, new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("into 2 scenario(s)", @out.ToString());
    }

    [Fact]
    public void A_messages_file_inside_an_input_directory_is_not_replayed_as_a_capture()
    {
        var captures = Path.Combine(_dir, "captures");
        Directory.CreateDirectory(captures);
        var messages = CucumberFixtures.WriteSubset(Path.Combine(captures, "messages.ndjson"), CucumberFixtures.SimpleScenario);
        var (request, response) = InteractionRecord.Pair("00000000000000000000000000000001", null, "GET", "http://a/x", "A", "Test",
            statusCode: "200", requestTimestamp: DateTimeOffset.UnixEpoch, responseTimestamp: DateTimeOffset.UnixEpoch.AddSeconds(1));
        File.WriteAllLines(Path.Combine(captures, "c.ndjson"), [request.ToJson(), response.ToJson()]);
        var output = Path.Combine(_dir, "mixed");
        var @out = new StringWriter();
        var err = new StringWriter();

        var exit = IngestCommand.Run([captures, "--cucumber-messages", messages, "-o", output], @out, err);

        Assert.Equal(0, exit);
        Assert.Contains("Ingesting 1 capture file(s)", @out.ToString());
        Assert.DoesNotContain("messages.ndjson", @out.ToString().Split('\n').First(l => l.Contains("Ingesting")));
        Assert.Equal(0, err.ToString().Length);
    }

    [Fact]
    public void Include_hooks_reaches_the_pipeline()
    {
        var output = Path.Combine(_dir, "hooks");

        var exit = IngestCommand.Run(
            ["--cucumber-messages", CucumberFixtures.MessagesPath, "--include-hooks", "-o", output],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("BeforeEach hook", File.ReadAllText(Path.Combine(output, "TestRunReport.html")));
    }

    [Fact]
    public void A_missing_messages_file_is_a_clear_error()
    {
        var err = new StringWriter();

        var exit = IngestCommand.Run(
            ["--cucumber-messages", Path.Combine(_dir, "nope.ndjson"), "-o", Path.Combine(_dir, "x")],
            new StringWriter(), err);

        Assert.Equal(1, exit);
        Assert.Contains("Cucumber messages file not found", err.ToString());
    }

    [Fact]
    public void A_missing_value_for_the_flag_is_a_usage_error()
    {
        var err = new StringWriter();

        Assert.Equal(2, IngestCommand.Run(["--cucumber-messages"], new StringWriter(), err));
        Assert.Contains("Missing value for --cucumber-messages", err.ToString());
    }
}
