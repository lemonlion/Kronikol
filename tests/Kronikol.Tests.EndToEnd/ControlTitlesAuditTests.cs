using System.Diagnostics;
using System.Net;
using System.Text;
using Kronikol.History;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The titles audit (5.0.0, plans/OPTION_C_PLAN.md S12, D49) in a browser. In a report that has every control the report
/// draws, taken through each state that brings one into view (everything open, the page scrolled, the box in the
/// floating bar, no scenario left, the Summary view, each tab, a phone's width), no control that shows is without a title
/// or without a name; a heading whose own text names what it holds needs no title. And the titles the scripts give: a
/// composite step's summary when it is first pointed at or focused, and the internal-flow popup's close, view tabs and
/// diagnostic info. ControlTitlesTests holds each title's words.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ControlTitlesAuditTests : PlaywrightTestBase
{
    public ControlTitlesAuditTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Suite = "ControlTitlesAudit";
    private static readonly DateTimeOffset At = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>What the audit looks at: a button, a link, a summary, a select, an input, a tab, a role of button.</summary>
    private const string Controls = "button, a[href], summary, select, input, [role=tab], [role=button]";

    /// <summary>The headings whose own text names what they hold: a feature's card, a rule's, an outline's group, the
    /// failure clusters and each cluster, the background calls.</summary>
    private const string Headings = "details.feature > summary, details.rule > summary, details.scenario-parameterized > summary, "
        + "details.failure-clusters > summary, details.failure-cluster > summary, details.background-calls > summary";

    // Every control that shows (not in a diagram's drawing), as a pointer that reaches it finds it: the page titles some
    // controls when they are first pointed at. A control has a name when it has a title, an aria-label, the text of what
    // it is labelled by, an image's alt or, but for a field, text with a letter or a digit in it.
    private const string AuditScript = """
        ([state, controls, headings]) => {
            const misses = [];
            const describe = el => el.tagName.toLowerCase() + (el.getAttribute('class') ? '.' + el.getAttribute('class').trim().split(/\s+/).join('.') : '')
                + ' "' + (el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 60) + '"';
            for (const el of document.querySelectorAll(controls)) {
                if (el.closest('svg')) continue;
                const box = el.getBoundingClientRect();
                if (box.width === 0 || box.height === 0 || getComputedStyle(el).visibility === 'hidden') continue;
                el.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
                const title = (el.getAttribute('title') || '').trim();
                const label = (el.getAttribute('aria-label') || '').trim();
                const labelledBy = (el.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean)
                    .map(id => document.getElementById(id)).filter(Boolean).map(e => e.textContent.trim()).join(' ');
                const alt = [...el.querySelectorAll('img[alt]')].map(i => i.alt.trim()).join(' ');
                const labels = el.labels ? [...el.labels].map(l => l.textContent.trim()).join(' ') : '';
                const text = el.matches('input, select') ? '' : (el.textContent || '');
                if (!title && !el.matches(headings)) misses.push(state + ': no title: ' + describe(el));
                if (!title && !label && !labelledBy && !alt && !labels && !/[\p{L}\p{N}]/u.test(text)) misses.push(state + ': no name: ' + describe(el));
            }
            return misses;
        }
        """;

    private Task<string[]> AuditAsync(string state) =>
        Page.EvaluateAsync<string[]>(AuditScript, new object[] { state, Controls, Headings });

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

    private sealed record Batch(string Currency, string Priority, List<string> Payments);

    /// <summary>
    /// A report with every control the report draws: every tab (the timeline, with a scenario that paid a first-call
    /// warm-up; the components; history; diagnostics), the CI box, every payload control and toggle, every filter row,
    /// a failure cluster, a background, a composite step of two steps and one of one, attachments of a file, a link and
    /// images on a scenario and a step, a step's parameter buttons, an outline's group with a value too complex to show
    /// whole, and a scenario whose calls have spans, so its diagrams have tabs. Returns its address.
    /// </summary>
    private string Write(string fileName)
    {
        var reportsDir = Directory.CreateDirectory(Path.Combine(TempDir, "reports-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        var sources = Directory.CreateDirectory(Path.Combine(TempDir, "sources-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        string Source(string name, byte[] bytes)
        {
            var path = Path.Combine(sources, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        var json = Encoding.UTF8.GetBytes("{\"id\":42}");
        var batch = new Batch("GBP", "Normal", ["P001", "P002"]);

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "pay", DisplayName = "Pays for an order", Result = ExecutionResult.Passed, IsHappyPath = true,
                        Duration = TimeSpan.FromMilliseconds(2000), Tags = ["fast", "smoke"],
                        Attachments = [new FileAttachment("order.json", Source("order.json", json)), new FileAttachment("screen.png", Source("screen.png", ReportTestHelper.OnePixelPng), "image/png")],
                        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the shop is open", Status = ExecutionResult.Passed }],
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "an order request", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(100),
                                Attachments =
                                [
                                    new FileAttachment("request.txt", Source("request.txt", json)),
                                    new FileAttachment("Trace", "https://example.com/trace/1"),
                                    new FileAttachment("step.png", Source("step.png", ReportTestHelper.OnePixelPng), "image/png")
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
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "the order is confirmed", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(200),
                                SubSteps = [new ScenarioStep { Keyword = "Then", Text = "the email is sent", Status = ExecutionResult.Passed }]
                            }
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
            },
            new Feature
            {
                DisplayName = "Muffins",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "bake", DisplayName = "Bakes a muffin", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(80),
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
                        Duration = TimeSpan.FromMilliseconds(60),
                        ExampleValues = new() { ["id"] = "batch-001", ["batch"] = batch.ToString() },
                        ExampleRawValues = new() { ["id"] = "batch-001", ["batch"] = batch }
                    },
                    new Scenario
                    {
                        Id = "b2", DisplayName = "Process(id: batch-002, batch: ...)", Result = ExecutionResult.Passed, OutlineId = "Process",
                        Duration = TimeSpan.FromMilliseconds(70),
                        ExampleValues = new() { ["id"] = "batch-002", ["batch"] = batch.ToString() },
                        ExampleRawValues = new() { ["id"] = "batch-002", ["batch"] = batch }
                    }
                ]
            }
        ];
        ReportGenerator.CopyAttachmentsToReportsFolder(features, reportsDir);

        DiagramAsCode[] diagrams =
        [
            new("pay", "", Busy), new("expired", "", Plain), new("blocked", "", Plain), new("ships", "", Plain),
            new("bake", "", Plain), new("b1", "", Plain), new("b2", "", Plain)
        ];

        // "Pays for an order" makes the run's first call, of 1.5 s, and "Pays with an expired card" two more of 5 ms: the
        // first is the run's first-call warm-up
        var logs = new List<RequestResponseLog>();
        Call(logs, "pay", 0, 1500);
        Call(logs, "expired", 3000, 5);
        Call(logs, "expired", 3100, 5);

        // three earlier runs in which every scenario passed
        var ledger = Path.Combine(TempDir, "ledger-" + Guid.NewGuid().ToString("N")[..8], "history.jsonl");
        var (roster, run) = HistoryRunBuilder.Build(features, [], Suite, null, At, new HistoryBuildOptions(), "e2e:99:1");
        for (var i = 0; i < 3; i++)
        {
            var results = new string('P', run.Results.Length);
            var prior = run with
            {
                Id = $"e2e:{i + 1}:1", At = At.AddHours(i - 3), Commit = $"c{i + 1:D6}", Results = results,
                Attempts = new string('-', results.Length), Durations = Enumerable.Repeat<int?>(100, results.Length).ToArray(),
                Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(), ErrorText = new Dictionary<string, string>()
            };
            var appended = HistoryLedgerWriter.Append(ledger, roster, prior, "3.11.0");
            if (appended.Outcome != HistoryAppendOutcome.Appended)
                throw new InvalidOperationException($"seeding the ledger failed: {appended.Message}");
        }
        var held = HistoryLedgerReader.Read(ledger, 50).Ledger ?? throw new InvalidOperationException("the seeded ledger did not read back");
        var history = HistoryAnalyzer.Analyse(held, roster, run, new HistoryAnalysisOptions { MinRuns = 2 });

        using var source = new ActivitySource("Kronikol.Tests.EndToEnd.ControlTitles");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Kronikol.Tests.EndToEnd.ControlTitles",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;
        var start = At.UtcDateTime.AddSeconds(-30);
        var root = source.StartActivity("HTTP POST /api/orders", ActivityKind.Server)!;
        root.SetStartTime(start);
        root.SetEndTime(start.AddMilliseconds(400));
        var child = source.StartActivity("SELECT orders", ActivityKind.Internal, new ActivityContext(root.TraceId, root.SpanId, ActivityTraceFlags.Recorded))!;
        child.SetStartTime(start.AddMilliseconds(20));
        child.SetEndTime(start.AddMilliseconds(300));
        Activity.Current = null;
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            ["iflow-test-ships"] = new(Guid.Empty, RequestResponseType.Request, "ships", start, start.AddMilliseconds(400), [root, child])
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features, At.UtcDateTime.AddMinutes(-1), At.UtcDateTime, null, Path.Combine(reportsDir, fileName), "Control titles", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            internalFlowTracking: true, wholeTestSegments: segments, trackedLogs: logs.ToArray(),
            wholeTestVisualization: WholeTestFlowVisualization.Both,
            ciMetadata: new CiMetadata(CiEnvironment.GitHubActions, "322", "main", "0ee43e8c0b5d3e85a4a099a20ee4ce37305b6b3f",
                "https://github.com/example/shop/actions/runs/7", "example/shop", "7"),
            componentDiagramPlantUml: ComponentSource,
            diagnostics: [new DiagnosticEntry(DiagnosticKind.CaptureDegraded, "tap-a: 1 oversize payload skipped")], showReportDiagnostics: true,
            separateBackgroundSteps: true, suite: Suite, history: history, showHistorySection: true);
        child.Dispose();
        root.Dispose();

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private static void Call(List<RequestResponseLog> logs, string scenario, double startMs, double durationMs)
    {
        var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://orders/orders");
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Request, trace, id, false)
            { Timestamp = At.AddTicks((long)Math.Round(startMs * TimeSpan.TicksPerMillisecond)) });
        logs.Add(new RequestResponseLog(scenario, scenario, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK)
            { Timestamp = At.AddTicks((long)Math.Round((startMs + durationMs) * TimeSpan.TicksPerMillisecond)) });
    }

    [Fact]
    public async Task Every_control_that_shows_has_a_title_and_a_name()
    {
        await Page.GotoAsync(Write("ControlTitlesAudit.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        var misses = new List<string>();

        // everything open: the features and scenarios, every disclosure under them, the filter rows with the field for
        // seconds, and each scenario's settings
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('#panel-features details').forEach(d => d.open = true)");
        await OpenFilterRowsAsync();
        await Page.Locator(".percentile-btn[data-custom]").ClickAsync();
        await Expect(Page.Locator("#duration-threshold")).ToBeVisibleAsync();
        // a closed settings bar is 0 px tall, its gear standing out of it, so the bars are counted by their gears
        var gears = Page.Locator(".scenario-settings-gear:visible");
        var bars = await gears.CountAsync();
        Assert.True(bars >= 4, $"{bars} scenario settings show");
        for (var i = 0; i < bars; i++)
            await OpenScenarioSettingsAsync(gears.Nth(i).Locator("../.."));
        await Expect(Page.Locator(".scenario-settings.is-open")).ToHaveCountAsync(bars);
        misses.AddRange(await AuditAsync("everything open"));

        // scrolled to the end, where the way back to the top shows
        await Page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
        await Page.WaitForFunctionAsync("() => document.getElementById('back-to-top').style.display === 'block'", null, new() { PollingInterval = 200 });
        misses.AddRange(await AuditAsync("scrolled to the end"));

        // the bar floating, with the box taken into it
        await Page.EvaluateAsync("() => window.scrollTo(0, document.querySelector('.nav-mark').getBoundingClientRect().top + window.scrollY + 300)");
        await Page.WaitForFunctionAsync("() => document.querySelector('.report-nav').classList.contains('k-floating')", null, new() { PollingInterval = 200 });
        await Page.Locator(".nav-filters-toggle").ClickAsync();
        await Expect(Page.Locator(".filters-close")).ToBeVisibleAsync();
        misses.AddRange(await AuditAsync("the box in the bar"));
        await Page.Locator(".filters-close").ClickAsync();
        await Expect(Page.Locator(".filters-close")).ToBeHiddenAsync();
        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");

        // no scenario left: the empty state and the bar's Clear All
        await FillSearchBar("nothing-matches-this");
        await Expect(Page.Locator(".filters-empty")).ToBeVisibleAsync();
        misses.AddRange(await AuditAsync("no scenario left"));
        await Page.Locator(".filters-empty-clear").ClickAsync();
        await Expect(Page.Locator(".filters-empty")).ToBeHiddenAsync();

        // the Summary view, then each tab
        await Page.Locator("#view-summary").ClickAsync();
        await Expect(Page.Locator("button.feature-open").First).ToBeVisibleAsync();
        misses.AddRange(await AuditAsync("the Summary view"));
        await Page.Locator("#view-full").ClickAsync();
        foreach (var tab in new[] { "timeline", "components", "history", "diagnostics" })
        {
            await OpenReportTabAsync(tab);
            misses.AddRange(await AuditAsync($"the {tab} tab"));
        }
        await OpenReportTabAsync("features");

        // a phone's width
        await Page.SetViewportSizeAsync(390, 844);
        await TwoAnimationFramesAsync(Page);
        misses.AddRange(await AuditAsync("on a phone"));

        Assert.Empty(misses);
    }

    /// <summary>A composite step's summary, by the text of its step.</summary>
    private ILocator CompositeSummary(string text) =>
        Page.Locator("details.step-collapsible > summary", new() { HasTextString = text }).First;

    [Fact]
    public async Task A_composite_steps_summary_says_how_many_steps_it_holds_once_it_is_pointed_at_or_focused()
    {
        await Page.GotoAsync(Write("ControlTitlesAudit_Steps.html"));
        await ExpandAllFeaturesAsync();
        await ExpandAllScenariosAsync();
        var two = CompositeSummary("the order is placed");
        var one = CompositeSummary("the order is confirmed");
        await Expect(two).ToBeVisibleAsync();

        // the page holds no title per step: each is given its own when it is first needed
        Assert.Null(await two.GetAttributeAsync("title"));
        Assert.Null(await one.GetAttributeAsync("title"));

        await two.DispatchEventAsync("mouseover");
        await Expect(two).ToHaveAttributeAsync("title", "This step has 2 steps inside it: click to show or hide them");
        await one.FocusAsync();
        await Expect(one).ToHaveAttributeAsync("title", "This step has one step inside it: click to show or hide it");
    }

    [Fact]
    public async Task The_internal_flow_popup_titles_its_close_its_view_tabs_and_its_diagnostic_info()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true, includeEmptySegment: true)));

        await Page.Locator("#trigger-seg-1").ClickAsync();
        var popup = Page.Locator(".iflow-popup");
        var close = popup.Locator(".iflow-popup-close");
        await Expect(close).ToHaveAttributeAsync("aria-label", "Close");
        await Expect(close).ToHaveAttributeAsync("title", "Close (Esc)");
        // the tabs are titled where they are made live, once the segment is decoded (3.31.9)
        await Expect(popup.Locator(".iflow-toggle-btn[data-view='main']")).ToHaveAttributeAsync("title",
            "The work inside the services for this call, span by span, as an activity diagram");
        await Expect(popup.Locator(".iflow-toggle-btn[data-view='flame']")).ToHaveAttributeAsync("title",
            "Where the time went inside the services for this call, span by span, as nested bars");
        await close.ClickAsync();
        await Expect(popup).ToHaveCountAsync(0);

        await Page.Locator("#trigger-seg-empty").ClickAsync();
        await Expect(Page.Locator(".iflow-popup .iflow-no-data details.iflow-diagnostics > summary")).ToHaveAttributeAsync("title",
            "Why this call may have no spans: click to show or hide the details");
    }
}
