using System.Diagnostics;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Kronikol.History;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A title on every control of the report (5.0.0, plans/OPTION_C_PLAN.md S12, D49), written by the generator where the
/// control is: each tab of the report bar says what it shows; a payload control says what it does and to which diagrams,
/// every sequence diagram in the report on the report bar and the scenario's own on a scenario's bar; the filter box's
/// controls say what they do, Clear All naming the search and every row the box has; a chip gives its count and then what
/// a click does; a section's heading says what it holds, and in a tab, where it is nothing to click, not that a click
/// shows or hides it; a link says where it goes or what it opens. The scripts title only what they draw or make live: a
/// failed drawing's Raw PlantUML, the internal-flow popup's close and its view tabs, and a composite step's summary,
/// given its title when it is first pointed at or focused, as its status icon is (plans/REPORT_FORMAT_PLAN.md R2).
/// ControlTitlesAuditTests holds what the scripts do in a browser.
/// </summary>
public class ControlTitlesTests : IDisposable
{
    private const string Suite = "ControlTitles";
    private const string SourceName = "Kronikol.Tests.ControlTitles";
    private static readonly DateTimeOffset At = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private const string InTheReport = "every sequence diagram in the report";
    private const string InTheScenario = "this scenario's sequence diagrams";
    private const string Showing = "The scenarios and features the filters leave showing";
    private const string ChipClicks = "Click: only these. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once.";
    private const string HistoryWords = "This run against the earlier runs on the same branch: what is failing, flaky, fixed, slower, new or gone";
    private const string DiagnosticsWords = "What Kronikol noticed while building this report, such as calls it could not put under a scenario";

    /// <summary>Every control the audits look at: a button, a link, a summary, a select, an input, a tab.</summary>
    internal const string Controls = "button, a[href], summary, select, input, [role=tab], [role=button]";

    /// <summary>
    /// The headings whose own text names what they hold, which carry no title: a feature's card, a rule's, an outline's
    /// group, the failure clusters and each cluster, the background calls.
    /// </summary>
    internal const string Headings = "details.feature > summary, details.rule > summary, details.scenario-parameterized > summary, "
        + "details.failure-clusters > summary, details.failure-cluster > summary, details.background-calls > summary";

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-control-titles").FullName;
    private readonly ActivitySource _source = new(SourceName);
    private readonly ActivityListener _listener;
    private readonly List<Activity> _spans = [];

    public ControlTitlesTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var span in _spans) span.Dispose();
        _listener.Dispose();
        _source.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // A sequence diagram with all that a payload control acts on: a JSON payload under its headers, an assertion, step bars
    // with a When (so a setup before it), a database and a queue.
    private static readonly string Busy = string.Join("\n",
        "@startuml",
        "actor \"Caller\" as caller",
        "participant \"OrderService\" as svc",
        "database \"OrderDb\" as db",
        "queue \"OrderEvents\" as events",
        StepBarPlantUml.Build("Given an order request"),
        "caller -> svc : POST /api/orders",
        "note left",
        "<color:gray>[content-type=application/json]</color>",
        "",
        "{",
        "  \"id\": 42",
        "}",
        "end note",
        StepBarPlantUml.Build("When the order is placed"),
        "svc -> db : INSERT INTO orders",
        "svc -> events : OrderPlaced",
        "svc --> caller : 200 OK",
        "note right <<assertionNote>>: ✓ status code should be OK",
        "@enduml");

    private const string Plain = "@startuml\nactor \"Caller\" as caller\nparticipant \"OrderService\" as svc\ncaller -> svc : GET /api/orders/1\nsvc --> caller : 200 OK\n@enduml";

    private const string ComponentSource = "@startuml\nrectangle \"Caller\" as caller\nrectangle \"OrderService\" as svc\ncaller --> svc\n@enduml";

    /// <summary>
    /// One feature with a control of every kind on it: a happy path with a tag, a category, durations, a background, a
    /// composite step, attachments of a file, a link and images on the scenario and a step; two failures that share their
    /// error (a failure cluster); a skipped and a bypassed scenario; and one whose calls have spans, so its diagrams have
    /// tabs.
    /// </summary>
    private static Feature[] Shop() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "pay", DisplayName = "Pays for an order", Result = ExecutionResult.Passed, IsHappyPath = true,
                    Duration = TimeSpan.FromMilliseconds(1200), Tags = ["fast", "smoke"],
                    Attachments = [new FileAttachment("order.json", "attachments/order.json"), new FileAttachment("screen.png", "attachments/screen.png", "image/png")],
                    BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the shop is open", Status = ExecutionResult.Passed }],
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Given", Text = "an order request", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(100),
                            Attachments =
                            [
                                new FileAttachment("request.txt", "attachments/request.txt"),
                                new FileAttachment("Trace", "https://example.com/trace/1"),
                                new FileAttachment("step.png", "attachments/step.png", "image/png")
                            ]
                        },
                        new ScenarioStep
                        {
                            Keyword = "When", Text = "the order is placed", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(900),
                            SubSteps =
                            [
                                new ScenarioStep { Keyword = "When", Text = "the card is charged", Status = ExecutionResult.Passed },
                                new ScenarioStep { Keyword = "And", Text = "the stock is held", Status = ExecutionResult.Passed }
                            ]
                        },
                        new ScenarioStep { Keyword = "Then", Text = "the order is confirmed", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(200) }
                    ]
                },
                new Scenario
                {
                    Id = "expired", DisplayName = "Pays with an expired card", Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(300),
                    ErrorMessage = "Expected 200 but got 402",
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "the payment is refused", Status = ExecutionResult.Failed }]
                },
                new Scenario
                {
                    Id = "blocked", DisplayName = "Pays with a blocked card", Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(250),
                    ErrorMessage = "Expected 200 but got 402",
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "the payment is refused", Status = ExecutionResult.Failed }]
                },
                new Scenario { Id = "later", DisplayName = "Pays later", Result = ExecutionResult.Skipped, Duration = TimeSpan.FromMilliseconds(5) },
                new Scenario { Id = "voucher", DisplayName = "Pays by voucher", Result = ExecutionResult.Bypassed, Duration = TimeSpan.FromMilliseconds(40) },
                new Scenario { Id = "ships", DisplayName = "Ships an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(500) }
            ]
        }
    ];

    private static DiagramAsCode[] ShopDiagrams() =>
    [
        new("pay", "", Busy), new("expired", "", Plain), new("blocked", "", Plain), new("ships", "", Plain)
    ];

    private static CiMetadata Ci() => new(CiEnvironment.GitHubActions, "322", "main", "0ee43e8c0b5d3e85a4a099a20ee4ce37305b6b3f",
        "https://github.com/example/shop/actions/runs/7", "example/shop", "7");

    private static DiagnosticEntry[] OneDiagnostic() => [new DiagnosticEntry(DiagnosticKind.CaptureDegraded, "tap-a: 1 oversize payload skipped")];

    /// <summary>The whole-test flow of <paramref name="testId"/>: a server span and a span under it.</summary>
    private Dictionary<string, InternalFlowSegment> Flow(string testId)
    {
        var start = At.UtcDateTime.AddSeconds(-30);
        Activity.Current = null;
        var root = _source.StartActivity("HTTP POST /api/orders", ActivityKind.Server)!;
        root.SetStartTime(start);
        root.SetEndTime(start.AddMilliseconds(400));
        var child = _source.StartActivity("SELECT orders", ActivityKind.Internal, new ActivityContext(root.TraceId, root.SpanId, ActivityTraceFlags.Recorded))!;
        child.SetStartTime(start.AddMilliseconds(20));
        child.SetEndTime(start.AddMilliseconds(300));
        Activity.Current = null;
        _spans.AddRange([root, child]);
        return new Dictionary<string, InternalFlowSegment>
        {
            [$"iflow-test-{testId}"] = new(Guid.Empty, RequestResponseType.Request, testId, start, start.AddMilliseconds(400), [root, child])
        };
    }

    /// <summary>Three earlier runs in which every scenario passed, and this run read against them.</summary>
    private HistoryVerdicts Verdicts(Feature[] features)
    {
        var ledgerPath = Path.Combine(_dir, $"ledger-{Guid.NewGuid():N}", "history.jsonl");
        var (roster, run) = HistoryRunBuilder.Build(features, [], Suite, null, At, new HistoryBuildOptions(), "test:99:1");
        for (var i = 0; i < 3; i++)
        {
            var results = new string('P', run.Results.Length);
            var prior = run with
            {
                Id = $"test:{i + 1}:1", At = At.AddHours(i - 3), Commit = $"c{i + 1:D6}",
                Results = results, Attempts = new string('-', results.Length),
                Durations = Enumerable.Repeat<int?>(100, results.Length).ToArray(),
                Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
                ErrorText = new Dictionary<string, string>()
            };
            Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(ledgerPath, roster, prior, "3.11.0").Outcome);
        }
        var ledger = HistoryLedgerReader.Read(ledgerPath, 50).Ledger!;
        return HistoryAnalyzer.Analyse(ledger, roster, run, new HistoryAnalysisOptions { MinRuns = 2 });
    }

    private string Write(string name, Feature[] features, DiagramAsCode[] diagrams, Dictionary<string, InternalFlowSegment>? segments = null,
        WholeTestFlowVisualization visualization = WholeTestFlowVisualization.Both, HistoryVerdicts? history = null,
        IReadOnlyList<DiagnosticEntry>? diagnostics = null, CiMetadata? ci = null, string? component = null, RequestResponseLog[]? logs = null)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features, At.UtcDateTime.AddMinutes(-1), At.UtcDateTime, null, Path.Combine(_dir, name), "Control titles", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: segments is not null, wholeTestSegments: segments, trackedLogs: logs,
            wholeTestVisualization: segments is null ? WholeTestFlowVisualization.None : visualization,
            ciMetadata: ci, componentDiagramPlantUml: component, diagnostics: diagnostics, showReportDiagnostics: diagnostics is not null,
            separateBackgroundSteps: true, suite: Suite, history: history, showHistorySection: history is not null);
        return File.ReadAllText(path);
    }

    /// <summary>The report of <see cref="Shop"/> with every tab: the timeline, the components, history and diagnostics.</summary>
    private (IDocument Page, Feature[] Features) Everything()
    {
        var features = Shop();
        var html = Write($"ControlTitles_{Guid.NewGuid():N}.html", features, ShopDiagrams(), segments: Flow("ships"),
            history: Verdicts(features), diagnostics: OneDiagnostic(), ci: Ci(), component: ComponentSource);
        return (Parse(html), features);
    }

    private IDocument Labs(Feature[] features) =>
        Parse(LabsReportGenerator.Build(new LabsPage("Control titles", features, Suite, Verdicts(features), OneDiagnostic(), "TestRunReport.html")));

    private static IDocument Parse(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

    private static IElement One(IParentNode scope, string selector)
    {
        var found = scope.QuerySelector(selector);
        Assert.True(found is not null, $"nothing matches {selector}");
        return found!;
    }

    private static string? TitleOf(IParentNode scope, string selector) => One(scope, selector).GetAttribute("title");

    /// <summary>A scenario's element, found by the name its copy button carries.</summary>
    private static IElement Scenario(IDocument page, string name) =>
        page.QuerySelectorAll("details.scenario").Single(s =>
            s.QuerySelector(":scope > summary .copy-scenario-name") is { } copy && copy.GetAttribute("data-scenario-name") == name);

    private static string Describe(IElement e) =>
        $"{e.LocalName}.{e.ClassName} \"{Regex.Replace(e.TextContent, @"\s+", " ").Trim()}\"";

    [Fact]
    public void Each_tab_of_the_report_bar_says_what_it_shows()
    {
        var (page, _) = Everything();

        Assert.Equal("The features and their scenarios, with the filters applied", TitleOf(page, "#tab-features"));
        Assert.Equal("Every scenario as a bar, slowest first", TitleOf(page, "#tab-timeline"));
        Assert.Equal("The services, databases and queues the scenarios called, drawn as one diagram", TitleOf(page, "#tab-components"));
        Assert.Equal(HistoryWords, TitleOf(page, "#tab-history"));
        Assert.Equal(DiagnosticsWords, TitleOf(page, "#tab-diagnostics"));
    }

    [Fact]
    public void The_timeline_tab_and_the_seconds_field_name_the_warm_up_only_where_a_scenario_paid_one()
    {
        var paid = WarmUp(firstMs: 600);
        Assert.Equal("Every scenario as a bar, slowest first, with the first-call warm-up it paid", TitleOf(paid, "#tab-timeline"));
        Assert.Equal("Seconds: scenarios taking at least this long, not counting first-call warm-up", TitleOf(paid, "#duration-threshold"));

        // a 5 ms first call is no warm-up
        var none = WarmUp(firstMs: 5);
        Assert.Equal("Every scenario as a bar, slowest first", TitleOf(none, "#tab-timeline"));
        Assert.Equal("Seconds: scenarios taking at least this long", TitleOf(none, "#duration-threshold"));
    }

    private IDocument WarmUp(double firstMs)
    {
        var (features, logs) = WarmUpHtmlTests.Marked(firstMs);
        return Parse(Write($"ControlTitles_WarmUp_{Guid.NewGuid():N}.html", features, [], logs: logs.ToArray()));
    }

    [Fact]
    public void The_report_bars_payload_controls_say_they_act_on_every_sequence_diagram_in_the_report()
    {
        var (page, _) = Everything();

        PayloadControlsSay(One(page, "#display-settings"), InTheReport);
    }

    [Fact]
    public void A_scenarios_payload_controls_say_they_act_on_its_own_sequence_diagrams()
    {
        var (page, _) = Everything();
        var bars = page.QuerySelectorAll(".scenario-settings").ToArray();

        Assert.Equal(4, bars.Length);
        Assert.All(bars, bar => PayloadControlsSay(bar, InTheScenario));
    }

    private static void PayloadControlsSay(IElement bar, string diagrams)
    {
        Assert.Equal($"How the payloads in {diagrams} are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit",
            TitleOf(bar, ".details-radio-label"));
        Assert.Equal(
            [
                $"expanded: The payloads in {diagrams} show in full; a long one scrolls in its box",
                $"collapsed: The payloads in {diagrams} fold to one line each; a box's + button opens it",
                $"truncated: Payloads in {diagrams} longer than the line limit beside this are cut at that many lines"
            ],
            bar.QuerySelectorAll(".details-radio-btn[data-state]").Select(b => $"{b.GetAttribute("data-state")}: {b.GetAttribute("title")}"));
        var lines = One(bar, "select.truncate-lines-select");
        Assert.Equal($"How many lines a cut payload box keeps in {diagrams} (with Truncated picked)", lines.GetAttribute("title"));
        Assert.Equal("Payload length", lines.GetAttribute("aria-label"));
        Assert.Equal(
            [
                $"headers: Show or hide the headers at the top of each payload in {diagrams}",
                $"assertions: Show or hide the boxes with the test's assertions in {diagrams}",
                $"steps: Show or hide the bars marking where each step starts in {diagrams}",
                $"databases: Show or hide the databases and their calls in {diagrams}",
                $"events: Show or hide the calls to and from message brokers (the participants drawn as queues), with their payloads, in {diagrams}",
                $"setup: Show or hide the setup in {diagrams}: everything before the first When step, or the Setup section where the report keeps setup apart"
            ],
            bar.QuerySelectorAll(".toggle-btn").Select(b => $"{b.GetAttribute("data-toggle")}: {b.GetAttribute("title")}"));
        Assert.Equal($"How the payloads in {diagrams} are written: JSON as captured, or the same data as YAML", TitleOf(bar, "select.note-format-select"));
        Assert.Equal($"How wide the payloads are in {diagrams}. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.",
            TitleOf(bar, "select.note-width-select"));
    }

    [Fact]
    public void The_filter_box_says_what_each_of_its_controls_does()
    {
        var (page, _) = Everything();
        var box = One(page, "#filtering-box");

        Assert.Equal(Showing, TitleOf(box, ".filter-readout"));
        Assert.Equal("Download a copy of this report holding only the features the filters show", TitleOf(box, ".export-btn[onclick='export_html()']"));
        Assert.Equal("Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds",
            TitleOf(box, ".export-btn[onclick='export_csv()']"));
        Assert.Equal("Search names, steps, tags and diagram text. Combine words with && (and), || (or) and !! (not); @tag and $status pick by tag or status, "
                     + "dep:, duration: and is:happy by dependency, time and happy path; ? shows the syntax", TitleOf(box, "#searchbar"));
        Assert.Equal("How to write a search: words, \"phrases\", @tags, $statuses, dep:, duration:, is:happy, &&, ||, !! and parentheses",
            TitleOf(box, ".search-help-toggle"));
        Assert.Equal("Show or hide the filters", TitleOf(page, ".mobile-filter-toggle"));
        Assert.Equal(
            [
                "Passed: Show only the scenarios that passed; pick more statuses to show them together",
                "Failed: Show only the scenarios that failed; pick more statuses to show them together",
                "Skipped: Show only the scenarios that were skipped; pick more statuses to show them together",
                "Bypassed: Show only the scenarios that were bypassed; pick more statuses to show them together"
            ],
            box.QuerySelectorAll(".status-toggle").Select(b => $"{b.GetAttribute("data-status")}: {b.GetAttribute("title")}"));
        Assert.Equal("Seconds: scenarios taking at least this long", TitleOf(box, "#duration-threshold"));
        Assert.Equal(
            ["off: Happy-path scenarios and the rest", "in: Only the scenarios tagged as happy paths", "out: Leave out the scenarios tagged as happy paths"],
            box.QuerySelectorAll(".happy-seg").Select(b => $"{b.GetAttribute("data-hp")}: {b.GetAttribute("title")}"));
    }

    [Fact]
    public void Clear_All_names_the_search_and_every_row_the_box_has_in_the_box_and_in_the_bar()
    {
        var (page, _) = Everything();
        const string everything = "Clear every filter: the search with its dep:, duration: and is: terms, and the statuses, duration, happy paths, tags and dependencies";

        Assert.Equal(everything, TitleOf(page, "#filtering-box .export-btn[onclick='clear_all_filters()']"));
        Assert.Equal(everything, TitleOf(page, ".nav-filters .nav-clear"));

        // a run with times and nothing else to filter by: no happy path, no tag or category, no call
        var timed = Parse(Write("ControlTitles_Timed.html",
            [new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "x", DisplayName = "X", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(5) }] }], []));
        Assert.Equal("Clear every filter: the search with its dep:, duration: and is: terms, and the statuses and duration",
            TitleOf(timed, "#filtering-box .export-btn[onclick='clear_all_filters()']"));

        // and one with statuses alone
        var bare = Parse(Write("ControlTitles_Bare.html",
            [new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "x", DisplayName = "X", Result = ExecutionResult.Passed }] }], []));
        Assert.Equal("Clear every filter: the search with its dep:, duration: and is: terms, and the statuses",
            TitleOf(bare, "#filtering-box .export-btn[onclick='clear_all_filters()']"));
    }

    [Fact]
    public void Both_readouts_say_what_they_count()
    {
        var (page, _) = Everything();

        Assert.Equal(Showing, TitleOf(page, ".filtering-box-export .filter-readout"));
        Assert.Equal(Showing, TitleOf(page, ".nav-filters .nav-readout"));
    }

    [Fact]
    public void A_chip_gives_the_count_of_scenarios_it_picks_out_then_what_a_click_does()
    {
        var (page, _) = Everything();
        var chips = page.QuerySelectorAll(".tag-toggle, .dependency-toggle").ToArray();

        Assert.NotEmpty(chips);
        Assert.All(chips, chip => Assert.Matches(@"^\d+ scenarios?\. " + Regex.Escape(ChipClicks) + "$", chip.GetAttribute("title")));
        // Caller and OrderService are in four diagrams; the skipped and the bypassed scenario have none
        Assert.Equal("4 scenarios. " + ChipClicks, TitleOf(page, ".dependency-toggle[data-dependency='Caller']"));
        Assert.Equal("1 scenario. " + ChipClicks, TitleOf(page, ".dependency-toggle[data-dependency='OrderDb']"));
        Assert.Equal("2 scenarios. " + ChipClicks, TitleOf(page, ".dependency-toggle[data-none]"));
    }

    [Fact]
    public void The_pages_own_buttons_and_links_say_where_they_go_or_what_they_do()
    {
        var (page, _) = Everything();

        Assert.Equal("Clear every filter", TitleOf(page, ".filters-empty-clear"));
        Assert.Equal("Go to the next failed scenario; the count says which one of how many", TitleOf(page, ".jump-to-failure"));
        var top = One(page, "#back-to-top");
        Assert.Equal("Back to the top of the report", top.GetAttribute("title"));
        Assert.Equal("Back to top", top.GetAttribute("aria-label"));
        Assert.Equal("Open the CI run this report came from", TitleOf(page, "a.run-link"));
        Assert.Equal("Open this feature's card in the Full view", TitleOf(page, "button.feature-open"));
        var clustered = page.QuerySelectorAll("a.failure-cluster-scenario-link").ToArray();
        Assert.Equal(2, clustered.Length);
        Assert.All(clustered, link => Assert.Equal("Go to this scenario", link.GetAttribute("title")));
    }

    [Fact]
    public void A_scenarios_sections_say_what_they_hold()
    {
        var (page, _) = Everything();

        var pay = Scenario(page, "Pays for an order");
        Assert.Equal("This scenario's sequence diagrams", TitleOf(pay, "details.example-diagrams > summary"));
        Assert.Equal("The steps of the feature's background, which run before this scenario's own", TitleOf(pay, "details.scenario-background > summary"));
        Assert.Equal("This scenario's steps, with how long each took", TitleOf(pay, "details.scenario-steps > summary"));

        // no step of a failure has a time
        var expired = Scenario(page, "Pays with an expired card");
        Assert.Equal("This scenario's steps", TitleOf(expired, "details.scenario-steps > summary"));
        Assert.Equal("Why the scenario failed: the error, with the expected and actual values where it gives them", TitleOf(expired, "details.failure-result > summary"));

        var ships = Scenario(page, "Ships an order");
        Assert.Equal("This scenario's diagrams", TitleOf(ships, "details.example-diagrams > summary"));
        Assert.Equal(
            [
                "seq: The calls between the test and the services, in order",
                "activity: The work inside the services, span by span, as an activity diagram",
                "flame: Where the time went inside the services, span by span, as nested bars"
            ],
            ships.QuerySelectorAll(".diagram-toggle-btn").Select(b => $"{b.GetAttribute("data-dtype")}: {b.GetAttribute("title")}"));
    }

    [Theory]
    [InlineData(WholeTestFlowVisualization.ActivityDiagram, "Activity Diagrams", "The work inside the services, span by span, as an activity diagram")]
    [InlineData(WholeTestFlowVisualization.FlameChart, "Flame Chart", "Where the time went inside the services, span by span, as nested bars")]
    public void A_section_with_one_view_is_titled_with_what_that_view_shows(WholeTestFlowVisualization visualization, string heading, string title)
    {
        Feature[] features = [new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "flow", DisplayName = "Flows", Result = ExecutionResult.Passed }] }];
        var page = Parse(Write($"ControlTitles_OneView_{visualization}.html", features, [], segments: Flow("flow"), visualization: visualization));

        var summary = One(page, "details.example-diagrams > summary");
        Assert.StartsWith(heading, summary.TextContent);
        Assert.Equal(title, summary.GetAttribute("title"));
    }

    [Fact]
    public void An_attachment_says_what_it_opens_and_whose_it_is()
    {
        var (page, _) = Everything();
        var pay = Scenario(page, "Pays for an order");

        Assert.Equal("Open order.json, a file this scenario attached", TitleOf(pay, "a.step-attachment[href='attachments/order.json']"));
        Assert.Equal("Open request.txt, a file this step attached", TitleOf(pay, "a.step-attachment[href='attachments/request.txt']"));
        Assert.Equal("Open Trace, a link this step attached", TitleOf(pay, "a.step-attachment[href='https://example.com/trace/1']"));
        Assert.Equal("Show screen.png, an image this scenario attached, at full size", TitleOf(pay, "a.attachment-image-link[href='attachments/screen.png']"));
        Assert.Equal("Show step.png, an image this step attached, at full size", TitleOf(pay, "a.attachment-image-link[href='attachments/step.png']"));
    }

    /// <summary>
    /// An outline's examples are drawn by a path of their own (RenderParameterizedGroup), each in a panel under the table of
    /// examples: a failed example's Failure Result and its attachments carry the titles a scenario's do.
    /// </summary>
    [Fact]
    public void An_outlines_failed_example_and_its_attachments_have_the_titles_a_scenarios_have()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Refunds",
                Scenarios =
                [
                    new Scenario { Id = "r1", DisplayName = "Refunds(amount: 5)", Result = ExecutionResult.Passed, OutlineId = "Refunds", ExampleValues = new() { ["amount"] = "5" } },
                    new Scenario
                    {
                        Id = "r2", DisplayName = "Refunds(amount: 0)", Result = ExecutionResult.Failed, OutlineId = "Refunds", ExampleValues = new() { ["amount"] = "0" },
                        ErrorMessage = "Expected 1 refund but found 0",
                        Attachments = [new FileAttachment("refund.json", "attachments/refund.json"), new FileAttachment("refund.png", "attachments/refund.png", "image/png")]
                    }
                ]
            }
        ];
        var panels = One(Parse(Write("ControlTitles_Outline.html", features, [])), "details.scenario-parameterized .param-detail-panels");

        Assert.Equal("Why the scenario failed: the error, with the expected and actual values where it gives them", TitleOf(panels, "details.failure-result > summary"));
        Assert.Equal("Open refund.json, a file this scenario attached", TitleOf(panels, "a.step-attachment[href='attachments/refund.json']"));
        Assert.Equal("Show refund.png, an image this scenario attached, at full size", TitleOf(panels, "a.attachment-image-link[href='attachments/refund.png']"));
    }

    private record Batch(string Currency, string Priority, List<string> Payments);

    [Fact]
    public void A_steps_parameter_buttons_and_a_tables_whole_values_say_what_they_show()
    {
        var batch = new Batch("GBP", "Normal", ["P001", "P002"]);
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Muffins",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "bake", DisplayName = "Bakes a muffin", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "a muffin recipe", Status = ExecutionResult.Passed,
                                TextSegments = [StepTextSegment.Literal("a muffin "), StepTextSegment.TableRef("recipe")],
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "recipe", Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false), new TabularColumn("Flour", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Classic", null, VerificationStatus.NotApplicable), new TabularCell("Plain Flour", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "I apply a large config", Status = ExecutionResult.Passed,
                                TextSegments = [StepTextSegment.Literal("I apply a large "), StepTextSegment.TableRef("config")],
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "config", Kind = StepParameterKind.Inline,
                                        InlineValue = new InlineParameterValue("AppConfig { Host = localhost, Port = 8080, Timeout = 30, RetryCount = 3, Debug = True }",
                                            null, VerificationStatus.NotApplicable)
                                    }
                                ]
                            }
                        ]
                    },
                    new Scenario
                    {
                        Id = "b1", DisplayName = "Process(id: batch-001, batch: ...)", Result = ExecutionResult.Passed, OutlineId = "Process",
                        ExampleValues = new() { ["id"] = "batch-001", ["batch"] = batch.ToString() },
                        ExampleRawValues = new() { ["id"] = "batch-001", ["batch"] = batch }
                    },
                    new Scenario
                    {
                        Id = "b2", DisplayName = "Process(id: batch-002, batch: ...)", Result = ExecutionResult.Passed, OutlineId = "Process",
                        ExampleValues = new() { ["id"] = "batch-002", ["batch"] = batch.ToString() },
                        ExampleRawValues = new() { ["id"] = "batch-002", ["batch"] = batch }
                    }
                ]
            }
        ];
        var page = Parse(Write("ControlTitles_Parameters.html", features, []));

        Assert.Equal("Show recipe in its table", TitleOf(page, "button.step-table-ref[data-param='recipe']"));
        Assert.Equal("Show or hide the value of config", TitleOf(page, "button.step-table-ref[data-param='config']"));
        var values = page.QuerySelectorAll("details.param-expand > summary").ToArray();
        Assert.NotEmpty(values);
        Assert.All(values, summary => Assert.Equal("Show or hide the whole value", summary.GetAttribute("title")));
    }

    [Fact]
    public void In_its_tab_a_sections_heading_says_what_the_section_is_and_not_that_a_click_shows_or_hides_it()
    {
        var (page, _) = Everything();

        Assert.Equal(HistoryWords, TitleOf(page, "#panel-history details.history-section > summary"));
        Assert.Equal(DiagnosticsWords, TitleOf(page, "#panel-diagnostics details.report-diagnostics > summary"));
        Assert.All(page.QuerySelectorAll(".report-panel:not(#panel-features) summary[title]"),
            summary => Assert.DoesNotContain("click", summary.GetAttribute("title")!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Outside_a_tab_a_sections_heading_says_a_click_shows_or_hides_it()
    {
        var labs = Labs(Shop());

        Assert.Equal("This run against the earlier runs on the same branch: click to show or hide the details", TitleOf(labs, "details.history-section > summary"));
        Assert.Equal("What Kronikol noticed while building this report: click to show or hide the list", TitleOf(labs, "details.report-diagnostics > summary"));
    }

    [Fact]
    public void A_history_link_says_where_it_goes()
    {
        var (page, features) = Everything();
        var inPage = page.QuerySelectorAll("#panel-history a.history-link").ToArray();
        Assert.NotEmpty(inPage);
        Assert.All(inPage, link => Assert.Equal("Go to this scenario on the Features tab", link.GetAttribute("title")));

        var outward = Labs(features).QuerySelectorAll("a.history-link").ToArray();
        Assert.NotEmpty(outward);
        Assert.All(outward, link => Assert.Equal("Open this scenario in the report", link.GetAttribute("title")));
    }

    [Fact]
    public void The_scripts_title_what_they_draw_or_make_live()
    {
        var render = DiagramContextMenu.GetPlantUmlBrowserRenderScript();
        Assert.Equal(3, Regex.Count(render, Regex.Escape("<summary title=\"The PlantUML source this diagram was drawn from\">Raw PlantUML</summary>")));
        Assert.DoesNotContain(">Raw PlantUML</summary>", render.Replace("<summary title=\"The PlantUML source this diagram was drawn from\">Raw PlantUML</summary>", ""));

        var popup = DiagramContextMenu.GetInternalFlowPopupScript();
        Assert.Contains("closeBtn.setAttribute('aria-label', 'Close');", popup);
        Assert.Contains("closeBtn.title = 'Close (Esc)';", popup);
        Assert.Contains("? 'Where the time went inside the services for this call, span by span, as nested bars'", popup);
        Assert.Contains(": 'The work inside the services for this call, span by span, as an activity diagram';", popup);

        var (page, _) = Everything();
        var steps = page.QuerySelectorAll("script").Select(s => s.TextContent).Single(s => s.Contains("function giveTitle(icon)", StringComparison.Ordinal));
        Assert.Contains("target.closest('details.step-collapsible > summary')", steps);
        Assert.Contains("? 'This step has one step inside it: click to show or hide it'", steps);
        Assert.Contains(": 'This step has ' + n + ' steps inside it: click to show or hide them'", steps);
    }

    /// <summary>
    /// The audit of what the generator writes: every control has a title but the headings that name what they hold
    /// (<see cref="Headings"/>) and a composite step's summary, whose title the page gives it when it is first pointed at
    /// or focused. ControlTitlesAuditTests makes the same audit in a browser, of what shows.
    /// </summary>
    [Fact]
    public void No_control_the_generator_writes_is_left_without_a_title()
    {
        var (page, _) = Everything();
        var headings = page.QuerySelectorAll(Headings + ", details.step-collapsible > summary").ToHashSet();

        var controls = page.QuerySelectorAll(Controls).Where(c => !headings.Contains(c)).ToArray();
        Assert.True(controls.Length > 80, $"only {controls.Length} controls: the report is not the one this audit is about");
        Assert.Empty(controls.Where(c => string.IsNullOrWhiteSpace(c.GetAttribute("title"))).Select(Describe));
    }
}
