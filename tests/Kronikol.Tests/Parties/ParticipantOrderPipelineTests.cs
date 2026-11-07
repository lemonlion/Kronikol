using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using Kronikol.Ingestion;
using Kronikol.InternalFlow;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Parties;

/// <summary>
/// R1 of plans/PARTICIPANT_ORDER_PLAN.md (#228, 7): the parties are resolved before the diagrams are drawn, and a run
/// that declares something draws each test's parties first, then the parties under test, then the rest; a run that
/// declares nothing keeps today's order. Every way the run's diagrams are drawn takes the same roles: the report, a
/// fetch after it, ingest and the CI summary.
/// </summary>
[Collection("DiagramsFetcher")]
public partial class ParticipantOrderPipelineTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-participant-order").FullName;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    // Tagged, since the process's call log and its test clients hold every test's.
    private string Caller => "Caller" + _tag;
    private string Disk => "Disk" + _tag;
    private string Git => "Git" + _tag;
    private string App => "App" + _tag;
    private string TestId => "participant-order-" + _tag;

    private static RequestResponseLog Boundary(string testId) =>
        new(TestName: testId, TestId: testId, Method: "", Content: "", Uri: new Uri("http://override.com"), Headers: [],
            ServiceName: "", CallerName: "", Type: RequestResponseType.Request, TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(), TrackingIgnore: false)
        {
            IsActionStart = true
        };

    /// <summary>The issue's scenario (2.1): the test sets up the disk and git, then runs the app, which uses both.</summary>
    private void LogCalls(string storeCategory = "Database", bool boundary = false)
    {
        RequestResponseLogger.LogPair("Run", TestId, "POST", new Uri("http://disk/work"), Disk, Caller, dependencyCategory: storeCategory);
        RequestResponseLogger.LogPair("Run", TestId, "POST", new Uri("http://git/repo"), Git, Caller, dependencyCategory: storeCategory);
        if (boundary)
            RequestResponseLogger.Log(Boundary(TestId));
        RequestResponseLogger.LogPair("Run", TestId, HttpMethod.Post, new Uri("http://app/run"), App, Caller, statusCode: HttpStatusCode.OK);
        RequestResponseLogger.LogPair("Run", TestId, "GET", new Uri("http://git/repo"), Git, App, dependencyCategory: storeCategory);
        RequestResponseLogger.LogPair("Run", TestId, "PUT", new Uri("http://disk/work/out"), Disk, App, dependencyCategory: storeCategory);
    }

    private string Run(string folder, Action<ReportConfigurationOptions>? configure = null, bool reset = true,
        ExecutionResult result = ExecutionResult.Passed)
    {
        // Each report draws its own diagrams since R1; the reset keeps this fact from depending on that where it can.
        if (reset)
            DefaultDiagramsFetcher.Reset();
        var reports = Path.Combine(_dir, folder);
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = reports,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
            CompressTestRunReport = false
        };
        configure?.Invoke(options);
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [new Feature { DisplayName = "Order " + _tag, Scenarios = [new Scenario { Id = TestId, DisplayName = "Runs the app", Result = result }] }],
            Start, Start.AddMinutes(1), options);
        return reports;
    }

    private PartyDescriptor[] AppUnderTest => [new PartyDescriptor(App) { Kind = PartyKind.UnderTest }];

    /// <summary>The scenario's sequence diagram source in the run report, read from the page as a browser gets it.</summary>
    private string SequenceSource(string reports)
    {
        var page = BrowsingContext.New(Configuration.Default)
            .OpenAsync(req => req.Content(ReportHtml.Read(Path.Combine(reports, "TestRunReport.html")))).GetAwaiter().GetResult();
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(page.GetElementById("puml-data")!.TextContent)!;
        return map.Values.Select(InternalFlowHtmlGenerator.DecompressFromBase64)
            .Single(s => s.Contains("autonumber", StringComparison.Ordinal) && s.Contains($"\"{App}\"", StringComparison.Ordinal));
    }

    [GeneratedRegex("""^\w+ "(?<name>[^"]*)" as \w+""", RegexOptions.Multiline)]
    private static partial Regex Declaration();

    /// <summary>The run's four parties in the order <paramref name="source"/> declares them.</summary>
    private string[] Order(string source) =>
        Declaration().Matches(source).Select(m => m.Groups["name"].Value)
            .Where(n => n == Caller || n == Disk || n == Git || n == App).ToArray();

    private static JsonElement Party(string reports, string name)
    {
        using var report = JsonDocument.Parse(ReportFile.Read(Path.Combine(reports, "TestRunReport.json")));
        return report.RootElement.GetProperty("parties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name).Clone();
    }

    private static (string?, string?) KindOf(JsonElement party) =>
        (party.GetProperty("kind").GetString(), party.GetProperty("kindSource").GetString());

    [Fact]
    public void P1_a_party_declared_under_test_is_drawn_right_after_the_test()
    {
        LogCalls();

        var reports = Run("declared", o => o.Parties = AppUnderTest);

        Assert.Equal([Caller, App, Disk, Git], Order(SequenceSource(reports)));
        Assert.Equal(("underTest", "declared"), KindOf(Party(reports, App)));
    }

    [Fact]
    public void P2_a_service_id_alone_lays_out_a_party_a_test_client_observed()
    {
        TestClientRegistry.Register(Caller, null).Saw(App);
        LogCalls();

        var reports = Run("observed", o => o.ServiceId = "app-" + _tag);

        Assert.Equal([Caller, App, Disk, Git], Order(SequenceSource(reports)));
        Assert.Equal(("underTest", "observed"), KindOf(Party(reports, App)));
    }

    [Theory]
    [InlineData("Database")]
    [InlineData("Disk")]
    public void P2b_a_service_id_alone_lays_out_the_inferred_subject_whatever_the_stores_are_called(string storeCategory)
    {
        // A store the palette knows (Database) is never a subject; one it does not (Disk) is not left out as a store,
        // and the app is still the subject, as the one that makes calls of its own.
        LogCalls(storeCategory);

        var reports = Run("inferred", o => o.ServiceId = "app-" + _tag);

        Assert.Equal([Caller, App, Disk, Git], Order(SequenceSource(reports)));
        Assert.Equal(("underTest", "inferred"), KindOf(Party(reports, App)));
    }

    [Fact]
    public void P3_a_run_that_declares_nothing_keeps_todays_order()
    {
        LogCalls();

        var reports = Run("undeclared");

        Assert.Equal([Caller, Disk, Git, App], Order(SequenceSource(reports)));
        Assert.False(File.ReadAllText(Path.Combine(reports, "TestRunReport.json")).Contains("\"parties\"", StringComparison.Ordinal));
    }

    [Fact]
    public void P4_the_report_draws_its_own_diagrams_after_an_earlier_fetch_and_a_later_fetch_takes_them()
    {
        LogCalls(boundary: true);
        DefaultDiagramsFetcher.Reset();
        _ = DefaultDiagramsFetcher.GetDiagramsFetcher(new DiagramsFetcherOptions { SeparateSetup = false })();

        var reports = Run("after-a-fetch", o =>
        {
            o.Parties = AppUnderTest;
            o.SeparateSetup = true;
        }, reset: false);

        // The report's own options first (F4: a set an earlier fetch cached was drawn with that fetch's), then its roles.
        var source = SequenceSource(reports);
        Assert.Matches(SetupPartition(), source);
        Assert.Equal([Caller, App, Disk, Git], Order(source));

        // BDDfy's batch processor fetches with no options at process exit, after the report: it takes the report's set.
        var later = DefaultDiagramsFetcher.GetDiagramsFetcher()().Single(d => d.TestRuntimeId == TestId);
        Assert.Matches(SetupPartition(), later.CodeBehind);
        Assert.Equal([Caller, App, Disk, Git], Order(later.CodeBehind));
    }

    // The Setup partition's own line: every sequence diagram's style block names "partition" too.
    [GeneratedRegex(@"^partition\b.*\bSetup\s*$", RegexOptions.Multiline)]
    private static partial Regex SetupPartition();

    [Fact]
    public void P7_a_hosts_own_fetch_keeps_todays_order()
    {
        // The roles are the report's to give (DiagramsFetcherOptions.Roles is internal): what a host draws itself through
        // the public fetcher, with no report, keeps the order a run that declares nothing has.
        LogCalls();
        DefaultDiagramsFetcher.Reset();

        var own = DefaultDiagramsFetcher.GetDiagramsFetcher(new DiagramsFetcherOptions())().Single(d => d.TestRuntimeId == TestId);

        Assert.Equal([Caller, Disk, Git, App], Order(own.CodeBehind));
    }

    [Fact]
    public void P5_ingest_with_a_parties_file_lays_out_the_party_under_test()
    {
        var t0 = new DateTimeOffset(Start);
        var captures = Path.Combine(_dir, "captures");
        Directory.CreateDirectory(captures);
        (string Service, string Caller, string Url)[] calls =
        [
            (Disk, Caller, "http://disk/work"), (Git, Caller, "http://git/repo"), (App, Caller, "http://app/run"),
            (Git, App, "http://git/repo"), (Disk, App, "http://disk/work/out")
        ];
        var records = calls.SelectMany((c, i) =>
        {
            var (request, response) = InteractionRecord.Pair(TestId, null, "POST", c.Url, c.Service, c.Caller, statusCode: "200",
                requestTimestamp: t0.AddMilliseconds(10 * i + 1), responseTimestamp: t0.AddMilliseconds(10 * i + 5));
            return new[] { request.ToJson(), response.ToJson() };
        });
        File.WriteAllLines(Path.Combine(captures, "calls.ndjson"), records);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = TestId, TestName = "runs the app", Feature = "app.spec.ts", Timestamp = t0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = TestId, Status = "passed", DurationMs = 60, Timestamp = t0.AddMilliseconds(60) }.ToJson(),
        ]);
        var parties = Path.Combine(_dir, "parties.json");
        File.WriteAllText(parties, $$"""{ "partiesFormatVersion": 1, "parties": [ { "name": "{{App}}", "kind": "underTest" } ] }""");
        var reports = Path.Combine(_dir, "ingested");

        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), "--parties", parties, "-o", reports], output, error);

        Assert.True(exit == 0, error.ToString());
        Assert.Equal([Caller, App, Disk, Git], Order(SequenceSource(reports)));
    }

    [Fact]
    public void P6_the_ci_summarys_diagram_takes_the_same_order_as_the_reports()
    {
        LogCalls();

        var reports = Run("ci-summary", o =>
        {
            o.Parties = AppUnderTest;
            o.WriteCiSummary = true;
        }, result: ExecutionResult.Failed);

        var summary = File.ReadAllText(Path.Combine(reports, ReportGenerator.CiSummaryFileName));
        var fenced = summary.Split("```plantuml").Skip(1).Select(block => block.Split("```")[0]).ToList();
        Assert.NotEmpty(fenced);
        Assert.All(fenced, block => Assert.Equal([Caller, App, Disk, Git], Order(block)));
        Assert.Equal(Order(SequenceSource(reports)), Order(fenced[0]));
    }
}
