using System.Globalization;
using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tests.Ingestion.Cucumber;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion;

/// <summary>
/// What the run window does with a record outside it once the host has said what lies there (plans/RUN_WINDOW_PLAN.md
/// R2, #187): another run's traffic is left to that run's report (<see cref="IngestRequest.OtherRunWindows"/>), the
/// stack's start-up is kept as a scenario of its own (<see cref="IngestRequest.SetupWindow"/>), and the entry counts only
/// what no window explains. The capture is harness P1's, the issue's shape: a stack's start-up at 09:47, a browser run
/// from 10:00:00 to 10:00:11 with two real losses after its verdict, and a device run from 10:00:11.6 to 10:00:21.
/// </summary>
[Collection("DiagramsFetcher")] // shares the process-global tracking store with every class that runs the pipeline
public class RunWindowSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-run-window-setup-" + Guid.NewGuid().ToString("N"));

    public RunWindowSetupTests()
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

    private static DateTimeOffset At(string time) =>
        DateTimeOffset.Parse("2026-10-21T" + time + "Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static int _pairs;

    private static InteractionRecord[] Call(string at, string answeredAt, string testId, string service, string caller, string method, string uri,
        string? capturedBy = null)
    {
        var id = $"c{Interlocked.Increment(ref _pairs)}";
        return
        [
            new() { Type = "Request", TestId = testId, TestName = testId, RequestResponseId = id, Method = method, Uri = uri, ServiceName = service, CallerName = caller, CapturedBy = capturedBy, Timestamp = At(at) },
            new() { Type = "Response", TestId = testId, TestName = testId, RequestResponseId = id, Method = method, Uri = uri, ServiceName = service, CallerName = caller, CapturedBy = capturedBy, StatusCode = "OK", Timestamp = At(answeredAt) },
        ];
    }

    /// <summary>Harness P1's capture, call for call: 16 records of start-up, 10 of the browser run, 4 lost, 6 of the device run.</summary>
    private static List<InteractionRecord> Capture(bool losses = true)
    {
        var records = new List<InteractionRecord>();
        // The stack's start-up, 13 minutes before the first run: a seeder, then the stack's own checks that its API is up.
        for (var i = 0; i < 6; i++)
            records.AddRange(Call($"09:47:0{i}.000", $"09:47:0{i}.050", "session", "Database", "seeder", "POST", $"db://orders/insert?seed={i}"));
        records.AddRange(Call("09:47:20.000", "09:47:20.010", "session", "Orders Api", "compose", "GET", "http://orders-api/health"));
        records.AddRange(Call("09:47:25.000", "09:47:25.010", "session", "Orders Api", "compose", "GET", "http://orders-api/health"));
        // The browser run: a global login, then two tests whose own calls carry their ids and whose database calls the
        // tap's fallback id.
        records.AddRange(Call("10:00:00.500", "10:00:00.600", "session", "Orders Api", "browser", "POST", "http://orders-api/login"));
        records.AddRange(Call("10:00:02.000", "10:00:02.300", "b1", "Orders Api", "browser", "GET", "http://orders-api/orders"));
        records.AddRange(Call("10:00:02.100", "10:00:02.200", "session", "Database", "Orders Api", "GET", "db://orders/find?customer=1"));
        records.AddRange(Call("10:00:07.000", "10:00:07.300", "b2", "Orders Api", "browser", "GET", "http://orders-api/menu"));
        records.AddRange(Call("10:00:07.100", "10:00:07.200", "session", "Database", "Orders Api", "GET", "db://menu/find"));
        if (losses)
        {
            // Two real losses: b2's own call made after the run's verdict, and traffic between the runs that no report covers.
            records.AddRange(Call("10:00:11.300", "10:00:11.350", "b2", "Orders Api", "browser", "POST", "http://orders-api/audit"));
            records.AddRange(Call("10:00:11.400", "10:00:11.450", "session", "Database", "Orders Api", "GET", "db://audit/find"));
        }
        // The device run, 0.6 s after the browser run's verdict, on the same stack.
        records.AddRange(Call("10:00:13.000", "10:00:13.300", "d1", "Orders Api", "device", "GET", "http://orders-api/orders"));
        records.AddRange(Call("10:00:13.100", "10:00:13.200", "session", "Database", "Orders Api", "GET", "db://orders/find?customer=2"));
        records.AddRange(Call("10:00:15.000", "10:00:15.300", "d1", "Orders Api", "device", "GET", "http://orders-api/menu"));
        return records;
    }

    private static TestRunRecord[] BrowserTests(bool endMarker = true) =>
    [
        new() { Event = "testrun", TestId = "__run__", Status = "started", Timestamp = At("10:00:00.000") },
        new() { Event = "start", TestId = "b1", TestName = "Lists orders", Feature = "Orders", Timestamp = At("10:00:01.000") },
        new() { Event = "end", TestId = "b1", Status = "passed", DurationMs = 4000, Timestamp = At("10:00:05.000") },
        new() { Event = "start", TestId = "b2", TestName = "Shows the menu", Feature = "Orders", Timestamp = At("10:00:06.000") },
        new() { Event = "end", TestId = "b2", Status = "passed", DurationMs = 4000, Timestamp = At("10:00:10.000") },
        .. endMarker ? new TestRunRecord[] { new() { Event = "testrun", TestId = "__run__", Status = "passed", Timestamp = At("10:00:11.000") } } : [],
    ];

    private static TestRunRecord[] DeviceTests() =>
    [
        new() { Event = "testrun", TestId = "__run__", Status = "started", Timestamp = At("10:00:11.600") },
        new() { Event = "start", TestId = "d1", TestName = "Orders on a phone", Feature = "Device", Timestamp = At("10:00:12.000") },
        new() { Event = "end", TestId = "d1", Status = "passed", DurationMs = 8000, Timestamp = At("10:00:20.000") },
        new() { Event = "testrun", TestId = "__run__", Status = "passed", Timestamp = At("10:00:21.000") },
    ];

    private static readonly SetupWindow StartUp = new(At("09:47:00.000"), At("09:47:30.000"));
    private static readonly OtherRunWindow Browser = new("browser", At("10:00:00.000"), At("10:00:11.000"));
    private static readonly OtherRunWindow Device = new("device", At("10:00:11.600"), At("10:00:21.000"));

    private ReportConfigurationOptions Options(string name, bool separateSetup = false)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = Path.Combine(_dir, name);
        options.GenerateComponentDiagram = false;
        options.WriteRunSummaryToConsole = false;
        options.SeparateSetup = separateSetup;
        return options;
    }

    /// <summary>A run ingested as the issue's host ingests it: window attribution with the fallback id, a fold, the run window.</summary>
    private IngestResult Ingest(string name, IEnumerable<InteractionRecord> capture, TestRunRecord[] tests, SetupWindow? setup = null,
        OtherRunWindow[]? others = null, bool separateSetup = false, Func<InteractionRecord, bool>? dropUnattributed = null,
        ExecutionResult resultWhenUnknown = ExecutionResult.Passed, bool merge = false) =>
        IngestPipeline.Run(new IngestRequest
        {
            Interactions = capture.ToList(),
            TestRecords = tests,
            Options = Options(name, separateSetup),
            AttributeByTestWindow = true,
            WindowAttributionFallbackId = "session",
            FoldUnknownTestsInto = new UnknownTestFold("Traffic outside any test"),
            DropOutsideRunWindow = true,
            SetupWindow = setup,
            OtherRunWindows = others ?? [],
            DropUnattributed = dropUnattributed,
            ResultWhenUnknown = resultWhenUnknown,
            MergeDuplicateInteractions = merge,
        });

    private static JsonElement[] Scenarios(IngestResult result)
    {
        using var document = JsonDocument.Parse(ReportFile.ReadAny(Path.Combine(result.ReportsDirectory, "TestRunReport.json")));
        return document.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray()).Select(s => s.Clone()).ToArray();
    }

    /// <summary>Each scenario's request URIs in the data file, by scenario id.</summary>
    private static Dictionary<string, string[]> RequestsByScenario(IngestResult result) =>
        Scenarios(result).ToDictionary(s => s.GetProperty("id").GetString()!, s => ReportFile.Items(s, "httpInteractions")
            .Where(i => i.GetProperty("type").GetString() == "Request").Select(i => i.GetProperty("uri").GetString()!).ToArray());

    private static string? Dropped(IngestResult result) =>
        result.Diagnostics.SingleOrDefault(d => d.Kind == DiagnosticKind.DroppedOutsideRunWindow)?.Message;

    private static string[] Others(IngestResult result) =>
        result.Diagnostics.Where(d => d.Kind == DiagnosticKind.Other).Select(d => d.Message).ToArray();

    private static string Query(IngestResult result, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([.. args, result.ReportsDirectory], output, error);
        Assert.True(exit == 0, error.ToString());
        return output.ToString();
    }

    // ------------------------------------------------------------------------------------------
    // Fact 12: the issue's shape
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void The_browser_report_keeps_the_start_up_leaves_the_device_run_and_counts_what_is_left()
    {
        var result = Ingest("Browser", Capture(), BrowserTests(), StartUp, [Device]);

        Assert.Equal(16, result.SetupRecords);
        Assert.Equal(new Dictionary<string, int> { ["device"] = 6 }, result.OtherRunRecords);
        Assert.Equal("4 interaction record(s) outside the run window dropped that no other window explains: 0 before the run began, "
                     + "4 after it ended (2 name a test in this run's tests file). The run window: 2026-10-21T10:00:00.0000000+00:00 → "
                     + "2026-10-21T10:00:11.0000000+00:00.", Dropped(result));
        var requests = RequestsByScenario(result);
        Assert.Equal(8, requests["stack-start-up"].Length);
        Assert.Equal(["http://orders-api/orders", "db://orders/find?customer=1"], requests["b1"]);
        Assert.Equal(["http://orders-api/menu", "db://menu/find"], requests["b2"]);
        Assert.Equal(["http://orders-api/login"], requests["outside-any-test"]);
    }

    [Fact]
    public void The_device_report_keeps_the_start_up_and_leaves_the_browser_run()
    {
        var result = Ingest("Device", Capture(), DeviceTests(), StartUp, [Browser]);

        Assert.Equal(16, result.SetupRecords);
        Assert.Equal(new Dictionary<string, int> { ["browser"] = 10 }, result.OtherRunRecords);
        // b2's late call names no test of the device run's tests file.
        Assert.Equal("4 interaction record(s) outside the run window dropped that no other window explains: 4 before the run began, "
                     + "0 after it ended. The run window: 2026-10-21T10:00:11.6000000+00:00 → 2026-10-21T10:00:21.0000000+00:00.", Dropped(result));
        Assert.Equal(["http://orders-api/orders", "db://orders/find?customer=2", "http://orders-api/menu"], RequestsByScenario(result)["d1"]);
    }

    [Fact]
    public void Each_window_implies_the_run_window()
    {
        foreach (var (name, setup, others) in new (string, SetupWindow?, OtherRunWindow[])[] { ("SetupOnly", StartUp, []), ("OthersOnly", null, [Device]) })
        {
            var result = IngestPipeline.Run(new IngestRequest
            {
                Interactions = Capture(), TestRecords = BrowserTests(), Options = Options(name), SetupWindow = setup, OtherRunWindows = others,
            });

            Assert.Equal(setup is null ? 0 : 16, result.SetupRecords);
            Assert.Equal(others.Length == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["device"] = 6 }, result.OtherRunRecords);
            Assert.NotNull(Dropped(result));
        }
    }

    // ------------------------------------------------------------------------------------------
    // Facts 13 and 14: which window takes a call
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void A_call_in_another_runs_window_and_the_setup_window_goes_to_the_other_run()
    {
        var earlier = new OtherRunWindow("earlier", At("09:47:20.000"), At("09:47:30.000"));

        var result = Ingest("Precedence", Capture(losses: false), BrowserTests(), StartUp, [earlier, Device]);

        // The two health checks are in both: the other run takes them, and the setup window keeps the six inserts.
        Assert.Equal(12, result.SetupRecords);
        Assert.Equal(new Dictionary<string, int> { ["earlier"] = 4, ["device"] = 6 }, result.OtherRunRecords);
        Assert.Null(Dropped(result));
    }

    [Fact]
    public void A_setup_window_over_everything_before_the_run_keeps_what_the_run_before_leaves()
    {
        // Harness P3 H: the device run's host declares everything before it as its setup, and the browser run as another run.
        var everything = new SetupWindow(At("09:47:00.000"), At("10:00:11.500"));

        var result = Ingest("Everything", Capture(), DeviceTests(), everything, [Browser]);

        Assert.Equal(20, result.SetupRecords);
        Assert.Equal(new Dictionary<string, int> { ["browser"] = 10 }, result.OtherRunRecords);
        Assert.Null(Dropped(result));
        // A setup window that reaches past another run's verdict takes its late calls into the setup scenario.
        Assert.Contains("http://orders-api/audit", RequestsByScenario(result)["stack-start-up"]);
    }

    [Fact]
    public void Of_two_other_runs_that_overlap_the_first_declared_takes_the_call()
    {
        var first = new OtherRunWindow("first", At("10:00:11.200"), At("10:00:11.500"));
        var second = new OtherRunWindow("second", At("10:00:11.000"), At("10:00:11.450"));

        var result = Ingest("Overlap", Capture(), BrowserTests(), StartUp, [first, second, Device]);

        Assert.Equal(new Dictionary<string, int> { ["first"] = 4, ["second"] = 0, ["device"] = 6 }, result.OtherRunRecords);
        Assert.Equal(["first", "second", "device"], result.OtherRunRecords.Keys);
        Assert.Null(Dropped(result));
    }

    [Fact]
    public void A_call_at_a_windows_last_instant_is_inside_it()
    {
        var capture = Capture(losses: false);
        // The device run's last instant, and the setup window's: bounds are inclusive, as the run window's are.
        capture.AddRange(Call("10:00:21.000", "10:00:21.050", "session", "Database", "Orders Api", "GET", "db://device/last"));
        capture.AddRange(Call("09:47:30.000", "09:47:30.050", "session", "Database", "seeder", "POST", "db://orders/insert?seed=last"));

        var result = Ingest("Bounds", capture, BrowserTests(), StartUp, [Device]);

        Assert.Equal(18, result.SetupRecords);
        Assert.Equal(new Dictionary<string, int> { ["device"] = 8 }, result.OtherRunRecords);
        Assert.Null(Dropped(result));
    }

    [Fact]
    public void A_call_inside_the_run_window_and_the_setup_window_stays_with_the_run()
    {
        // The setup window reaches 5 s into the run: the login and b1's calls are the run's, attributed as before.
        var overlapping = new SetupWindow(At("09:47:00.000"), At("10:00:05.000"));

        var result = Ingest("RunWins", Capture(), BrowserTests(), overlapping, [Device]);

        Assert.Equal(16, result.SetupRecords);
        var requests = RequestsByScenario(result);
        Assert.Equal(["http://orders-api/orders", "db://orders/find?customer=1"], requests["b1"]);
        Assert.Equal(["http://orders-api/login"], requests["outside-any-test"]);
        Assert.Equal(8, requests["stack-start-up"].Length);
    }

    // ------------------------------------------------------------------------------------------
    // Facts 15 to 17: the setup scenario
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void The_setup_scenario_is_a_feature_of_its_own_drawn_first_not_a_test_and_all_setup()
    {
        // ResultWhenUnknown Failed: the start-up never started or ended, and is still no failure.
        var result = Ingest("Scenario", Capture(), BrowserTests(), StartUp, [Device], resultWhenUnknown: ExecutionResult.Failed);

        Assert.Equal(["Stack start-up", "Orders", "Ingested"], result.Features.Select(f => f.DisplayName));
        var setup = Assert.Single(result.Features[0].Scenarios);
        Assert.Equal("stack-start-up", setup.Id);
        Assert.Equal("Stack start-up", setup.DisplayName);
        Assert.True(setup.NotATest);
        Assert.Equal(ExecutionResult.Passed, setup.Result);
        Assert.False(setup.ResultDefaulted);
        Assert.DoesNotContain(result.Diagnostics, d => d.Kind == DiagnosticKind.ResultDefaulted);

        var interactions = ReportFile.Items(Scenarios(result).Single(s => s.GetProperty("id").GetString() == "stack-start-up"), "httpInteractions").ToArray();
        Assert.Equal(16, interactions.Length);
        Assert.All(interactions, i => Assert.Equal("Setup", i.GetProperty("phase").GetString()));
    }

    [Fact]
    public void The_setup_scenario_takes_the_name_and_id_it_is_given()
    {
        var result = Ingest("Named", Capture(), BrowserTests(), StartUp with { ScenarioName = "Compose up", ScenarioId = "compose-up" }, [Device]);

        Assert.Equal("Compose up", result.Features[0].DisplayName);
        Assert.Equal(8, RequestsByScenario(result)["compose-up"].Length);
    }

    [Fact]
    public void The_setup_scenario_is_not_folded_not_asked_about_not_unattributed_and_not_the_runs_time()
    {
        var asked = new List<string>();
        bool Ask(InteractionRecord record)
        {
            asked.Add(record.Uri);
            return false;
        }

        var with = Ingest("Exempt", Capture(), BrowserTests(), StartUp, [Device], dropUnattributed: Ask);
        var askedWith = asked.Distinct().ToArray();
        asked.Clear();
        var without = Ingest("Plain", Capture(), BrowserTests(), dropUnattributed: Ask);

        // The window is applied, so what follows holds of a run that keeps the start-up, not of one that ignored it.
        Assert.Equal(16, with.SetupRecords);
        Assert.Contains("stack-start-up", RequestsByScenario(with).Keys);
        // Harness P3 E: the predicate is asked about the login only.
        Assert.Equal(["http://orders-api/login"], askedWith);
        Assert.Equal(["http://orders-api/login"], asked.Distinct());
        Assert.Equal("2 interaction record(s) could not be attributed to a test.",
            with.Diagnostics.Single(d => d.Kind == DiagnosticKind.UnattributedInteractions).Message);
        // The run lasts what it lasted: 9.5 s, the start-up's 13 minutes left out (harness P1 A and P3 A').
        Assert.Equal(without.Start, with.Start);
        Assert.Equal(without.End, with.End);
        Assert.Equal(At("10:00:00.500").UtcDateTime, with.Start);
        Assert.Equal(At("10:00:10.000").UtcDateTime, with.End);
    }

    [Fact]
    public void With_SeparateSetup_the_setup_scenario_is_drawn_inside_the_Setup_partition()
    {
        string Diagram(IngestResult result) =>
            ReportPayloadText.Of(Scenarios(result).Single(s => s.GetProperty("id").GetString() == "stack-start-up").GetProperty("diagrams")[0])!;

        var separated = Diagram(Ingest("Separated", Capture(), BrowserTests(), StartUp, [Device], separateSetup: true));
        var plain = Diagram(Ingest("Unseparated", Capture(), BrowserTests(), StartUp, [Device]));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(separated, @"(?m)^partition "));
        var partition = IngestSetupBoundaryTests.PartitionOf(separated);
        foreach (var seed in Enumerable.Range(0, 6))
            Assert.Contains($"seed={seed}", partition);
        Assert.Contains("health", partition);
        Assert.DoesNotMatch(@"(?m)^partition ", plain);
    }

    // ------------------------------------------------------------------------------------------
    // Fact 18: an open end
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void An_open_end_is_closed_at_the_next_declared_runs_start_exclusively()
    {
        var capture = Capture();
        // A call that began the instant the device run began is the device run's; one after the device run is nobody's.
        capture.AddRange(Call("10:00:11.600", "10:00:11.650", "session", "Database", "Orders Api", "GET", "db://boundary/find"));
        capture.AddRange(Call("10:00:30.000", "10:00:30.050", "session", "Database", "Orders Api", "GET", "db://late/find"));

        var result = Ingest("OpenEnd", capture, BrowserTests(endMarker: false), StartUp, [Device]);

        Assert.Equal(new Dictionary<string, int> { ["device"] = 8 }, result.OtherRunRecords);
        Assert.Equal("2 interaction record(s) outside the run window dropped that no other window explains: 0 before the run began, "
                     + "2 after it ended. The run window: 2026-10-21T10:00:00.0000000+00:00 → 2026-10-21T10:00:11.6000000+00:00, where device began.",
            Dropped(result));
        // b2's late call began before the device run did: it is the browser run's, kept in b2.
        Assert.Contains("http://orders-api/audit", RequestsByScenario(result)["b2"]);
    }

    [Fact]
    public void ResolveRunWindow_closes_an_open_end_one_tick_before_the_next_run_and_is_unchanged_without_one()
    {
        var tests = BrowserTests(endMarker: false);

        var closed = IngestPipeline.ResolveRunWindow(new IngestRequest { OtherRunWindows = [Device] }, tests);
        var open = IngestPipeline.ResolveRunWindow(new IngestRequest { DropOutsideRunWindow = true }, tests);
        var ended = IngestPipeline.ResolveRunWindow(new IngestRequest { OtherRunWindows = [Device] }, BrowserTests());
        // A run declared to begin before this one closes nothing.
        var before = IngestPipeline.ResolveRunWindow(new IngestRequest { OtherRunWindows = [new OtherRunWindow("before", At("09:59:00.000"), At("09:59:30.000"))] }, tests);

        Assert.Equal(At("10:00:00.000"), closed!.Value.Start);
        Assert.Equal(At("10:00:11.600").AddTicks(-1), closed.Value.End);
        Assert.Equal(At("10:00:00.000"), open!.Value.Start);
        Assert.Null(open.Value.End);
        Assert.Equal(At("10:00:11.000"), ended!.Value.End);
        Assert.Null(before!.Value.End);
        // A run that cannot be applied (no name, an end before its start) closes nothing either.
        var blank = IngestPipeline.ResolveRunWindow(new IngestRequest { OtherRunWindows = [new OtherRunWindow(" ", At("10:00:11.600"), At("10:00:21.000"))] }, tests);
        var inverted = IngestPipeline.ResolveRunWindow(new IngestRequest { OtherRunWindows = [new OtherRunWindow("device", At("10:00:21.000"), At("10:00:11.600"))] }, tests);
        Assert.Null(blank!.Value.End);
        Assert.Null(inverted!.Value.End);
    }

    // ------------------------------------------------------------------------------------------
    // Facts 19 and 20: what the count says
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void When_the_windows_explain_everything_there_is_no_entry_and_no_line_in_a_query_answer()
    {
        var result = Ingest("Explained", Capture(losses: false), BrowserTests(), StartUp, [Device]);

        Assert.Null(Dropped(result));
        Assert.DoesNotContain("outside the run window", Query(result, "summary"));
    }

    [Fact]
    public void Without_windows_the_entry_counts_as_before_with_its_figures_first()
    {
        var result = Ingest("NoWindows", Capture(), BrowserTests());

        Assert.Equal("26 interaction record(s) outside the run window dropped: 16 before the run began, 10 after it ended "
                     + "(2 name a test in this run's tests file). The run window: 2026-10-21T10:00:00.0000000+00:00 → "
                     + "2026-10-21T10:00:11.0000000+00:00.", Dropped(result));
        Assert.Equal(0, result.SetupRecords);
        Assert.Empty(result.OtherRunRecords);
        // The query keeps 160 characters of it: both figures and what they count.
        Assert.Contains("26 interaction record(s) outside the run window dropped: 16 before the run began, 10 after it ended", Query(result, "summary"));
    }

    // ------------------------------------------------------------------------------------------
    // Facts 21 and 24: windows that cannot be windows
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void A_setup_window_that_ends_before_it_starts_is_not_applied()
    {
        var result = Ingest("SetupInverted", Capture(losses: false), BrowserTests(), new SetupWindow(At("09:47:30.000"), At("09:47:00.000")), [Device]);

        Assert.Equal(0, result.SetupRecords);
        Assert.Equal(["SetupWindow: the setup window ends before it starts (2026-10-21T09:47:30.0000000+00:00 → "
                      + "2026-10-21T09:47:00.0000000+00:00); it was not applied."], Others(result));
        Assert.Equal("16 interaction record(s) outside the run window dropped that no other window explains: 16 before the run began, "
                     + "0 after it ended. The run window: 2026-10-21T10:00:00.0000000+00:00 → 2026-10-21T10:00:11.0000000+00:00.", Dropped(result));
    }

    [Fact]
    public void A_window_that_is_not_applied_explains_nothing()
    {
        // The only window given cannot be one: the entry reads as it does with none.
        var result = Ingest("NoneApplied", Capture(losses: false), BrowserTests(), new SetupWindow(At("09:47:30.000"), At("09:47:00.000")));

        Assert.Equal("22 interaction record(s) outside the run window dropped: 16 before the run began, 6 after it ended. The run window: "
                     + "2026-10-21T10:00:00.0000000+00:00 → 2026-10-21T10:00:11.0000000+00:00.", Dropped(result));
    }

    [Fact]
    public void A_setup_window_with_the_folds_id_is_not_applied()
    {
        var result = Ingest("SetupFoldId", Capture(losses: false), BrowserTests(), StartUp with { ScenarioId = "outside-any-test" }, [Device]);

        Assert.Equal(0, result.SetupRecords);
        Assert.Equal(["SetupWindow: its scenario id \"outside-any-test\" is the fold's (FoldUnknownTestsInto); it was not applied."], Others(result));
    }

    [Fact]
    public void A_setup_window_with_a_tests_id_is_not_applied_and_the_test_keeps_its_verdict()
    {
        // Applied, the start-up's calls joined the test, and the test was then made the start-up: passed, and not a test, its
        // failure gone from every count.
        var tests = BrowserTests().Select(t => t.TestId == "b2" && t.Event == "end" ? t with { Status = "failed" } : t).ToArray();
        var result = Ingest("SetupTestId", Capture(losses: false), tests, StartUp with { ScenarioId = "b2" }, [Device]);

        Assert.Equal(0, result.SetupRecords);
        Assert.Equal(["SetupWindow: its scenario id \"b2\" is a test's in the tests records; it was not applied."], Others(result));
        var b2 = Scenarios(result).Single(s => s.GetProperty("id").GetString() == "b2");
        Assert.Equal("Failed", b2.GetProperty("result").GetString());
        Assert.False(b2.TryGetProperty("notATest", out _));
        Assert.DoesNotContain(RequestsByScenario(result)["b2"], uri => uri.StartsWith("db://orders/insert", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Stack start-up", " ")]
    [InlineData(" ", "stack-start-up")]
    public void A_setup_window_with_a_blank_name_or_id_is_not_applied(string name, string id)
    {
        var result = Ingest("SetupBlank" + Guid.NewGuid().ToString("N")[..4], Capture(losses: false), BrowserTests(),
            StartUp with { ScenarioName = name, ScenarioId = id }, [Device]);

        Assert.Equal(0, result.SetupRecords);
        Assert.Equal(["SetupWindow: its scenario name and id must not be blank; it was not applied."], Others(result));
    }

    [Fact]
    public void Another_run_that_ends_before_it_starts_or_has_a_blank_name_is_not_applied()
    {
        var inverted = new OtherRunWindow("device", At("10:00:21.000"), At("10:00:11.600"));
        var blank = new OtherRunWindow(" ", At("10:00:11.600"), At("10:00:21.000"));

        var result = Ingest("OthersInvalid", Capture(losses: false), BrowserTests(), StartUp, [inverted, blank]);

        Assert.Empty(result.OtherRunRecords);
        Assert.Equal(
        [
            "OtherRunWindows: the run \"device\" ends before it starts (2026-10-21T10:00:21.0000000+00:00 → 2026-10-21T10:00:11.6000000+00:00); it was not applied.",
            "OtherRunWindows: a run with a blank name (2026-10-21T10:00:11.6000000+00:00 → 2026-10-21T10:00:21.0000000+00:00) was not applied.",
        ], Others(result));
        // The device run's 6 records are dropped as they were before.
        Assert.StartsWith("6 interaction record(s) outside the run window dropped that no other window explains: 0 before the run began, 6 after it ended.",
            Dropped(result));
    }

    [Fact]
    public void With_no_run_window_the_setup_window_and_other_runs_are_not_applied_and_the_entry_says_so()
    {
        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = Capture(), Options = Options("NoWindow"), SetupWindow = StartUp, OtherRunWindows = [Device],
        });

        Assert.Equal(0, result.SetupRecords);
        Assert.Empty(result.OtherRunRecords);
        Assert.Equal(["DropOutsideRunWindow: no run window could be derived (no tests records and no explicit start); nothing was dropped, "
                      + "and the setup window and the other run were not applied."], Others(result));
    }

    [Theory]
    [InlineData(false, 1, ", and the other run was not applied.")]
    [InlineData(false, 2, ", and the other runs were not applied.")]
    [InlineData(true, 0, ", and the setup window was not applied.")]
    [InlineData(true, 2, ", and the setup window and the other runs were not applied.")]
    public void With_no_run_window_the_entry_names_each_window_not_applied(bool setup, int runs, string named)
    {
        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = Capture(), Options = Options("NoWindow" + Guid.NewGuid().ToString("N")[..4]),
            SetupWindow = setup ? StartUp : null, OtherRunWindows = new[] { Device, Browser }[..runs],
        });

        Assert.Equal(["DropOutsideRunWindow: no run window could be derived (no tests records and no explicit start); nothing was dropped"
                      + named], Others(result));
    }

    [Fact]
    public void The_same_name_given_twice_is_one_run()
    {
        // The device run declared in two parts: its calls are left to one run, counted once under its name.
        var result = Ingest("SameName", Capture(), BrowserTests(), StartUp,
            [Device with { End = At("10:00:14.000") }, Device with { Start = At("10:00:14.000") }]);

        Assert.Equal(["device"], result.OtherRunRecords.Keys);
        Assert.Equal(6, result.OtherRunRecords["device"]);
    }

    [Fact]
    public void With_an_inverted_run_window_the_setup_window_is_not_applied_and_the_entry_says_so()
    {
        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = Capture(), TestRecords = BrowserTests(), Options = Options("InvertedRun"), SetupWindow = StartUp,
            RunStartedAt = At("10:00:30.000"),
        });

        Assert.Equal(0, result.SetupRecords);
        Assert.Equal(["DropOutsideRunWindow: the run window ends before it starts (2026-10-21T10:00:30.0000000+00:00 → "
                      + "2026-10-21T10:00:11.0000000+00:00); nothing was dropped, and the setup window was not applied."], Others(result));
    }

    // ------------------------------------------------------------------------------------------
    // Facts 25, 26 and 29: the start-up's calls among the run's
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void The_start_ups_slow_first_call_of_a_shape_is_the_warm_up_and_the_tests_later_call_is_not()
    {
        var capture = Capture();
        // The stack's own first GET /orders took 3.5 s; b1's, 13 minutes later, 0.3 s; b2's, later still, 20 ms. Without the
        // start-up's call, b1's would be the run's first of its shape, and 15 times b2's: a warm-up.
        capture.AddRange(Call("09:47:26.000", "09:47:29.500", "session", "Orders Api", "compose", "GET", "http://orders-api/orders"));
        capture.AddRange(Call("10:00:08.000", "10:00:08.020", "b2", "Orders Api", "browser", "GET", "http://orders-api/orders"));

        var scenarios = Scenarios(Ingest("WarmUp", capture, BrowserTests(), StartUp, [Device]));

        var setup = scenarios.Single(s => s.GetProperty("id").GetString() == "stack-start-up");
        Assert.Equal(3.5, setup.GetProperty("warmUpSeconds").GetDouble());
        Assert.False(scenarios.Single(s => s.GetProperty("id").GetString() == "b1").TryGetProperty("warmUpSeconds", out _));
    }

    [Fact]
    public void A_start_up_call_captured_from_both_sides_lands_in_the_setup_scenario_and_merges_there()
    {
        var capture = Capture();
        capture.AddRange(Call("09:47:10.000", "09:47:10.040", "session", "Database", "seeder", "POST", "db://orders/insert?seed=twin", capturedBy: "wire"));
        capture.AddRange(Call("09:47:10.001", "09:47:10.040", "session", "Database", "seeder", "POST", "db://orders/insert?seed=twin", capturedBy: "span"));

        var result = Ingest("Twins", capture, BrowserTests(), StartUp, [Device], merge: true);

        var setup = Scenarios(result).Single(s => s.GetProperty("id").GetString() == "stack-start-up");
        var twin = Assert.Single(ReportFile.Items(setup, "httpInteractions"),
            i => i.GetProperty("type").GetString() == "Request" && i.GetProperty("uri").GetString() == "db://orders/insert?seed=twin");
        Assert.Contains(twin.GetProperty("headers").EnumerateArray(),
            h => h.GetProperty("key").GetString() == InteractionMerger.CapturedByHeader && h.GetProperty("value").GetString() == "wire + span");
        Assert.Equal(20, result.SetupRecords);
    }

    [Fact]
    public void A_raw_marker_in_the_setup_window_is_not_kept_and_is_counted_as_before()
    {
        // Each kind of marker a capture carries: a step bar, an assertion note and a Setup/Action boundary.
        var capture = Capture();
        capture.Add(InteractionRecord.StepMarker("session", "a step of nobody's", At("09:47:15.000"), keyword: "Given"));
        capture.Add(InteractionRecord.AssertionMarker("session", "an assertion of nobody's", passed: true, At("09:47:16.000"), null));
        capture.Add(new InteractionRecord
        {
            Kind = InteractionRecord.Kinds.Marker, MarkerKind = nameof(DiagramMarkerKind.Phase), Type = "Request", Uri = "http://override.com/",
            ServiceName = "", CallerName = "", TestId = "session", Timestamp = At("09:47:17.000"),
        });

        var result = Ingest("Marker", capture, BrowserTests(), StartUp, [Device], separateSetup: true);

        Assert.Equal(16, result.SetupRecords);
        Assert.Equal("7 interaction record(s) outside the run window dropped that no other window explains: 3 before the run began, "
                     + "4 after it ended (2 name a test in this run's tests file). The run window: 2026-10-21T10:00:00.0000000+00:00 → "
                     + "2026-10-21T10:00:11.0000000+00:00.", Dropped(result));
        var setup = Scenarios(result).Single(s => s.GetProperty("id").GetString() == "stack-start-up");
        var source = ReportPayloadText.Of(setup.GetProperty("diagrams")[0]);
        Assert.DoesNotContain("a step of nobody", source);
        Assert.DoesNotContain("an assertion of nobody", source);
    }

    // ------------------------------------------------------------------------------------------
    // Fact 28: with Cucumber messages
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void With_Cucumber_messages_the_setup_feature_is_still_first_in_the_model_and_on_the_page()
    {
        var firstMarker = CucumberFixtures.Build().Markers.Where(m => m.Timestamp is not null).Min(m => m.Timestamp!.Value);
        var startUp = new SetupWindow(firstMarker.AddMinutes(-15), firstMarker.AddMinutes(-10));
        var at = firstMarker.AddMinutes(-13);
        InteractionRecord[] seed =
        [
            new() { Type = "Request", TestId = "session", RequestResponseId = "seed-1", Method = "POST", Uri = "db://orders/insert?seed=gherkin", ServiceName = "Database", CallerName = "seeder", Timestamp = at },
            new() { Type = "Response", TestId = "session", RequestResponseId = "seed-1", Method = "POST", Uri = "db://orders/insert?seed=gherkin", ServiceName = "Database", CallerName = "seeder", StatusCode = "OK", Timestamp = at.AddMilliseconds(40) },
        ];

        var result = IngestPipeline.Run(new IngestRequest
        {
            Interactions = seed, CucumberMessagesFiles = [CucumberFixtures.MessagesPath], Options = Options("Cucumber"), SetupWindow = startUp,
        });

        Assert.Equal(2, result.SetupRecords);
        Assert.Equal(["Stack start-up", CucumberFixtures.DemoFeature, CucumberFixtures.RetryFeature], result.Features.Select(f => f.DisplayName));
        var html = ReportHtml.Read(result.TestRunReportHtml);
        var setupAt = html.IndexOf(">Stack start-up<", StringComparison.Ordinal);
        Assert.True(setupAt >= 0 && setupAt < html.IndexOf(">" + CucumberFixtures.DemoFeature + "<", StringComparison.Ordinal));
    }
}
