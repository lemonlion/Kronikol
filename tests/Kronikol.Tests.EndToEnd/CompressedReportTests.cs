using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The compressed report page in the browser (plans/HTML_COMPRESSION_PLAN.md R1, section 5 tests 8 to 20): the loader
/// paints its screen, decodes the page frame and puts the report in its place, and the report reads its diagram sources
/// from the pieces as it needs them.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class CompressedReportTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    private const string FirstNeedle = "zqxfirstpiece-11";
    private const string LastNeedle = "zqxlastpiece-99";
    private const string FlameNeedle = "zqxflamespan-42";
    private const int Diagrams = 12;

    private static ScenarioStep Composite(string text) => new()
    {
        Keyword = "Given", Text = text, Status = ExecutionResult.Passed,
        SubSteps = Enumerable.Range(1, 30).Select(i => new ScenarioStep
        {
            Keyword = "And", Text = $"{text} step {i} " + new string('x', 60), Status = ExecutionResult.Passed,
        }).ToArray(),
    };

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Ordering",
            Scenarios =
            [
                new Scenario { Id = "s1", DisplayName = "First scenario", Result = ExecutionResult.Passed, Steps = [Composite("the basket")] },
                new Scenario { Id = "s2", DisplayName = "Second scenario", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "an order", Status = ExecutionResult.Passed }] },
                new Scenario { Id = "s3", DisplayName = "Third scenario", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a flow", Status = ExecutionResult.Passed }] },
            ]
        }
    ];

    private static DiagramAsCode[] DiagramSources() => Enumerable.Range(0, Diagrams).Select(i => new DiagramAsCode(
        i < Diagrams / 2 ? "s1" : "s2", "",
        $"@startuml\nparticipant \"Caller\" as caller\nparticipant \"Svc{i}\" as svc\ncaller -> svc : GET: /api/{(i == 0 ? FirstNeedle : i == Diagrams - 1 ? LastNeedle : "call-" + i)}\nsvc --> caller : 200 OK\n@enduml")).ToArray();

    /// <summary>A report whose twelve diagram sources sit in several pieces, with a held step, a flame chart and the component diagram.</summary>
    private string Generate(string fileName, int pieceBytes = 400)
    {
        var flameJson = "{\"s\":[\"Kronikol.Tests\"],\"f\":[[0,\"" + FlameNeedle + "\",0,100,0,5]]}";
        var flame = $"<div class=\"iflow-flame\" data-diagram-type=\"flamechart\" data-flame-z=\"{InternalFlowHtmlGenerator.CompressToBase64(flameJson)}\"></div>";
        var precomputed = new Dictionary<string, Kronikol.Reports.Merge.WholeTestFlowFragment> { ["s3"] = new("", flame, 1) };
        string path;
        using (CompressedPage.WithPieceBytes(pieceBytes))
            path = ReportGenerator.GenerateHtmlReport(DiagramSources(), Features(), DateTime.UtcNow, DateTime.UtcNow, null,
                Path.Combine(TempDir, fileName), "Compressed Report", true,
                diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
                componentDiagramPlantUml: "@startuml\n[Orders] --> [Stock]\n@enduml",
                internalFlowTracking: true, wholeTestVisualization: WholeTestFlowVisualization.FlameChart,
                precomputedWholeTestContent: precomputed);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        Assert.True(HtmlReportFile.IsCompressed(File.ReadAllText(path)));
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>The same report as a plain page, written by <see cref="HtmlReportFile.ReadPlain"/> beside it.</summary>
    private string PlainCopy(string url)
    {
        var path = new Uri(url).LocalPath;
        var plain = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".plain.html");
        File.WriteAllText(plain, HtmlReportFile.ReadPlain(path));
        return new Uri(plain).AbsoluteUri;
    }

    private async Task<JsonElement> LoadAsync(IPage? page = null) =>
        await (page ?? Page).EvaluateAsync<JsonElement>("() => JSON.parse(JSON.stringify(window.__kronikolLoad || null))");

    private async Task DrawAllAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.querySelectorAll('details.feature, details.scenario').forEach(d => d.open = true)");
        await page.EvaluateAsync("() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
    }

    private static Task WaitForDiagramTextAsync(IPage page, string text, int timeoutMs = 60000) =>
        page.WaitForFunctionAsync(
            "t => Array.from(document.querySelectorAll('[data-diagram-type=\"plantuml\"] svg')).some(s => s.textContent.includes(t))",
            text, new() { Timeout = timeoutMs, PollingInterval = 200 });

    private static async Task<string[]> VisibleScenariosAsync(IPage page) =>
        await page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').map(s => s.querySelector('.scenario-name, summary') ? s.textContent.trim().slice(0, 40) : s.id)");

    private static async Task SearchAsync(IPage page, string query, int visible)
    {
        await FillSearchBar(page, query);
        await page.WaitForFunctionAsync(
            $"() => window.__kronikolSearch && window.__kronikolSearch.indexState === 'ready' && Array.from(document.querySelectorAll('.scenario')).filter(s => getComputedStyle(s).display !== 'none').length === {visible}",
            null, new() { Timeout = 30000, PollingInterval = 200 });
    }

    // ---------- test 8: the loading screen, then the report ----------

    [Fact]
    public async Task The_loading_screen_paints_and_then_the_report_takes_its_place_with_no_page_errors()
    {
        var errors = new List<string>();
        Page.PageError += (_, e) => errors.Add(e);
        await Page.AddInitScriptAsync("document.addEventListener('DOMContentLoaded', () => { window.__sawLoading = !!document.getElementById('kron-loading'); });");

        await Page.GotoAsync(Generate("Compressed_Loading.html"));
        await Page.WaitForReportAsync();

        Assert.True(await Page.EvaluateAsync<bool>("() => window.__sawLoading === true"));
        var load = await LoadAsync();
        Assert.True(load.GetProperty("painted").GetDouble() <= load.GetProperty("decoded").GetDouble());
        Assert.True(load.GetProperty("decoded").GetDouble() <= load.GetProperty("written").GetDouble());
        Assert.Contains(load.GetProperty("pagePath").GetString(), new[] { "native", "wasm", "fzstd" });
        Assert.Equal(JsonValueKind.Null, load.GetProperty("failure").ValueKind);
        Assert.Equal(0, await Page.Locator("#kron-loading").CountAsync());
        Assert.Equal("Compressed Report", await Page.TitleAsync());
        await Expect(Page.Locator("details.feature").First).ToBeVisibleAsync();
        Assert.Empty(errors);
    }

    // ---------- test 9: a deep link ----------

    [Fact]
    public async Task A_deep_link_lands_where_it_lands_in_the_plain_page()
    {
        var url = Generate("Compressed_DeepLink.html");
        var sid = Regex.Match(ReportFileText.Read(new Uri(url).LocalPath), "data-stable-id=\"([0-9a-f]+)\"[^>]*>(?:(?!data-stable-id).)*?Second scenario", RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(sid);

        async Task<(bool Open, double Top)> LandAsync(string address)
        {
            await Page.GotoReportAsync(address + "#sid-" + sid);
            await Page.WaitForFunctionAsync($"() => {{ var s = document.querySelector('[data-stable-id=\"{sid}\"]'); return s && s.open; }}", null, new() { PollingInterval = 200 });
            await WaitForScrollToSettleAsync();
            var top = await Page.EvaluateAsync<double>($"() => document.querySelector('[data-stable-id=\"{sid}\"]').getBoundingClientRect().top");
            return (await Page.EvaluateAsync<bool>($"() => document.querySelector('[data-stable-id=\"{sid}\"]').open"), top);
        }

        var plain = await LandAsync(PlainCopy(url));
        var compressed = await LandAsync(url);
        Assert.True(compressed.Open);
        Assert.Equal(plain.Open, compressed.Open);
        Assert.InRange(compressed.Top, plain.Top - 2, plain.Top + 2);
    }

    // ---------- test 10: Back and Forward stay in the document ----------

    [Fact]
    public async Task Back_and_Forward_after_in_page_navigation_stay_in_the_document_and_decode_nothing_again()
    {
        await Page.GotoReportAsync(Generate("Compressed_Back.html"));
        await Page.EvaluateAsync("() => { window.__sameDocument = true; }");
        var decoded = (await LoadAsync()).GetProperty("decoded").GetDouble();
        await Page.EvaluateAsync("() => { location.hash = '#one'; }");
        await Page.EvaluateAsync("() => { location.hash = '#two'; }");

        await Page.GoBackReportAsync();
        Assert.EndsWith("#one", Page.Url, StringComparison.Ordinal);
        await Page.GoForwardReportAsync();
        Assert.EndsWith("#two", Page.Url, StringComparison.Ordinal);

        Assert.True(await Page.EvaluateAsync<bool>("() => window.__sameDocument === true"));
        Assert.Equal(decoded, (await LoadAsync()).GetProperty("decoded").GetDouble());
    }

    // ---------- test 11: what the page draws ----------

    [Fact]
    public async Task Diagrams_from_the_first_and_the_last_piece_a_held_step_a_flame_chart_and_the_component_diagram_all_draw()
    {
        await Page.GotoReportAsync(Generate("Compressed_Draw.html"));
        var index = JsonSerializer.Deserialize<int[][]>(await Page.EvaluateAsync<string>("() => JSON.stringify(window.__kronikolFrames.index)"))!;
        Assert.True(index.Length >= 3, $"{index.Length} pieces");

        await DrawAllAsync(Page);
        await WaitForDiagramTextAsync(Page, FirstNeedle);
        await WaitForDiagramTextAsync(Page, LastNeedle);
        Assert.True((await LoadAsync()).GetProperty("pieceDecodes").GetInt32() >= 2);

        // The held step: its sub-steps come from #step-data, raw in the page frame.
        await Page.Locator("xpath=//details[contains(concat(' ', @class, ' '), ' step-collapsible ')]/summary").First.ClickAsync();
        await Page.WaitForFunctionAsync("() => !document.querySelector('.sub-steps[data-steps]')", null, new() { PollingInterval = 200 });
        await Expect(Page.GetByText("the basket step 30", new() { Exact = false }).First).ToBeVisibleAsync();

        // The flame chart: its data-flame-z holds the chart's JSON raw.
        await Page.WaitForFunctionAsync(
            $"() => {{ var f = document.querySelector('.iflow-flame'); if (!f) return false; f.closest('details') && (f.closest('details').open = true); return f.textContent.includes('{FlameNeedle}') || !!f.querySelector('[data-flame-rendered], svg, canvas'); }}",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        // The component diagram, a token like the others.
        await OpenReportTabAsync("components");
        await Page.WaitForFunctionAsync("() => !!document.querySelector('#panel-components svg')", null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    // ---------- test 12: deep search ----------

    [Fact]
    public async Task Deep_search_finds_text_only_in_diagram_sources_far_apart_and_only_in_a_flame_span_as_the_plain_page_does()
    {
        var url = Generate("Compressed_Search.html");
        var found = new Dictionary<string, string[]>();
        foreach (var address in new[] { PlainCopy(url), url })
        {
            await Page.GotoReportAsync(address);
            foreach (var needle in new[] { FirstNeedle, LastNeedle, FlameNeedle })
            {
                await SearchAsync(Page, needle, 1);
                found[$"{address.EndsWith(".plain.html", StringComparison.Ordinal)}:{needle}"] = await VisibleScenariosAsync(Page);
            }
        }
        foreach (var needle in new[] { FirstNeedle, LastNeedle, FlameNeedle })
            Assert.Equal(found[$"True:{needle}"], found[$"False:{needle}"]);
    }

    // ---------- test 13: the export ----------

    [Fact]
    public async Task The_export_of_a_compressed_report_is_compressed_and_opens_with_its_diagrams_and_held_steps()
    {
        await Page.GotoReportAsync(Generate("Compressed_Export.html"));
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await PressBoxExportAsync(Page, "Export Filtered HTML"));
        var path = Path.Combine(TempDir, "Compressed_Exported.html");
        await download.SaveAsAsync(path);
        File.Copy(path, Path.Combine(OutputDir, "Compressed_Exported.html"), true);

        var text = File.ReadAllText(path);
        Assert.True(HtmlReportFile.IsCompressed(text));
        Assert.Matches("<script id=\"kron-frame-page\" type=\"application/octet-stream\" data-codec=\"gzip\"", text);
        Assert.Contains("kronikol decode filtered-report.html -o plain.html", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Compressed_Export.html", text, StringComparison.Ordinal);
        Assert.Contains("data-kron-piece=\"1\"", text, StringComparison.Ordinal);
        Assert.Contains("Filtered Report", HtmlReportFile.ToPlain(text), StringComparison.Ordinal);

        var context = await OpenContextAsync(Page.Context.Browser!);
        var page = await OpenPageAsync(context);
        await page.GotoReportAsync(new Uri(path).AbsoluteUri);
        Assert.Equal("gzip", (await LoadAsync(page)).GetProperty("pagePath").GetString());
        await DrawAllAsync(page);
        await WaitForDiagramTextAsync(page, LastNeedle);
        await page.Locator("xpath=//details[contains(concat(' ', @class, ' '), ' step-collapsible ')]/summary").First.ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('.sub-steps[data-steps]')", null, new() { PollingInterval = 200 });
        await context.CloseAsync();
    }

    [Fact]
    public async Task Where_the_browser_cannot_compress_the_export_stores_its_page_frame_and_still_opens()
    {
        // Chrome before 80, Firefox before 113 and Safari before 16.4 have no CompressionStream, and open the report
        // with the page's own decoder.
        await Page.AddInitScriptAsync("delete window.CompressionStream;");
        await Page.GotoReportAsync(Generate("Compressed_ExportStored.html"));
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await PressBoxExportAsync(Page, "Export Filtered HTML"));
        var path = Path.Combine(TempDir, "Compressed_ExportedStored.html");
        await download.SaveAsAsync(path);

        var text = File.ReadAllText(path);
        Assert.Contains("<script id=\"kron-frame-page\" type=\"application/octet-stream\" data-codec=\"none\"", text, StringComparison.Ordinal);
        Assert.Contains("Filtered Report", HtmlReportFile.ToPlain(text), StringComparison.Ordinal);

        var context = await OpenContextAsync(Page.Context.Browser!);
        var page = await OpenPageAsync(context);
        await page.GotoReportAsync(new Uri(path).AbsoluteUri);
        Assert.Equal("none", (await LoadAsync(page)).GetProperty("pagePath").GetString());
        await DrawAllAsync(page);
        await WaitForDiagramTextAsync(page, LastNeedle);
        await context.CloseAsync();
    }

    // ---------- tests 14 and 15: the theme, and a damaged file ----------

    private static string Damaged(string url, Func<byte[], byte[]> damage)
    {
        var path = new Uri(url).LocalPath;
        var text = File.ReadAllText(path);
        var m = Regex.Match(text, "(<script id=\"kron-frame-page\"[^>]*>)([A-Za-z0-9+/=]+)(</script>)");
        File.WriteAllText(path, text[..m.Groups[2].Index] + Convert.ToBase64String(damage(Convert.FromBase64String(m.Groups[2].Value))) + text[(m.Groups[2].Index + m.Groups[2].Length)..]);
        return url;
    }

    [Theory]
    [InlineData("middle")]
    [InlineData("checksum")]
    public async Task A_damaged_page_frame_says_so_on_the_loading_screen_and_never_leaves_a_blank_page(string where)
    {
        var url = Damaged(Generate($"Compressed_Damaged_{where}.html"), bytes =>
        {
            var copy = (byte[])bytes.Clone();
            copy[where == "middle" ? copy.Length / 2 : copy.Length - 1] ^= 0x5a;
            return copy;
        });
        await Page.GotoAsync(url);
        await Page.WaitForFunctionAsync("() => window.__kronikolLoad && window.__kronikolLoad.failure", null, new() { Timeout = 20000, PollingInterval = 200 });
        await Expect(Page.Locator("#kron-loading[data-failed]")).ToBeVisibleAsync();
        await Expect(Page.Locator("#kron-loading")).ToContainTextAsync("This report could not be opened");
        // The file it names is this one, not TestRunReport.html.
        await Expect(Page.Locator("#kron-loading")).ToContainTextAsync($"kronikol decode Compressed_Damaged_{where}.html -o plain.html");
        Assert.Equal(0, await Page.Locator("details.feature").CountAsync());
    }

    [Fact]
    public async Task The_loading_screen_is_dark_in_the_dark_theme_and_the_theme_switch_works_after_the_rebuild()
    {
        await Context.AddInitScriptAsync("try { localStorage.setItem('kronikol.theme', 'dark'); } catch (e) { }");
        // A screen that stays: the frame cannot be decoded.
        await Page.GotoAsync(Damaged(Generate("Compressed_Dark.html"), b => b[..(b.Length / 2)]));
        await Expect(Page.Locator("#kron-loading[data-failed]")).ToBeVisibleAsync();
        Assert.Equal("rgb(14, 19, 26)", await Page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor"));
        Assert.Equal("dark", await Page.EvaluateAsync<string>("() => document.documentElement.getAttribute('data-theme')"));

        await Page.GotoReportAsync(Generate("Compressed_Theme.html"));
        Assert.Equal("dark", await Page.EvaluateAsync<string>("() => document.documentElement.getAttribute('data-theme')"));
        await Page.Locator("button.theme-toggle").ClickAsync();
        await Page.WaitForFunctionAsync("() => document.documentElement.getAttribute('data-theme') === 'light'", null, new() { PollingInterval = 200 });
    }

    // ---------- test 16: no workers ----------

    [Fact]
    public async Task With_workers_refused_the_pieces_are_decoded_on_the_page_and_the_diagrams_still_draw()
    {
        await Page.AddInitScriptAsync("window.Worker = function () { throw new Error('refused by the test'); };");
        await Page.GotoReportAsync(Generate("Compressed_NoWorkers.html"));
        await DrawAllAsync(Page);
        await WaitForDiagramTextAsync(Page, LastNeedle, 90000);
        Assert.True((await LoadAsync()).GetProperty("pieceDecodesOnPage").GetInt32() >= 1);
    }

    // ---------- test 17: Firefox's own zstd ----------

    [Fact]
    public async Task Firefox_with_its_zstd_preference_on_decodes_the_page_natively()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        IBrowser? firefox = null;
        try
        {
            firefox = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                FirefoxUserPrefs = new Dictionary<string, object> { ["dom.compression_streams.zstd.enabled"] = true },
            });
        }
        catch (PlaywrightException) { }
        Assert.SkipWhen(firefox is null, "firefox is not installed (CI installs chromium only)");
        await using var _ = firefox!;
        var page = await OpenPageAsync(firefox!);
        await page.GotoReportAsync(Generate("Compressed_Native.html"));
        Assert.Equal("native", (await LoadAsync(page)).GetProperty("pagePath").GetString());
        await DrawAllAsync(page);
        await WaitForDiagramTextAsync(page, LastNeedle, 90000);
    }

    // ---------- test 18: the suites' opening helpers ----------

    [Fact]
    public async Task The_opening_helpers_wait_for_the_rebuilt_report_and_a_report_init_script_sees_its_functions()
    {
        // The loader slowed down by 600 ms inside document.open: an Evaluate straight after the helper still finds the report.
        await Page.AddInitScriptAsync("""
            (function () {
                var open = Document.prototype.open;
                Document.prototype.open = function () { var until = performance.now() + 600; while (performance.now() < until) { } return open.apply(this, arguments); };
            })();
            """);
        await Page.AddReportInitScriptAsync("window.__hookSaw = typeof window.search_scenarios + ':' + !!document.querySelector('details.feature');");
        await Page.GotoReportAsync(Generate("Compressed_Helpers.html"));
        Assert.True(await Page.EvaluateAsync<bool>("() => !!document.querySelector('details.feature')"));
        Assert.Equal("function:true", await Page.EvaluateAsync<string>("() => window.__hookSaw"));

        await Page.ReloadReportAsync();
        Assert.True(await Page.EvaluateAsync<bool>("() => !!document.querySelector('details.feature')"));
    }

    // ---------- test 19: the loader declares no global name ----------

    [Fact]
    public async Task A_report_script_that_declares_the_loader_s_names_at_its_top_level_still_runs_after_the_rebuild()
    {
        var url = Generate("Compressed_Names.html");
        var path = new Uri(url).LocalPath;
        // Every name the loader and the decoder declare in their function, but those the report's own scripts declare.
        var report = ReportFileText.Read(path);
        var names = new[] { "report-loader.js", "report-zstd-decoder.js" }
            .SelectMany(file => Regex.Matches(ReportGenerator.LoadResource(file), "^    (?:var|function) ([A-Za-z_$][A-Za-z0-9_$]*)", RegexOptions.Multiline))
            .Select(m => m.Groups[1].Value).Distinct()
            .Where(name => !Regex.IsMatch(report, $@"\b(?:var|let|const|function|class)\s+{Regex.Escape(name)}\b"))
            .ToArray();
        Assert.True(names.Length >= 15, string.Join(", ", names));
        // After the page's first <head>: a report script's strings hold another "</head>".
        File.WriteAllText(path, CompressedPage.EditPage(File.ReadAllText(path), page => page.Insert(page.IndexOf("<head>", StringComparison.Ordinal) + "<head>".Length,
            $"<script>let {string.Join(", ", names.Select((n, i) => $"{n} = {i}"))}; window.__ownScriptRan = true;</script>")));
        var errors = new List<string>();
        Page.PageError += (_, e) => errors.Add(e);
        await Page.GotoReportAsync(url);
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__ownScriptRan === true"), string.Join("\n", errors));
        await DrawAllAsync(Page);
        await WaitForDiagramTextAsync(Page, FirstNeedle);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    // ---------- test 20: JavaScript off ----------

    [Fact]
    public async Task With_JavaScript_off_the_page_says_what_to_do()
    {
        var context = await OpenContextAsync(Page.Context.Browser!, new() { JavaScriptEnabled = false });
        var page = await OpenPageAsync(context);
        await page.GotoAsync(Generate("Compressed_NoScript.html"));
        // Playwright's text matching skips <noscript>, so the paragraph is found by its tag.
        await Expect(page.Locator("noscript p")).ToContainTextAsync("This report is compressed and needs JavaScript to open");
        await Expect(page.Locator("noscript p")).ToBeVisibleAsync();
        await Expect(page.Locator(".kron-loading-hint")).ToHaveCSSAsync("opacity", "1", new() { Timeout = 10000 });
        await Expect(page.Locator(".kron-loading-hint")).ToContainTextAsync("kronikol decode Compressed_NoScript.html -o plain.html");
        await context.CloseAsync();
    }
}
