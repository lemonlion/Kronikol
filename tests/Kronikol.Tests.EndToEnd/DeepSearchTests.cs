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
    private const string WrapNeedle = "zqxwrapneedle99";
    private const string ParamPayloadNeedle = "zqxparamtag-31";
    private const string ExampleValueNeedle = "zqxexample-11";

    private static RequestResponseLog[] BuildCaptureLogs()
    {
        var wrapToken = "eyJhbGciOi" + new string('A', 80) + WrapNeedle + new string('B', 80);
        var s1Body = "{\"widget\":\"" + PayloadNeedle + "\",\"blob\":\"" + wrapToken + "\",\"sql\":\"" + SqlNeedle + " (item, qty) values ('w', 2)\"}";
        var s3Body = "{\"tag\":\"" + ParamPayloadNeedle + "\"}";
        var t1 = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
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
        await Page.GotoAsync(GenerateDeepReport("DeepSearchSql.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible("zorbtable_qx", 1);
    }

    [Fact]
    public async Task Wrapped_unbreakable_run_is_found_across_the_formatter_line_break()
    {
        // The capture pipeline wraps >120-char runs with real newlines; the shared
        // normalization must rejoin them or this mid-token needle is unfindable.
        await Page.GotoAsync(GenerateDeepReport("DeepSearchWrapped.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        await SearchAndWaitForVisible(WrapNeedle, 1);
    }

    [Fact]
    public async Task Parameterized_row_payload_is_found_and_row_is_highlighted()
    {
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
