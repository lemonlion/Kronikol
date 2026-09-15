using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What the report says about the calls a scenario did not make: the <c>background</c> block of every
/// data format, the section in the HTML, the diagnostic, and the standard flow that does the
/// re-attribution once for every output (BACKGROUND_ATTRIBUTION_PLAN, options D and F).
/// </summary>
[Collection("DiagramsFetcher")]
public class BackgroundCallsReportTests : IDisposable
{
    private static readonly DateTimeOffset Ended = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-bg-" + Guid.NewGuid().ToString("N"));

    public BackgroundCallsReportTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static Feature[] Features(string testId, DateTimeOffset endedAt) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = testId, DisplayName = "Pay by card", Result = ExecutionResult.Passed, EndedAt = endedAt }]
        }
    ];

    private static RequestResponseLog[] Pair(string testId, DateTimeOffset at, string path = "/api/orders")
    {
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        return
        [
            new RequestResponseLog("Pay by card", testId, HttpMethod.Get, null, new Uri("http://orders" + path), [], "orders", "Test",
                RequestResponseType.Request, trace, pair, false) { Timestamp = at, AttributionSource = AttributionSource.TestContext },
            new RequestResponseLog("Pay by card", testId, HttpMethod.Get, "[]", new Uri("http://orders" + path), [], "orders", "Test",
                RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK) { Timestamp = at.AddMilliseconds(20), AttributionSource = AttributionSource.TestContext }
        ];
    }

    /// <summary>One pair the scenario made and one pair the host made after the scenario ended, expired.</summary>
    private static (Feature[] Features, RequestResponseLog[] Logs) ExpiredRun(string testId)
    {
        var features = Features(testId, Ended);
        var logs = BackgroundAttribution.Expire(features, [.. Pair(testId, Ended.AddSeconds(-2)), .. Pair(testId, Ended.AddSeconds(30), "/api/orders/sweep")]);
        return (features, logs);
    }

    private string Write(Feature[] features, RequestResponseLog[]? logs, DataFormat format)
    {
        var extension = format switch { DataFormat.Json => "json", DataFormat.Xml => "xml", _ => "yml" };
        return ReportGenerator.GenerateTestRunReportData(
            features, Ended.AddMinutes(-1).UtcDateTime, Ended.AddMinutes(1).UtcDateTime,
            $"Background_{Guid.NewGuid():N}.{extension}", format, null, logs);
    }

    [Fact]
    public void The_json_lists_the_background_calls_and_keeps_them_out_of_the_scenario()
    {
        var (features, logs) = ExpiredRun("bg-1");

        using var document = JsonDocument.Parse(File.ReadAllText(Write(features, logs, DataFormat.Json)));
        var root = document.RootElement;

        var background = root.GetProperty("background");
        Assert.Equal(1, background.GetProperty("calls").GetInt32());
        var group = Assert.Single(background.GetProperty("afterScenarioEnd").EnumerateArray());
        Assert.Equal("bg-1", group.GetProperty("scenarioId").GetString());
        Assert.Equal("Pay by card", group.GetProperty("scenario").GetString());
        Assert.Equal(1, group.GetProperty("calls").GetInt32());
        Assert.Equal("2026-01-01T10:00:30.020Z", group.GetProperty("lastAt").GetString());

        var interactions = background.GetProperty("interactions").EnumerateArray().ToArray();
        Assert.Equal(2, interactions.Length);
        Assert.All(interactions, i => Assert.Equal("Expired", i.GetProperty("attributionSource").GetString()));
        Assert.All(interactions, i => Assert.Equal("bg-1", i.GetProperty("expiredFrom").GetString()));
        Assert.Equal("http://orders/api/orders/sweep", interactions[0].GetProperty("uri").GetString());

        // The scenario keeps only the pair it made.
        var scenario = root.GetProperty("features").EnumerateArray().Single().GetProperty("scenarios").EnumerateArray().Single();
        var own = scenario.GetProperty("httpInteractions").EnumerateArray().ToArray();
        Assert.Equal(2, own.Length);
        Assert.All(own, i => Assert.Equal("http://orders/api/orders", i.GetProperty("uri").GetString()));
    }

    [Fact]
    public void A_run_with_nothing_in_the_background_still_writes_the_block()
    {
        var features = Features("bg-2", Ended);

        using var document = JsonDocument.Parse(File.ReadAllText(Write(features, Pair("bg-2", Ended.AddSeconds(-2)), DataFormat.Json)));

        var background = document.RootElement.GetProperty("background");
        Assert.Equal(0, background.GetProperty("calls").GetInt32());
        Assert.Empty(background.GetProperty("afterScenarioEnd").EnumerateArray());
        Assert.Empty(background.GetProperty("interactions").EnumerateArray());
    }

    [Fact]
    public void The_xml_omits_the_empty_containers_and_keeps_the_count()
    {
        var background = XDocument.Load(Write(Features("bg-3x", Ended), Pair("bg-3x", Ended.AddSeconds(-2)), DataFormat.Xml)).Root!.Element("Background")!;

        Assert.Equal("0", background.Element("Calls")!.Value);
        Assert.Null(background.Element("AfterScenarioEnd"));
        Assert.Null(background.Element("Interactions"));
    }

    [Fact]
    public void A_report_written_without_any_logs_still_writes_the_block()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Write(Features("bg-3", Ended), null, DataFormat.Json)));

        Assert.Equal(0, document.RootElement.GetProperty("background").GetProperty("calls").GetInt32());
    }

    [Fact]
    public void The_schema_declares_the_block_and_the_output_honours_it()
    {
        var (features, logs) = ExpiredRun("bg-4");
        var reportPath = Write(features, logs, DataFormat.Json);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"Background_{Guid.NewGuid():N}.schema.json", DataFormat.Json);

        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var background = schema.RootElement.GetProperty("properties").GetProperty("background");
        Assert.Equal("object", background.GetProperty("type").GetString());
        Assert.Equal(["calls", "afterScenarioEnd", "interactions"], background.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.Contains("background", schema.RootElement.GetProperty("required").EnumerateArray().Select(r => r.GetString()));

        var errors = SchemaValidationTests.Validate(schemaPath, reportPath);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void The_xml_carries_it_and_the_xsd_admits_it()
    {
        var (features, logs) = ExpiredRun("bg-5");
        var xmlPath = Write(features, logs, DataFormat.Xml);

        var background = XDocument.Load(xmlPath).Root!.Element("Background")!;
        Assert.Equal("1", background.Element("Calls")!.Value);
        var group = Assert.Single(background.Element("AfterScenarioEnd")!.Elements("Scenario"));
        Assert.Equal("bg-5", group.Element("ScenarioId")!.Value);
        Assert.Equal("Pay by card", group.Element("Name")!.Value);
        Assert.Equal("1", group.Element("Calls")!.Value);
        Assert.Equal("2026-01-01T10:00:30.020Z", group.Element("LastAt")!.Value);
        Assert.Equal(2, background.Element("Interactions")!.Elements("HttpInteraction").Count());

        var schemaSet = new XmlSchemaSet();
        schemaSet.Add("", System.Xml.XmlReader.Create(new StringReader(File.ReadAllText(
            ReportGenerator.GenerateTestRunReportSchema($"Background_{Guid.NewGuid():N}.xsd", DataFormat.Xml)))));
        var errors = new List<string>();
        XDocument.Load(xmlPath).Validate(schemaSet, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);
    }

    [Fact]
    public void The_yaml_carries_it_and_validates_against_its_schema()
    {
        var (features, logs) = ExpiredRun("bg-6");
        var yamlPath = Write(features, logs, DataFormat.Yaml);
        var yaml = File.ReadAllText(yamlPath);

        Assert.Contains("\nBackground:\n  Calls: 1\n  AfterScenarioEnd:\n    - ScenarioId: bg-6\n      Scenario: Pay by card\n      Calls: 1\n", yaml, StringComparison.Ordinal);
        Assert.Contains("\n  Interactions:\n    - Type: Request\n", yaml, StringComparison.Ordinal);

        var schemaPath = ReportGenerator.GenerateTestRunReportSchema($"Background_{Guid.NewGuid():N}.schema.json", DataFormat.Yaml);
        var errors = YamlSchemaValidationTests.ValidateYaml(schemaPath, yamlPath);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void The_html_has_a_background_section_only_when_there_is_something_in_it()
    {
        var (features, logs) = ExpiredRun("bg-7");
        var summary = BackgroundAttribution.Summarise(logs, features);

        var with = File.ReadAllText(ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            $"Background_{Guid.NewGuid():N}.html", "Run", true, background: summary));
        var without = File.ReadAllText(ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            $"Background_{Guid.NewGuid():N}.html", "Run", true, background: BackgroundCalls.None));

        Assert.Contains("<details class=\"background-calls\">", with, StringComparison.Ordinal);
        Assert.Contains("Background calls (1 after a scenario ended)", with, StringComparison.Ordinal);
        Assert.Contains("<td>Pay by card</td><td>orders</td><td>GET</td><td>/api/orders/sweep</td><td>1</td><td>2026-01-01T10:00:30.020Z</td>", with, StringComparison.Ordinal);
        // The stylesheet names the class whether or not the section renders; only the element says it did.
        Assert.DoesNotContain("<details class=\"background-calls\">", without, StringComparison.Ordinal);
    }

    [Fact]
    public void The_html_heading_counts_the_calls_no_scenario_made_when_they_are_kept()
    {
        // With CaptureBackground on, a detached hosted service's calls sit in the same table under
        // "(no scenario)"; the heading and the note say so, or a reader takes the whole table for expiry.
        var (features, logs) = ExpiredRun("bg-8");
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        var kept = new[]
        {
            new RequestResponseLog(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, "Query", null, new Uri("http://cosmos/dbs/db/colls/outbox/docs"), [], "CosmosDB", "Breakfast Provider",
                RequestResponseType.Request, trace, pair, false) { Timestamp = Ended.AddSeconds(-5), AttributionSource = AttributionSource.Detached },
            new RequestResponseLog(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, "Query", "[]", new Uri("http://cosmos/dbs/db/colls/outbox/docs"), [], "CosmosDB", "Breakfast Provider",
                RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK) { Timestamp = Ended.AddSeconds(-5).AddMilliseconds(3), AttributionSource = AttributionSource.Detached }
        };
        var summary = BackgroundAttribution.Summarise([.. logs, .. kept], features);

        var html = File.ReadAllText(ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            $"Background_{Guid.NewGuid():N}.html", "Run", true, background: summary));

        Assert.Equal(2, summary.Calls);
        Assert.Contains("Background calls (2: 1 after a scenario ended, 1 with no scenario)", html, StringComparison.Ordinal);
        Assert.Contains("<td>(no scenario)</td><td>CosmosDB</td><td>Query</td><td>/dbs/db/colls/outbox/docs</td><td>1</td>", html, StringComparison.Ordinal);
        Assert.Contains("no scenario at all", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_html_heading_names_only_the_kind_of_call_the_table_holds()
    {
        var features = Features("bg-9", Ended);
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        var kept = new[]
        {
            new RequestResponseLog(TestIdentityScope.UnknownTestName, TestIdentityScope.UnknownTestId, HttpMethod.Get, null, new Uri("http://orders/api/poll"), [], "orders", "host",
                RequestResponseType.Request, trace, pair, false) { Timestamp = Ended, AttributionSource = AttributionSource.Detached }
        };

        var html = File.ReadAllText(ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            $"Background_{Guid.NewGuid():N}.html", "Run", true, background: BackgroundAttribution.Summarise(kept, features)));

        Assert.Contains("Background calls (1 with no scenario)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_standard_flow_expires_once_for_every_output_and_records_a_diagnostic()
    {
        var testId = "bg-flow-" + Guid.NewGuid().ToString("N");
        var ended = DateTimeOffset.UtcNow.AddMinutes(-1);
        foreach (var log in Pair(testId, ended.AddSeconds(-5)))
            RequestResponseLogger.Log(log);
        // Logged now, a minute after the scenario ended: the host's, not the scenario's.
        RequestResponseLogger.LogPair("Pay by card", testId, HttpMethod.Get, new Uri("http://orders/api/orders/sweep"), "orders", "Test",
            source: AttributionSource.TestContext);

        ReportGenerator.CreateStandardReportsWithDiagrams(Features(testId, ended), ended.AddMinutes(-1).UtcDateTime, DateTime.UtcNow, new ReportConfigurationOptions
        {
            ReportsFolderPath = _dir,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        });

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "TestRunReport.json")));
        var root = document.RootElement;

        // The scenario's own block: this test's scenario, with only the pair it made.
        var scenario = root.GetProperty("features").EnumerateArray().Single().GetProperty("scenarios").EnumerateArray().Single();
        Assert.Equal(2, scenario.GetProperty("httpInteractions").GetArrayLength());

        // The background block is process-wide (the logger is), so look for this test's group rather than counting.
        var group = Assert.Single(root.GetProperty("background").GetProperty("afterScenarioEnd").EnumerateArray(),
            g => g.GetProperty("scenarioId").GetString() == testId);
        Assert.Equal(1, group.GetProperty("calls").GetInt32());

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "BackgroundCalls" && d.GetProperty("scenarioId").GetString() == testId);
        Assert.Contains("1 call arrived after 'Pay by card' ended", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);

        var html = File.ReadAllText(Path.Combine(_dir, "TestRunReport.html"));
        Assert.Contains("<details class=\"background-calls\">", html, StringComparison.Ordinal);
        Assert.Contains("<td>/api/orders/sweep</td>", html, StringComparison.Ordinal);
    }
}
