using System.Net;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// SEARCH_INDEX_PLAN §9.3: deep search ("search everything") end-to-end. Diagrams are generated
/// through the REAL capture pipeline (<see cref="RequestResponseLog"/> entries →
/// <see cref="PlantUmlCreator"/>) — hand-written PlantUML would bypass the formatter and the
/// chunking/wrapping/creole transforms the normalization exists for would never fire.
/// </summary>
[Collection(PlaywrightCollections.Search)]
public class DeepSearchTests : PlaywrightTestBase
{
    public DeepSearchTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string PayloadNeedle = "zqxpayload-widget-7734";
    private const string HeaderNeedle = "zqxheaderval-55";
    private const string SqlNeedle = "insert into zorbtable_qx";
    private const string MessageNeedle = "zqxmsgpath-42";
    private const string WrapNeedle = "zqxwrapneedle99";
    private const string ParamPayloadNeedle = "zqxparamtag-31";
    private const string ExampleValueNeedle = "zqxexample-11";

    private static RequestResponseLog[] BuildCaptureLogs()
    {
        // The wrap needle must STRADDLE the formatter's hard cut: WrapUnbreakableRuns cuts at
        // 120 run chars (no punctuation in the needle or the A-run, so the back-scan cannot
        // dodge it). Run = `"` + 10-char prefix + 102 A's -> the 15-char needle occupies run
        // positions 113-127, bisected at 120. Wrapped_unbreakable_run_… asserts this geometry.
        var wrapToken = "eyJhbGciOi" + new string('A', 102) + WrapNeedle + new string('B', 80);
        var s1Body = "{\"widget\":\"" + PayloadNeedle + "\",\"blob\":\"" + wrapToken + "\"}";
        var s3Body = "{\"tag\":\"" + ParamPayloadNeedle + "\"}";
        var t1 = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var t3 = Guid.NewGuid();
        var p3 = Guid.NewGuid();
        return
        [
            new("Create order successfully", "s1", HttpMethod.Post, s1Body,
                new Uri("https://api.example.test/api/orders"),
                [("Content-Type", "application/json"), ("X-Zqx-Header", HeaderNeedle)],
                "OrderService", "Test", RequestResponseType.Request, t1, p1, TrackingIgnore: false),
            new("Create order successfully", "s1", HttpMethod.Post, "{\"status\":\"created\"}",
                new Uri("https://api.example.test/api/orders"),
                [("Content-Type", "application/json")],
                "OrderService", "Test", RequestResponseType.Response, t1, p1, TrackingIgnore: false, StatusCode: HttpStatusCode.Created),
            // Genuine SQL-capture shape (SqlDiagnosticTracker): string method, SQL text as the
            // content, database dependency category. The arrow label "INSERT: /zqxmsgpath-42"
            // is NOT extracted into shallow data-search (UrlRegex only matches HTTP verbs), so
            // both the SQL text and the path are deep-only surfaces.
            new("Create order successfully", "s1", "INSERT", SqlNeedle + " (item, qty) values ('w', 2)",
                new Uri("sql://orders-db/" + MessageNeedle),
                [],
                "OrdersDb", "OrderService", RequestResponseType.Request, t3, p3, TrackingIgnore: false,
                DependencyCategory: Kronikol.Constants.DependencyCategories.SQL),
            new("Row one", "s3", HttpMethod.Post, s3Body,
                new Uri("https://api.example.test/api/rows"),
                [("Content-Type", "application/json")],
                "RowService", "Test", RequestResponseType.Request, t2, p2, TrackingIgnore: false)
        ];
    }

    private static Feature[] BuildFeatures() =>
    [
        new Feature
        {
            DisplayName = "Order Feature",
            Endpoint = "zqx-endpoint-svc",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "Create order successfully", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "s2", DisplayName = "List orders quietly", Result = ExecutionResult.Passed,
                    Description = "zqxdesc-listing covers the quiet path",
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "s3", DisplayName = "Row one", Result = ExecutionResult.Passed,
                    OutlineId = "rows-grp",
                    ExampleValues = new Dictionary<string, string> { ["Code"] = ExampleValueNeedle },
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a row value", Status = ExecutionResult.Passed }]
                },
                new Scenario
                {
                    Id = "s4", DisplayName = "Row two", Result = ExecutionResult.Passed,
                    OutlineId = "rows-grp",
                    ExampleValues = new Dictionary<string, string> { ["Code"] = "plain-code" },
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a row value", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private string GenerateDeepReport(string fileName,
        PlantUmlRendering rendering = PlantUmlRendering.BrowserJs,
        bool inlineSvg = false,
        bool fullSearchIndex = true)
    {
        var stubSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"20\"><text y=\"12\">d</text></svg>";
        var imgSrc = rendering == PlantUmlRendering.BrowserJs ? "" : inlineSvg ? stubSvg : "stub.png";
        var diagrams = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(BuildCaptureLogs(), clientSideSplitting: rendering == PlantUmlRendering.BrowserJs)
            .SelectMany(t => t.PlantUmls.Select(p => new DiagramAsCode(t.TestId, imgSrc, p.PlainText)))
            .ToArray();

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, BuildFeatures(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Deep Search Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: rendering,
            inlineSvgRendering: inlineSvg,
            fullSearchIndex: fullSearchIndex);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task<int> GetVisibleScenarioCount() =>
        await Page.EvaluateAsync<int>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none').length
        """);

    private async Task SearchAndWaitForVisible(string query, int expectedCount, int timeoutMs = 15000)
    {
        await FillSearchBar(query);
        await Page.WaitForFunctionAsync(
            $"() => Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === {expectedCount}",
            null, new() { Timeout = timeoutMs, PollingInterval = 200 });
    }

    private async Task WaitForChipText(string contains, int timeoutMs = 15000)
    {
        await Page.WaitForFunctionAsync(
            $"() => {{ var c = document.querySelector('.kron-deep-chip'); return c && c.style.display !== 'none' && c.textContent.includes('{contains}'); }}",
            null, new() { Timeout = timeoutMs, PollingInterval = 200 });
    }

    // ── deep-only surfaces become findable ──

    [Fact]
    public async Task Payload_only_text_is_found_and_chip_reports_the_addition()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchPayload.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(PayloadNeedle, 1);
        var search = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none')[0].id
        """);
        Assert.Equal("scenario-create-order-successfully", search);
        await WaitForChipText("+1 more found in payloads & diagrams");
    }

    [Fact]
    public async Task Header_only_text_is_found()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchHeader.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(HeaderNeedle, 1);
    }

    [Fact]
    public async Task Sql_statement_text_is_found()
    {
        // The needle travels the genuine SQL-capture shape (string method, database category,
        // SQL as content) — not a JSON payload wearing SQL clothing.
        await Page.GotoAsync(GenerateDeepReport("DeepSearchSql.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible("zorbtable_qx", 1);
    }

    [Fact]
    public async Task Message_only_arrow_text_is_found()
    {
        // The SQL arrow label "INSERT: /zqxmsgpath-42" is message text the shallow search never
        // sees (ExtractDiagramSearchTerms only extracts HTTP-verb targets).
        await Page.GotoAsync(GenerateDeepReport("DeepSearchMessage.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(MessageNeedle, 1);
        await WaitForChipText("more found");
    }

    [Fact]
    public async Task Wrapped_unbreakable_run_is_found_across_the_formatter_line_break()
    {
        // The capture pipeline wraps >120-char runs with real newlines; the shared
        // normalization must rejoin them or this mid-token needle is unfindable.
        // First prove the geometry: the formatter really did bisect the needle — without this,
        // the fact would pass even with the 5b rejoin deleted.
        var codeBehinds = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(BuildCaptureLogs(), clientSideSplitting: true)
            .SelectMany(t => t.PlantUmls.Select(p => p.PlainText)).ToArray();
        Assert.DoesNotContain(codeBehinds, c => c.Contains(WrapNeedle));

        await Page.GotoAsync(GenerateDeepReport("DeepSearchWrapped.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(WrapNeedle, 1);
    }

    [Fact]
    public async Task Parameterized_row_payload_match_reveals_the_group()
    {
        // Deep matches reveal the GROUP; pinpointing the matching row for payload-only needles
        // is hit-location UX — SEARCH_INDEX_PLAN Phase 2 (§10), deliberately unbuilt. (A needle
        // present in data-row-search always shallow-matches the group too, since the group
        // data-search aggregates all row text — so the shallow row highlight already covers
        // every reachable row-highlight case.)
        await Page.GotoAsync(GenerateDeepReport("DeepSearchParamRow.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(ParamPayloadNeedle, 1);
        var visibleIsGroup = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none')[0]
                .classList.contains('scenario-parameterized')
        """);
        Assert.True(visibleIsGroup);
    }

    [Fact]
    public async Task Example_values_are_found_via_the_coverage_fix()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchExampleValue.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // shallow finds it now (data-search carries example values) — visible without deep
        await SearchAndWaitForVisible(ExampleValueNeedle, 1);
    }

    // ── rendering modes ──

    [Fact]
    public async Task InlineSvg_mode_finds_payload_text()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchInlineSvg.html", PlantUmlRendering.Local, inlineSvg: true));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(PayloadNeedle, 1);
    }

    [Fact]
    public async Task Img_mode_finds_payload_text_via_raw_plantuml_pre()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchImgMode.html", PlantUmlRendering.Server));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(PayloadNeedle, 1);
    }

    // ── deep is authoritative: negation can remove shallow results ──

    [Fact]
    public async Task Negated_payload_term_removes_a_shallow_match_and_chip_says_refined()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchNegation.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // shallow: both "running" scenarios match (needle is not in data-search, so !! passes);
        // deep: s1's payload contains the needle -> excluded, only s2 stays
        await FillSearchBar($"running && !!{PayloadNeedle}");
        await Page.WaitForFunctionAsync("""
            () => {
                var vis = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return vis.length === 1 && vis[0].id === 'scenario-list-orders-quietly';
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });
        await WaitForChipText("results refined (+0/−1)");
    }

    [Fact]
    public async Task InlineSvg_mode_negation_removes_a_shallow_match()
    {
        // Mode-specific verify-corpus assembly under negation: InlineSvg reads #puml-data too.
        await Page.GotoAsync(GenerateDeepReport("DeepSearchInlineSvgNegation.html", PlantUmlRendering.Local, inlineSvg: true));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar($"running && !!{PayloadNeedle}");
        await Page.WaitForFunctionAsync("""
            () => {
                var vis = Array.from(document.querySelectorAll('.scenario'))
                    .filter(s => getComputedStyle(s).display !== 'none');
                return vis.length === 1 && vis[0].id === 'scenario-list-orders-quietly';
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    // ── descriptions, endpoints and stack traces (3.0.72 scope extension) ──

    [Fact]
    public async Task Scenario_description_and_endpoint_are_found_instantly()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchDescEndpoint.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // both are in data-search now — shallow finds them, no deep round-trip needed
        await SearchAndWaitForVisible("zqxdesc-listing", 1);
        var visible = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelectorAll('.scenario'))
                .filter(s => getComputedStyle(s).display !== 'none')[0].id
        """);
        Assert.Equal("scenario-list-orders-quietly", visible);

        await SearchAndWaitForVisible("zqx-endpoint-svc", 3); // feature-wide: every scenario
    }

    private string GenerateFailedReport(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Failing Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "f1", DisplayName = "Breaks badly", Result = ExecutionResult.Failed,
                        ErrorMessage = "Expected <null> to be 7",
                        ErrorStackTrace = "at Zqx.Frames.zqxstacktrace77() in OrderService.cs:line 42",
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "a doomed setup", Status = ExecutionResult.Failed }]
                    },
                    new Scenario
                    {
                        Id = "f2", DisplayName = "Works fine", Result = ExecutionResult.Passed,
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "a fine setup", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport(
            [], features, DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Deep Search Failed Report", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    [Fact]
    public async Task Stack_trace_text_is_found_via_deep_search_only()
    {
        await Page.GotoAsync(GenerateFailedReport("DeepSearchStackTrace.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // deep-only by design: not in any data-search attribute…
        var inShallow = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('[data-search]'))
                .some(el => el.getAttribute('data-search').includes('zqxstacktrace77'))
        """);
        Assert.False(inShallow);

        // …but the search box still finds it, via the index + the rendered failure <pre>
        await SearchAndWaitForVisible("zqxstacktrace77", 1);
        await WaitForChipText("+1 more found");
    }

    [Fact]
    public async Task Failure_message_with_angle_brackets_renders_visibly()
    {
        // "<null>" used to parse as an unknown tag and vanish from the rendered failure text
        await Page.GotoAsync(GenerateFailedReport("DeepSearchFailureEncoding.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var preText = await Page.Locator(".failure-result pre").First.EvaluateAsync<string>("el => el.textContent");
        Assert.Contains("Expected <null> to be 7", preText);
        Assert.Contains("zqxstacktrace77", preText);
    }

    // ── a broken index must fail loudly, not wedge ──

    [Fact]
    public async Task Corrupt_index_blob_surfaces_error_and_never_wedges_the_chip()
    {
        var url = GenerateDeepReport("DeepSearchCorruptBlob.html");
        var path = new Uri(url).LocalPath;
        var html = File.ReadAllText(path);
        // valid base64, not gzip — the worker's DecompressionStream throws during init
        var corrupted = System.Text.RegularExpressions.Regex.Replace(html,
            "(<script id=\"kron-search-index\" type=\"application/json\">\")[^\"]*",
            "$1" + Convert.ToBase64String("not-gzip-data"u8.ToArray()));
        Assert.NotEqual(html, corrupted);
        File.WriteAllText(path, corrupted);

        await Page.GotoAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();

        // deep-eligible query triggers init; the failure must surface as indexState 'error'
        // with the chip hidden (an unhandled worker rejection would leave it pulsing forever)
        await FillSearchBar(PayloadNeedle);
        await Page.WaitForFunctionAsync(
            "() => window.__kronikolSearch.indexState === 'error'",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        await Page.WaitForFunctionAsync(
            "() => { var c = document.querySelector('.kron-deep-chip'); return !c || c.style.display === 'none'; }",
            null, new() { Timeout = 5000, PollingInterval = 200 });

        // shallow search is unaffected
        await SearchAndWaitForVisible("successfully", 1);
    }

    // ── the JSON⇄YAML note toggle must not change deep results ──

    [Fact]
    public async Task Yaml_toggle_does_not_change_deep_results()
    {
        // The bulk toggle rewrites data-plantuml attributes and re-renders every note; verify
        // reads #puml-data, so deep results must be identical before and after.
        await Page.GotoAsync(GenerateDeepReport("DeepSearchYamlToggle.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(PayloadNeedle, 1);

        await FillSearchBar("");
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === 3",
            null, new() { Timeout = 15000, PollingInterval = 200 });

        await Page.EvaluateAsync("() => { var v = document.querySelectorAll('details'); v.forEach(d => d.setAttribute('open','')); }");
        await WaitForDiagramSvg();
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await Page.Locator(".toolbar-right .note-format-select").SelectOptionAsync("yaml");
        await Page.WaitForFunctionAsync(
            $"() => (window._renderCompleteCount || 0) > {renderCount}",
            null, new() { Timeout = 15000, PollingInterval = 200 });

        await SearchAndWaitForVisible(PayloadNeedle, 1);
    }

    // ── chip states ──

    [Fact]
    public async Task Chip_reports_no_additional_matches_when_deep_adds_nothing()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchChipZero.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible("successfully", 1);
        await WaitForChipText("no additional matches");
    }

    [Fact]
    public async Task Chip_is_hidden_for_metadata_only_queries()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchChipHidden.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await FillSearchBar(PayloadNeedle);
        await WaitForChipText("more found");

        await FillSearchBar("$passed");
        await Page.WaitForFunctionAsync(
            "() => { var c = document.querySelector('.kron-deep-chip'); return !c || c.style.display === 'none'; }",
            null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Clear_all_filters_resets_deep_state_and_chip()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchClearAll.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(PayloadNeedle, 1);
        await WaitForChipText("more found");

        await Page.EvaluateAsync("() => clear_all_filters()");
        await Page.WaitForFunctionAsync(
            "() => { var c = document.querySelector('.kron-deep-chip'); return !c || c.style.display === 'none'; }",
            null, new() { Timeout = 5000, PollingInterval = 200 });
        Assert.Equal(3, await GetVisibleScenarioCount());
    }

    // ── URL hash restore re-runs deep ──

    [Fact]
    public async Task Hash_restore_applies_deep_results_once_the_index_is_ready()
    {
        var url = GenerateDeepReport("DeepSearchHashRestore.html");
        await Page.GotoAsync(url + "#q=" + PayloadNeedle);
        await Page.Locator("details.feature").First.WaitForAsync();

        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === 1",
            null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    // ── opt-out behaves like today ──

    [Fact]
    public async Task Opt_out_report_has_no_index_no_chip_and_shallow_search_still_works()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchOptOut.html", fullSearchIndex: false));
        await Page.Locator("details.feature").First.WaitForAsync();

        Assert.False(await Page.EvaluateAsync<bool>("() => !!document.getElementById('kron-search-index')"));

        await SearchAndWaitForVisible("successfully", 1);   // shallow still works
        await FillSearchBar(PayloadNeedle);
        await Page.WaitForTimeoutAsync(1200);               // debounce + would-be deep window
        Assert.Equal(0, await GetVisibleScenarioCount());   // deep-only needle stays unfindable
        Assert.False(await Page.EvaluateAsync<bool>(
            "() => { var c = document.querySelector('.kron-deep-chip'); return !!c && c.style.display !== 'none'; }"));
    }

    // ── the puml-data (not data-plantuml) invariant ──

    [Fact]
    public async Task Deep_results_are_unchanged_after_rendering_mutates_data_plantuml()
    {
        await Page.GotoAsync(GenerateDeepReport("DeepSearchAfterRender.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(PayloadNeedle, 1);

        // Render the diagrams — _preProcessSource rewrites data-plantuml attributes on first
        // render (note-format conversion, truncation). Verify reads #puml-data, so results
        // must not change.
        await Page.EvaluateAsync("() => { var v = document.querySelectorAll('details'); v.forEach(d => d.setAttribute('open','')); }");
        await WaitForDiagramSvg();

        await FillSearchBar("");
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === 3",
            null, new() { Timeout = 15000, PollingInterval = 200 });

        await SearchAndWaitForVisible(PayloadNeedle, 1);
    }

    // ── telemetry + stability ──

    [Fact]
    public async Task Telemetry_object_is_populated_and_no_page_errors_occur()
    {
        var errors = new List<string>();
        Page.PageError += (_, e) => errors.Add(e);

        await Page.GotoAsync(GenerateDeepReport("DeepSearchTelemetry.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(PayloadNeedle, 1);
        await WaitForChipText("more found");

        var state = await Page.EvaluateAsync<string>("() => window.__kronikolSearch.indexState");
        Assert.Equal("ready", state);
        var docs = await Page.EvaluateAsync<int>("() => window.__kronikolSearch.docs");
        Assert.Equal(4, docs); // s1, s2, s3, s4
        var verified = await Page.EvaluateAsync<int>("() => window.__kronikolSearch.lastQuery.verified");
        Assert.Equal(1, verified);
        var added = await Page.EvaluateAsync<int>("() => window.__kronikolSearch.lastQuery.added");
        Assert.Equal(1, added);

        Assert.Empty(errors);
    }
}
