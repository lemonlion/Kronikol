using Kronikol.Extensions.Otlp;
using Kronikol.Extensions.Spanner;
using Kronikol.PlantUml;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Spanner;

/// <summary>
/// What each reader of a record's kind says of a Spanner call, through the real tracker (#220,
/// plans/CALLS_AND_EVENTS_PLAN.md 6.3, 6.4). A record built by hand at <c>Default</c> would pass every fact here and prove
/// nothing, so each one reads what <see cref="SpannerTracker"/> wrote.
/// </summary>
public sealed class SpannerCallsAreCallsTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly string _test = Guid.NewGuid().ToString();
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-spanner-calls").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private SpannerTracker Tracker(string caller = "TestCaller") => new(new SpannerTrackingOptions
    {
        ServiceName = "Spanner",
        CallerName = caller,
        Verbosity = SpannerTrackingVerbosity.Detailed,
        CurrentTestInfoFetcher = () => ("A Spanner query", _test)
    });

    private RequestResponseLog[] Logs() =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _test).ToArray();

    private void Query(SpannerTracker tracker)
    {
        var operation = new SpannerOperationInfo(SpannerOperation.Query, "Users");
        var (id, trace) = tracker.LogRequest(operation, "SELECT * FROM Users");
        tracker.LogResponse(operation, id, trace, "2 rows returned");
    }

    private Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Users",
            Scenarios = [new Scenario { Id = _test, DisplayName = "A Spanner query", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(10) }]
        }
    ];

    /// <summary>Five queries through the tracker, the first of them slow, each record as the tracker wrote it but for its time.</summary>
    private RequestResponseLog[] ASlowFirstQuery(string caller)
    {
        var tracker = Tracker(caller);
        for (var i = 0; i < 5; i++)
            Query(tracker);
        var timed = Logs().Select((log, position) =>
        {
            var call = position / 2;
            var start = call == 0 ? 0 : 600 + call * 100;
            var end = call == 0 ? 600 : start + 5;
            return log with { Timestamp = T0.AddMilliseconds(log.Type == RequestResponseType.Request ? start : end) };
        }).ToArray();
        Assert.Equal(10, timed.Length);
        return timed;
    }

    [Fact]
    public void Both_halves_of_a_query_are_calls()
    {
        Query(Tracker());

        Assert.All(Logs(), log => Assert.Equal(RequestResponseMetaType.Default, log.MetaType));
        Assert.Equal(2, Logs().Length);
    }

    [Fact]
    public void A_database_call_is_drawn_on_the_plain_note()
    {
        Query(Tracker());

        var plantUml = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(Logs()).Single().PlantUmls.First().PlainText;

        Assert.Contains("SELECT FROM Users", plantUml, StringComparison.Ordinal);
        Assert.Contains("SELECT * FROM Users", plantUml, StringComparison.Ordinal);
        Assert.DoesNotContain("<<eventNote>>", plantUml, StringComparison.Ordinal);
        Assert.DoesNotContain(".eventNote", plantUml, StringComparison.Ordinal);
    }

    /// <summary>The rule judges the calls a test makes, and a Spanner call was read as a message and left out of it.</summary>
    [Fact]
    public void A_database_query_the_test_makes_itself_is_judged()
    {
        var logs = ASlowFirstQuery("TestCaller");
        var first = logs[0].RequestResponseId;

        var mark = Assert.Single(WarmUpCalls.Find(Features(), logs).Calls);

        Assert.Equal(first, mark.Key);
        Assert.Equal(WarmUpMark.FirstKind, mark.Value.Kind);
    }

    /// <summary>The rule's other condition: a call by a party the test called is that party's, whatever its kind.</summary>
    [Fact]
    public void A_database_query_the_service_makes_is_not()
    {
        var logs = ASlowFirstQuery("Orders Api");
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://orders/orders");
        RequestResponseLog[] testsCall =
        [
            new("A Spanner query", _test, HttpMethod.Post, null, uri, [], "Orders Api", "Test", RequestResponseType.Request, trace, pair, false)
                { Timestamp = T0.AddMilliseconds(-1) },
            new("A Spanner query", _test, HttpMethod.Post, null, uri, [], "Orders Api", "Test", RequestResponseType.Response, trace, pair, false, System.Net.HttpStatusCode.OK)
                { Timestamp = T0.AddMilliseconds(1000) }
        ];

        Assert.Empty(WarmUpCalls.Find(Features(), [.. testsCall, .. logs]).Calls);
    }

    [Fact]
    public void A_spanner_call_is_a_client_span()
    {
        Query(Tracker());
        var logs = Logs();

        var span = OtlpSpanMapper.Map(logs[0], logs[1], new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") }, T0);

        Assert.Equal(OtlpSpanKind.Client, span.Kind);
        Assert.Equal("gcp.spanner", span.Attributes.Single(a => a.Key == "db.system.name").Value);
    }

    private void WriteReport(DataFormat format)
    {
        using (RequestResponseLogger.IsolateForTests())
        {
            Query(Tracker());
            ReportGenerator.CreateStandardReportsWithDiagrams(Features(), T0.UtcDateTime, T0.AddMinutes(1).UtcDateTime, new ReportConfigurationOptions
            {
                ReportsFolderPath = _dir,
                PlantUmlRendering = PlantUmlRendering.BrowserJs,
                TestRunReportDataFormat = format,
                GenerateComponentDiagram = false,
                WriteRunSummaryToConsole = false,
            });
        }
    }

    /// <summary>The issue's own query, on a report the real pipeline wrote.</summary>
    [Fact]
    public void Query_reads_a_spanner_call_as_a_call()
    {
        WriteReport(DataFormat.Json);

        var (output, error) = (new StringWriter(), new StringWriter());
        Assert.True(QueryCommand.Run(["interactions", _dir, "--group-by", "category,kind"], output, error) == 0, error.ToString());

        Assert.Contains("Spanner", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Default", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Event", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The XML and YAML writers leave out what carries nothing, and <c>Default</c> carries nothing.</summary>
    [Theory]
    [InlineData(DataFormat.Xml, "TestRunReport.xml")]
    [InlineData(DataFormat.Yaml, "TestRunReport.yml")]
    public void A_data_file_says_nothing_of_a_calls_kind(DataFormat format, string file)
    {
        WriteReport(format);

        var text = File.ReadAllText(Path.Combine(_dir, file));

        Assert.Contains("RequestResponseId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MetaType", text, StringComparison.Ordinal);
    }
}
