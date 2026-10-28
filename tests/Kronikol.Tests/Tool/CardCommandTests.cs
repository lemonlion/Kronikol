using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Kronikol.Parties;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tests.CallGraph;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.3: <c>kronikol card</c> writes <c>service.json</c> from a report already written, through
/// the one writer. It takes no declarations, since the card's <c>behaviour</c> is a function of the report (Q15), and it
/// mints no run id.
/// </summary>
[Collection("DiagramsFetcher")]
public class CardCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-card").FullName;

    public CardCommandTests()
    {
        DefaultDiagramsFetcher.Reset();
        TestClientRegistry.Clear();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        TestClientRegistry.Clear();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static (string Out, string Err, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = Commands.Dispatch(["card", .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private static readonly DateTime Start = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A real run written by the run end: the test orders pancakes from Breakfast Provider, which asks Cow Service for milk
    /// inside that request, and the service writes its outbox outside every scenario.
    /// </summary>
    private void WriteRun(string directory, bool json = false, string? serviceId = null, bool observeTheClient = false, bool userActions = false)
    {
        // this run's calls alone: the process's store holds every other test's, which a run reports as background calls
        using var isolation = RequestResponseLogger.IsolateForTests();
        var testId = "card-" + Guid.NewGuid().ToString("N");
        if (observeTheClient)
            TestClientRegistry.Register("Caller", "BreakfastProvider.Api").Saw("Breakfast Provider");

        var (order, milk) = (Guid.NewGuid(), Guid.NewGuid());
        var trace = Guid.NewGuid();
        Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Post, "{\"dish\":\"pancakes\"}", new Uri("http://localhost/orders/123"), [], "Breakfast Provider", "Caller", RequestResponseType.Request, trace, order, false)
            { AttributionSource = AttributionSource.TestContext, Phase = TestPhase.Action });
        Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Get, null, new Uri("http://localhost:5001/milk"), [], "Cow Service", "Breakfast Provider", RequestResponseType.Request, trace, milk, false)
            { AttributionSource = AttributionSource.RequestHeader });
        Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Get, null, new Uri("http://localhost:5001/milk"), [], "Cow Service", "Breakfast Provider", RequestResponseType.Response, trace, milk, false, HttpStatusCode.OK)
            { AttributionSource = AttributionSource.RequestHeader });
        Log(new RequestResponseLog("Order pancakes", testId, HttpMethod.Post, null, new Uri("http://localhost/orders/123"), [], "Breakfast Provider", "Caller", RequestResponseType.Response, trace, order, false, HttpStatusCode.Created)
            { AttributionSource = AttributionSource.TestContext, Phase = TestPhase.Action });
        if (userActions)
        {
            // Playwright's user actions travel with the empty Guid, which is no id: two kept as ids would pair with each other
            foreach (var (target, status) in new[] { ("http://localhost/ui/menu", HttpStatusCode.OK), ("http://localhost/ui/order", HttpStatusCode.Accepted) })
            {
                Log(new RequestResponseLog("Order pancakes", testId, "click", null, new Uri(target), [], "Breakfast Provider", "Browser", RequestResponseType.Request, Guid.Empty, Guid.Empty, false)
                    { IsUserAction = true });
                Log(new RequestResponseLog("Order pancakes", testId, "click", null, new Uri(target), [], "Breakfast Provider", "Browser", RequestResponseType.Response, Guid.Empty, Guid.Empty, false, status)
                    { IsUserAction = true });
            }
        }

        var outbox = Guid.NewGuid();
        var unknown = TestIdentityScope.UnknownTestId;
        Log(new RequestResponseLog(unknown, unknown, HttpMethod.Post, null, new Uri("http://localhost:5002/outbox"), [], "Outbox Store", "Breakfast Provider", RequestResponseType.Request, Guid.NewGuid(), outbox, false)
            { AttributionSource = AttributionSource.Expired });
        Log(new RequestResponseLog(unknown, unknown, HttpMethod.Post, null, new Uri("http://localhost:5002/outbox"), [], "Outbox Store", "Breakfast Provider", RequestResponseType.Response, Guid.NewGuid(), outbox, false, HttpStatusCode.OK)
            { AttributionSource = AttributionSource.Expired });

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                SourceFile = "Features/Orders.feature",
                Scenarios = [new Scenario { Id = testId, DisplayName = "Order pancakes", Result = ExecutionResult.Passed, SourceFile = "Features/Orders.feature", SourceLine = 7 }]
            }
        ];

        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = directory,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
            ServiceId = serviceId,
            CompressTestRunReport = !json
        };
        ReportGenerator.CreateStandardReportsWithDiagrams(features, Start, Start.AddSeconds(30), options);
    }

    private static void Log(RequestResponseLog log)
    {
        log.Timestamp = DateTimeOffset.UtcNow;
        RequestResponseLogger.Log(log);
    }

    private string Report(string? directory = null) => ReportFile.Resolve(Path.Combine(directory ?? _directory, "TestRunReport.json"));

    private static JsonElement Card(string text) => ServiceCardTests.Root(text);

    // ─── The command ───────────────────────────────────────────

    [Fact]
    public void The_card_goes_to_stdout_and_its_hash_holds()
    {
        WriteRun(_directory);

        var (output, error, exit) = Run(Report());

        Assert.True(exit == 0, error);
        ServiceCardTests.AssertHashHolds(output);
        var card = Card(output);
        Assert.Equal(1, card.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Breakfast Provider", card.GetProperty("behaviour").GetProperty("subject").GetProperty("name").GetString());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Report()))), card.GetProperty("run").GetProperty("report").GetProperty("sha256").GetString());
    }

    [Fact]
    public void A_directory_holding_a_report_is_read_as_the_report()
    {
        WriteRun(_directory);

        var fromFile = Run(Report()).Out;
        Assert.StartsWith("{\"formatVersion\":1,", fromFile, StringComparison.Ordinal);
        Assert.Equal(fromFile, Run(_directory).Out);
    }

    // 5.3.3
    [Fact]
    public void Out_writes_the_file_creating_its_folder()
    {
        WriteRun(_directory);
        var target = Path.Combine(_directory, "cards", "nested", "service.json");

        var (output, error, exit) = Run(Report(), "-o", target);

        Assert.True(exit == 0, error);
        Assert.Equal("", output);
        Assert.Equal(Run(Report()).Out, File.ReadAllText(target));
    }

    [Fact]
    public void No_report_and_an_unknown_option_are_usage_errors()
    {
        Assert.Equal(2, Run().Exit);

        WriteRun(_directory);
        var (_, error, exit) = Run(Report(), "--json");
        Assert.Equal(2, exit);
        Assert.Contains("Unknown option: --json", error);
    }

    [Fact]
    public void A_report_that_cannot_be_read_is_a_runtime_failure()
    {
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal(1, Run(path).Exit);
    }

    // 5.3.1: one run, its two forms.
    [Fact]
    public void The_json_and_the_krkl_of_one_run_give_the_same_behaviour()
    {
        var json = Path.Combine(_directory, "json");
        var krkl = Path.Combine(_directory, "krkl");
        WriteRun(json, json: true);
        WriteRun(krkl);

        Assert.EndsWith(".json", Report(json), StringComparison.Ordinal);
        Assert.EndsWith(".krkl", Report(krkl), StringComparison.Ordinal);
        var fromJson = Card(Run(Report(json)).Out).GetProperty("behaviour");
        var fromKrkl = Card(Run(Report(krkl)).Out).GetProperty("behaviour");
        // the run ids differ (each scenario's test id is a new Guid) but no member of behaviour holds one
        Assert.Equal(fromJson.GetRawText(), fromKrkl.GetRawText());
    }

    // 5.3.2, Q15: the run observed the client it built and declared nothing, so the file has no block and the card infers.
    [Fact]
    public void What_the_run_observed_and_did_not_write_never_reaches_the_card()
    {
        WriteRun(_directory, observeTheClient: true);

        var parties = Card(Run(Report()).Out).GetProperty("behaviour").GetProperty("parties").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!);

        Assert.Equal(("test", "inferred"), (parties["Caller"].GetProperty("kind").GetString(), parties["Caller"].GetProperty("kindSource").GetString()));
        Assert.Equal(("underTest", "inferred"), (parties["Breakfast Provider"].GetProperty("kind").GetString(), parties["Breakfast Provider"].GetProperty("kindSource").GetString()));
    }

    // 5.3.2: a run that declares its service writes its block, the rows it observed included, and the card reads them.
    [Fact]
    public void A_run_that_declares_its_service_gives_the_card_its_block_as_written()
    {
        WriteRun(_directory, serviceId: "breakfast-provider", observeTheClient: true);

        var behaviour = Card(Run(Report()).Out).GetProperty("behaviour");
        var parties = behaviour.GetProperty("parties").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);

        Assert.Equal("observed", parties["Caller"].GetProperty("kindSource").GetString());
        Assert.Equal(("Breakfast Provider", "breakfast-provider", "observed"),
            (behaviour.GetProperty("subject").GetProperty("name").GetString(), behaviour.GetProperty("subject").GetProperty("id").GetString(), behaviour.GetProperty("subject").GetProperty("source").GetString()));
        Assert.Equal(JsonValueKind.Null, behaviour.GetProperty("rules").GetProperty("inference").ValueKind);
    }

    // 5.3.2, F16: the root background block's calls are on their edge, background, in no scenario, with no address.
    [Fact]
    public void The_root_background_blocks_call_is_on_its_edge_and_in_no_scenario()
    {
        WriteRun(_directory);

        var card = Card(Run(Report()).Out);
        var outbox = card.GetProperty("behaviour").GetProperty("edges").EnumerateArray()
            .Single(e => e.GetProperty("to").GetString() == "Outbox Store").GetProperty("operations")[0];

        Assert.Equal(["background"], outbox.GetProperty("causes").EnumerateArray().Select(c => c.GetString()));
        var op = outbox.GetProperty("op").GetString();
        Assert.DoesNotContain(card.GetProperty("behaviour").GetProperty("scenarios").EnumerateArray(), s => s.GetProperty("ops").EnumerateArray().Any(o => o.GetString() == op));
        Assert.DoesNotContain(card.GetProperty("run").GetProperty("scenarios").EnumerateArray(), s => s.GetProperty("ops").EnumerateArray().Any(o => o.GetString() == op));
        Assert.Equal(JsonValueKind.Null, card.GetProperty("run").GetProperty("examples").EnumerateArray().Single(e => e.GetProperty("op").GetString() == op).GetProperty("address").ValueKind);
    }

    // 5.3.2: markers and user actions carry the empty Guid; read as an id it would pair them all in the nesting.
    [Fact]
    public void User_actions_with_the_empty_guid_are_operations_never_nested_and_never_exposed()
    {
        WriteRun(_directory, userActions: true);

        var card = Card(Run(Report()).Out);
        var edges = card.GetProperty("behaviour").GetProperty("edges").EnumerateArray().ToList();
        var browser = edges.Single(e => e.GetProperty("from").GetString() == "Browser").GetProperty("operations").EnumerateArray().ToList();

        Assert.Equal(2, browser.Count);
        Assert.All(browser, o => Assert.Equal(["unknown"], o.GetProperty("effectivePhases").EnumerateArray().Select(p => p.GetString())));
        // answered with no id, so with nothing to pair by: kept as an id, both would take the first answer, 200
        Assert.All(browser, o => Assert.Equal(["none"], o.GetProperty("statuses").EnumerateArray().Select(s => s.GetString())));
        var exposed = card.GetProperty("behaviour").GetProperty("exposes").GetProperty("ops").EnumerateArray().Select(o => o.GetString()).ToList();
        Assert.DoesNotContain(browser, o => exposed.Contains(o.GetProperty("op").GetString()));
    }

    // 5.4.4: for each run of 5.3.2, the card its run end wrote is the one the command writes from its data file, byte for
    // byte. Every run here also writes the root background block's outbox call.
    [Theory]
    [InlineData(false, null, false, false)]
    [InlineData(false, "breakfast-provider", true, false)]
    [InlineData(true, null, true, true)]
    public void The_run_ends_card_is_the_one_the_command_writes(bool json, string? serviceId, bool observeTheClient, bool userActions)
    {
        WriteRun(_directory, json, serviceId, observeTheClient, userActions);

        Assert.Equal(File.ReadAllText(Path.Combine(_directory, "service.json")), Run(Report()).Out);
    }

    // 5.3.5: the run's id and end from the Run.json beside the report, when it lists the report and is not older than it.
    [Fact]
    public void The_run_id_comes_from_the_manifest_beside_the_report_that_lists_it()
    {
        WriteRun(_directory);
        var manifest = RunManifest.TryRead(_directory)!;

        var run = Card(Run(Report()).Out).GetProperty("run");

        Assert.Equal(manifest.Run, run.GetProperty("id").GetString());
        Assert.Equal(manifest.At.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture), run.GetProperty("at").GetString());
    }

    [Fact]
    public void An_earlier_runs_manifest_beside_a_later_report_gives_no_id()
    {
        WriteRun(_directory);
        var manifest = RunManifest.TryRead(_directory)!;
        (manifest with { At = Start.AddMinutes(-5) }).Write(_directory);

        var run = Card(Run(Report()).Out).GetProperty("run");

        Assert.Equal(JsonValueKind.Null, run.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("at").ValueKind);
    }

    [Fact]
    public void A_report_with_no_manifest_beside_it_gives_no_id()
    {
        WriteRun(_directory);
        File.Delete(Path.Combine(_directory, RunManifest.FileName));

        Assert.Equal(JsonValueKind.Null, Card(Run(Report()).Out).GetProperty("run").GetProperty("id").ValueKind);
    }

    // 5.3.4: what tells the fallback's name apart is not in a report written before 5.13.0, so the card keeps the name.
    [Fact]
    public void A_report_written_before_5_13_keeps_the_name_it_recorded_for_a_remote_host()
    {
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.WriteAllText(path, Legacy("5.12.1", "localhost:443", "https://pricing.example.com/prices"));

        var behaviour = Card(Run(path).Out).GetProperty("behaviour");

        Assert.Contains(behaviour.GetProperty("parties").EnumerateArray(), p => p.GetProperty("name").GetString() == "localhost:443");
        Assert.Contains("localhost:443", behaviour.GetProperty("unresolved").EnumerateArray().Select(n => n.GetString()));
    }

    // 5.2.5: a mergeable file written before 3.1.0 could not carry calls, which is not a run that made none.
    [Fact]
    public void A_mergeable_file_written_before_3_1_says_its_calls_were_not_recorded()
    {
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.WriteAllText(path, """
            {"mergeableFormatVersion":1,"kronikolVersion":"3.0.50","startTime":"2026-01-01T10:00:00Z","endTime":"2026-01-01T10:00:30Z",
             "features":[{"name":"Orders","scenarios":[{"id":"t1","name":"Order pancakes","result":"Passed"}]}]}
            """);

        var (output, error, exit) = Run(path);

        Assert.True(exit == 0, error);
        Assert.Equal("notRecorded", Card(output).GetProperty("behaviour").GetProperty("capture").GetProperty("calls").GetString());
    }

    // ... and a report that can carry calls and carries none is a run that made none: its calls were recorded.
    [Fact]
    public void A_run_that_made_no_call_says_its_calls_were_recorded()
    {
        using (RequestResponseLogger.IsolateForTests())
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "quiet-" + Guid.NewGuid().ToString("N"), DisplayName = "Read the menu", Result = ExecutionResult.Passed }] }],
                Start, Start.AddSeconds(30),
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = _directory, InternalFlowTracking = false, GenerateComponentDiagram = false,
                    GenerateSpecificationsReport = false, GenerateSpecificationsData = false
                });

        var behaviour = Card(Run(Report()).Out).GetProperty("behaviour");

        Assert.Equal("recorded", behaviour.GetProperty("capture").GetProperty("calls").GetString());
        Assert.Empty(behaviour.GetProperty("edges").EnumerateArray());
    }

    // 5.2.8: a large report carded in one scan, the statement bodies the operation key reads included.
    [Fact]
    public void A_large_report_is_carded_in_one_scan_within_its_budget()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = [.. Enumerable.Range(0, 2000).Select(i => new Scenario { Id = $"large-{i}", DisplayName = $"Order {i}", Result = ExecutionResult.Passed })]
            }
        ];
        var logs = new List<RequestResponseLog>();
        var at = new DateTimeOffset(Start);
        foreach (var i in Enumerable.Range(0, 2000))
        {
            var (order, insert, trace) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var testId = $"large-{i}";
            logs.Add(new RequestResponseLog("Order", testId, HttpMethod.Post, "{\"dish\":\"pancakes\"}", new Uri($"http://localhost/orders/{i}"), [], "Breakfast Provider", "Caller",
                RequestResponseType.Request, trace, order, false) { AttributionSource = AttributionSource.TestContext, Timestamp = at });
            logs.Add(new RequestResponseLog("Order", testId, "INSERT", $"INSERT INTO orders (id, dish) VALUES ({i}, 'pancakes')", new Uri("sql://localhost/orders"), [], "Orders Db", "Breakfast Provider",
                RequestResponseType.Request, trace, insert, false, DependencyCategory: "SQL") { AttributionSource = AttributionSource.RequestHeader, Timestamp = at.AddMilliseconds(1) });
            logs.Add(new RequestResponseLog("Order", testId, "INSERT", null, new Uri("sql://localhost/orders"), [], "Orders Db", "Breakfast Provider",
                RequestResponseType.Response, trace, insert, false, HttpStatusCode.OK, DependencyCategory: "SQL") { AttributionSource = AttributionSource.RequestHeader, Timestamp = at.AddMilliseconds(2) });
            logs.Add(new RequestResponseLog("Order", testId, HttpMethod.Post, null, new Uri($"http://localhost/orders/{i}"), [], "Breakfast Provider", "Caller",
                RequestResponseType.Response, trace, order, false, HttpStatusCode.Created) { AttributionSource = AttributionSource.TestContext, Timestamp = at.AddMilliseconds(3) });
        }
        var report = Path.Combine(_directory, "TestRunReport.json");
        File.Move(ReportGenerator.GenerateTestRunReportData(features, Start, Start.AddMinutes(5), "Large_" + Guid.NewGuid().ToString("N")[..8] + ".json",
            DataFormat.Json, null, [.. logs]), report);

        var scale = ContentionScale.Measure();
        var watch = Stopwatch.StartNew();
        var (output, error, exit) = Run(report);
        watch.Stop();

        Assert.True(exit == 0, error);
        var behaviour = Card(output).GetProperty("behaviour");
        Assert.Equal(2000, behaviour.GetProperty("scenarios").GetArrayLength());
        Assert.Contains(behaviour.GetProperty("edges").EnumerateArray().SelectMany(e => e.GetProperty("operations").EnumerateArray()),
            o => o.GetProperty("target").GetString()!.StartsWith("/orders INSERT INTO orders", StringComparison.Ordinal));
        Assert.True(watch.Elapsed.TotalMilliseconds < LargeReportBudgetMs * scale, $"{watch.Elapsed.TotalMilliseconds:F0} ms against {LargeReportBudgetMs * scale:F0}");
    }

    /// <summary>Measured at 302 to 309 ms on 2026-10-28, with the machine busy enough to stretch every budget fivefold.</summary>
    private const double LargeReportBudgetMs = 1500;

    // 5.2.6: the rules a report recorded shape the card's targets as they shape route's, and rules.templateRules says which:
    // their hash; "none" from 5.19.0, which records a run's rules whenever it has any; null for a report that cannot say,
    // written before 5.19.0 or by Kronikol4J, whose version is its own.
    [Theory]
    [InlineData("5.19.0", true, "rules")]
    [InlineData("5.19.0+e21bd103", false, "none")]
    [InlineData("5.18.0", false, null)]
    [InlineData("0.1.24", false, null)]
    public void The_rules_a_report_recorded_shape_its_targets_and_the_card_says_which(string version, bool block, string? recorded)
    {
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.WriteAllText(path, Templated(version, block));

        var behaviour = Card(Run(path).Out).GetProperty("behaviour");

        var target = behaviour.GetProperty("edges").EnumerateArray().Single().GetProperty("operations").EnumerateArray().Single()
            .GetProperty("target").GetString();
        var templateRules = behaviour.GetProperty("rules").GetProperty("templateRules");
        var builtIn = CallOperation.Parts("GET", "http://localhost/orders/ORD-123/price", null, null, rules: null, queryKeys: false).Target;
        if (recorded == "rules")
        {
            Assert.Equal("/orders/{order}/price", target);
            Assert.Equal(HistoryShapeRules.Create([new HistoryShapeTemplate("ORD-[0-9]+", "{order}")])!.Hash, templateRules.GetString());
        }
        else
        {
            Assert.Equal(builtIn, target);
            Assert.Equal(recorded, templateRules.ValueKind == JsonValueKind.Null ? null : templateRules.GetString());
        }
    }

    /// <summary>One test call to the service at an order id the consumer's rule names, with the rule recorded when <paramref name="block"/>.</summary>
    private static string Templated(string version, bool block) => $$$"""
        {"formatVersion":3,"kronikolVersion":"{{{version}}}","suite":"Orders","startTime":"2026-10-28T10:00:00Z","endTime":"2026-10-28T10:00:30Z",
         {{{(block ? "\"shapeTemplates\":[{\"pattern\":\"ORD-[0-9]+\",\"placeholder\":\"{order}\"}]," : "")}}}
         "features":[{"name":"Orders","scenarios":[{"id":"t1","stableId":"0123456789abcdef","name":"Price an order","result":"Passed","httpInteractions":[
           {"type":"Request","method":"GET","uri":"http://localhost/orders/ORD-123/price","serviceName":"Breakfast Provider","callerName":"Caller","requestResponseId":"a1","traceId":"t1","attributionSource":"TestContext","phase":"Action"},
           {"type":"Response","method":"GET","uri":"http://localhost/orders/ORD-123/price","serviceName":"Breakfast Provider","callerName":"Caller","requestResponseId":"a1","traceId":"t1","statusCode":200,"statusText":"OK"}]}]}],
         "background":{"calls":0,"afterScenarioEnd":[],"interactions":[]}}
        """;

    /// <summary>A data file as a 5.x writer before 5.13.0 wrote it: one test call to the service, which calls a remote host the fallback named.</summary>
    private static string Legacy(string version, string remoteName, string remoteUri) => $$$"""
        {"formatVersion":2,"kronikolVersion":"{{{version}}}","suite":"Orders","startTime":"2026-10-01T10:00:00Z","endTime":"2026-10-01T10:00:30Z",
         "features":[{"name":"Orders","scenarios":[{"id":"t1","stableId":"0123456789abcdef","name":"Price an order","result":"Passed","httpInteractions":[
           {"type":"Request","method":"GET","uri":"http://localhost/orders/1/price","serviceName":"Breakfast Provider","callerName":"Caller","requestResponseId":"a1","traceId":"t1","attributionSource":"TestContext","phase":"Action"},
           {"type":"Request","method":"GET","uri":"{{{remoteUri}}}","serviceName":"{{{remoteName}}}","callerName":"Breakfast Provider","requestResponseId":"a2","traceId":"t1","attributionSource":"RequestHeader"},
           {"type":"Response","method":"GET","uri":"{{{remoteUri}}}","serviceName":"{{{remoteName}}}","callerName":"Breakfast Provider","requestResponseId":"a2","traceId":"t1","statusCode":200,"statusText":"OK"},
           {"type":"Response","method":"GET","uri":"http://localhost/orders/1/price","serviceName":"Breakfast Provider","callerName":"Caller","requestResponseId":"a1","traceId":"t1","statusCode":200,"statusText":"OK"}]}]}],
         "background":{"calls":0,"afterScenarioEnd":[],"interactions":[]}}
        """;
}
