using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #192 R2 (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md Q7): <c>kronikol ingest --shape-templates FILE</c> gives an ingested run
/// the HistoryShapeTemplates a .NET run sets in its options, read from a file in the shape a report's
/// <c>shapeTemplates</c> block has, since a regular expression on a command line is a quoting hazard in every shell.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestShapeTemplatesTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private const string TestId = "orders-a-cached-order";

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-ingest-templates").FullName;

    public IngestShapeTemplatesTests() => RequestResponseLogger.Redaction = null;

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Captures()
    {
        var captures = Path.Combine(_dir, "captures-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(captures);
        var (first, firstAnswer) = InteractionRecord.Pair(TestId, null, "GET", "http://cache/cache/abc-1", "Cache", "Caller",
            statusCode: "200", requestTimestamp: T0.AddMilliseconds(1), responseTimestamp: T0.AddMilliseconds(5));
        var (second, secondAnswer) = InteractionRecord.Pair(TestId, null, "GET", "http://cache/cache/def-2", "Cache", "Caller",
            statusCode: "200", requestTimestamp: T0.AddMilliseconds(10), responseTimestamp: T0.AddMilliseconds(15));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"), [first.ToJson(), firstAnswer.ToJson(), second.ToJson(), secondAnswer.ToJson()]);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = TestId, TestName = "orders › a cached order", Feature = "orders.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = TestId, Status = "passed", DurationMs = 20, Timestamp = T0.AddMilliseconds(20) }.ToJson(),
        ]);
        return captures;
    }

    private (int Exit, string Out, string Err) Ingest(string captures, params string[] more)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), .. more], output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private string RulesFile(string json)
    {
        var path = Path.Combine(_dir, "rules-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void The_rules_it_is_given_reach_the_report_and_its_routes()
    {
        var rules = RulesFile("""[ { "pattern": "(?<=/cache/)[a-z]+-\\d+", "placeholder": "{key}" } ]""");
        var reports = Path.Combine(_dir, "out");

        var (exit, _, error) = Ingest(Captures(), "-o", reports, "--shape-templates", rules);

        Assert.True(exit == 0, error);
        var data = ReportFile.Resolve(Path.Combine(reports, "TestRunReport.json"));
        using var report = JsonDocument.Parse(ReportFile.Read(data));
        var block = Assert.Single(report.RootElement.GetProperty("shapeTemplates").EnumerateArray());
        Assert.Equal(@"(?<=/cache/)[a-z]+-\d+", block.GetProperty("pattern").GetString());
        var (output, queryError) = (new StringWriter(), new StringWriter());
        Assert.Equal(0, Kronikol.Query.QueryCommand.Run(["interactions", data, "--group-by", "route"], output, queryError));
        Assert.Matches(@"(?m)^/cache/\{key\}\s+2\s", output.ToString());
    }

    [Fact]
    public void A_rules_file_that_is_not_there_is_refused()
    {
        var (exit, _, error) = Ingest(Captures(), "-o", Path.Combine(_dir, "out-nofile"), "--shape-templates", Path.Combine(_dir, "nowhere.json"));

        Assert.Equal(1, exit);
        Assert.Contains("Shape templates file not found", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"pattern\": \"x\" }")]
    [InlineData("[ { \"placeholder\": \"{x}\" } ]")]
    [InlineData("[ \"x\" ]")]
    [InlineData("[ { \"pattern\": \"x\", \"placeholder\": 3 } ]")]
    [InlineData("not json")]
    public void A_rules_file_that_is_not_a_list_of_rules_is_refused_with_what_it_should_be(string json)
    {
        var (exit, _, error) = Ingest(Captures(), "-o", Path.Combine(_dir, "out-bad"), "--shape-templates", RulesFile(json));

        Assert.Equal(2, exit);
        Assert.Contains("[{ \"pattern\": ..., \"placeholder\": ... }]", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_flag_refuses_a_missing_value()
    {
        var (exit, _, error) = Ingest(Captures(), "-o", Path.Combine(_dir, "out-missing"), "--shape-templates");

        Assert.Equal(2, exit);
        Assert.Contains("Missing value for --shape-templates", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_help_names_the_flag()
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        IngestCommand.Run(["--help"], output, error);

        Assert.Contains("--shape-templates <file>", output.ToString() + error, StringComparison.Ordinal);
    }
}
