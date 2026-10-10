using System.Text.Json;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A request whose URL is thousands of characters long used to produce one arrow statement past the
/// engine's 2000-character parse limit. PlantUML reports nothing for that: the statement matches no
/// rule, the parser abandons the whole diagram, and the fragment renders as <c>Syntax Error?</c> —
/// taking every other call in it down with it.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class LongStatementRenderingTests : PlaywrightTestBase
{
    public LongStatementRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    private static string LongUrlReportHtml()
    {
        var log = new RequestResponseLog(
            TestName: "A cold insights request reaches the cache", TestId: "long-url-1",
            Method: HttpMethod.Delete, Content: null,
            Uri: new Uri("http://example.com/data-insights-api/_v2/customer-local-competitors-charts-agg-"
                         + string.Join(",", Enumerable.Range(0, 60).Select(i => $"rWeeks-{i}-" + new string('k', 80)))),
            Headers: [], ServiceName: "redis", CallerName: "dataInsights",
            Type: RequestResponseType.Request, TraceId: Guid.NewGuid(), RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false);

        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId([log]).Single().PlantUmls.First().PlainText;
        var encoded = System.Net.WebUtility.HtmlEncode(source);

        return $$"""
            <!DOCTYPE html><html><head><title>long url</title>
            <style>{{DiagramContextMenu.GetInlineSvgStyles()}}</style>
            {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
            </head><body><div class="scenario">
            <div class="plantuml-browser" id="puml-1" data-plantuml="{{encoded}}" data-diagram-type="plantuml"></div>
            </div></body></html>
            """;
    }

    [Fact]
    public async Task A_trace_with_a_very_long_url_renders_without_a_syntax_error()
    {
        await Page.GotoAsync(ServePage(LongUrlReportHtml()));

        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120000, PollingInterval = 200 });

        Assert.Equal(0, await Page.Locator("[data-engine-failure]").CountAsync());
        Assert.Equal(1, await Page.Locator("#puml-1 svg").CountAsync());

        // The truncated label kept the method and the start of the path, and the note beside it still
        // carries the whole thing.
        var text = await Page.Locator("#puml-1").InnerTextAsync();
        Assert.Contains("DELETE", text);
        Assert.Contains("Full", text);
    }

    [Fact]
    public async Task An_over_long_statement_that_reaches_the_engine_is_named_in_the_failure_block()
    {
        // The backstop means Kronikol no longer emits one of these, so this drives the diagnosis path
        // with a hand-written diagram: if the limit ever moves, or a future emitter forgets, the report
        // says which line and how long instead of leaving a bare "Syntax Error?".
        var source = "@startuml\nAlice -> Bob: " + new string('x', 2500) + "\n@enduml";
        var encoded = System.Net.WebUtility.HtmlEncode(source);
        var html = $$"""
            <!DOCTYPE html><html><head><title>over long</title>
            <style>{{DiagramContextMenu.GetInlineSvgStyles()}}</style>
            {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
            </head><body><div class="scenario">
            <div class="plantuml-browser" id="puml-1" data-plantuml="{{encoded}}" data-diagram-type="plantuml"></div>
            </div></body></html>
            """;

        await Page.GotoAsync(ServePage(html));
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120000, PollingInterval = 200 });

        // The classifier is what turns the banner into a diagnosis; assert it directly, since whether
        // this particular diagram trips the banner depends on the engine's class-diagram fallback.
        var found = await Page.EvaluateAsync<JsonElement>(
            "(src) => window._findOverLongStatement(src) || {}", source);

        Assert.Equal("message statement", found.GetProperty("kind").GetString());
        Assert.Equal(2, found.GetProperty("line").GetInt32());
        Assert.Equal(2514, found.GetProperty("length").GetInt32());
        Assert.Equal(2000, found.GetProperty("limit").GetInt32());
    }

    // ── A long label inside an internal-flow link (plans/ENGINE_PIN_PLAN.md S0) ─────────────────────
    //
    // With internal-flow tracking on, the default, the request label sits inside [[#iflow-<id> …]] and a component
    // edge's method list inside [[#iflow-rel-… …]]. BrowserJs renders in a Chromium worker, whose stack is smaller
    // than the page's, and the engine overflowed it parsing a long link: the diagram's place held "RangeError:
    // Maximum call stack size exceeded" (the component diagram drew the engine's error picture) and nothing else.

    /// <summary>What a rendered diagram container holds: its text, and whether an SVG is in it.</summary>
    private sealed record Rendered(string Text, bool HasSvg);

    /// <summary>
    /// Opens the report on <paramref name="page"/>, renders every diagram in it (the component diagram's section is
    /// hidden until toggled, and the worker renders hidden diagrams too), and reads each container once it has rendered.
    /// </summary>
    private static async Task<(Rendered Sequence, Rendered Component)> RenderLongLinkedLabelReport(IPage page, string reportUri)
    {
        await page.GotoAsync(reportUri);
        await page.Locator("details.feature").First.WaitForAsync();
        await page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await page.WaitForFunctionAsync(
            "() => { const all = document.querySelectorAll('.plantuml-browser[data-plantuml]'); return all.length === 2 && Array.from(all).every(el => el.dataset.rendered === '1'); }",
            null, new() { Timeout = 120_000, PollingInterval = 200 });
        async Task<Rendered> Read(string selector) => new(
            await page.Locator(selector).First.EvaluateAsync<string>("el => el.textContent"),
            await page.Locator(selector + " svg").CountAsync() > 0);
        return (await Read(".scenario .plantuml-browser[data-plantuml]"), await Read("#component-diagram .plantuml-browser[data-plantuml]"));
    }

    private static void AssertDrawn(Rendered rendered, string expectedText)
    {
        Assert.DoesNotContain("Maximum call stack size exceeded", rendered.Text);
        Assert.DoesNotContain("Render error", rendered.Text);
        Assert.True(rendered.HasSvg, $"no diagram was drawn: {rendered.Text[..Math.Min(300, rendered.Text.Length)]}");
        Assert.Contains(expectedText, rendered.Text);
    }

    [Fact]
    public async Task Long_request_label_with_an_internal_flow_link_draws_in_the_worker()
    {
        var (sequence, _) = await RenderLongLinkedLabelReport(Page,
            ReportTestHelper.GenerateReportWithLongLinkedLabels(TempDir, OutputDir, "LongLinkedLabels.html"));

        Assert.Equal("worker", await Page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        // The label is cut inside its link, and the whole path is in the note beside the arrow.
        AssertDrawn(sequence, "Full path");
    }

    [Fact]
    public async Task Long_linked_labels_draw_in_a_worker_with_the_optimizing_compilers_off()
    {
        // A browser run with V8's optimizing compilers off, as an enterprise policy or a browser security mode can
        // set, runs every frame at the interpreter's size, and there the worker overflowed from 495 characters inside
        // a request's link and 475 inside a component edge's. WebAssembly stays on, so Graphviz lays the component
        // diagram out (V8's --jitless turns WebAssembly off too: the next fact).
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--js-flags=--no-opt --no-maglev"] });
        var page = await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        var (sequence, component) = await RenderLongLinkedLabelReport(page,
            ReportTestHelper.GenerateReportWithLongLinkedLabels(TempDir, OutputDir, "LongLinkedLabelsNoOpt.html"));

        Assert.Equal("worker", await page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        AssertDrawn(sequence, "Full path");
        AssertDrawn(component, "calls across");
    }

    [Fact]
    public async Task Long_linked_labels_draw_in_a_worker_under_jitless()
    {
        // V8's --jitless runs every frame at the interpreter's size, as the fact above does, and turns WebAssembly off,
        // so Graphviz cannot run: the page leaves it out and the engine lays the component diagram out with its
        // Smetana port. Before that, the component diagram drew the engine's "dot/GraphViz has crashed" picture.
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--js-flags=--jitless"] });
        var page = await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        var (sequence, component) = await RenderLongLinkedLabelReport(page,
            ReportTestHelper.GenerateReportWithLongLinkedLabels(TempDir, OutputDir, "LongLinkedLabelsJitless.html"));

        Assert.Equal("worker", await page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        Assert.False(await page.EvaluateAsync<bool>("() => window.__kronikolRender.webAssembly"));
        AssertDrawn(sequence, "Full path");
        AssertDrawn(component, "calls across");
        Assert.DoesNotContain("has crashed", component.Text);
    }

    [Fact]
    public async Task The_classifier_leaves_notes_comments_and_short_statements_alone()
    {
        await Page.GotoAsync(ServePage("""
            <!DOCTYPE html><html><head><title>classifier</title>
            """ + DiagramContextMenu.GetPlantUmlBrowserRenderScript() + """
            </head><body></body></html>
            """));

        var longRun = new string('n', 6000);
        var safe = "@startuml\nAlice -> Bob: Hello\n"
                   + "' a comment with a -> b: arrow and " + longRun + "\n"
                   + "hnote across #black:" + longRun + "\n"
                   + "note left\na -> b: payload, not a statement " + longRun + "\nend note\n"
                   + "@enduml";

        var found = await Page.EvaluateAsync<JsonElement?>("(src) => window._findOverLongStatement(src)", safe);
        Assert.True(found is null || found.Value.ValueKind == JsonValueKind.Null);
    }

    // ── The statements the render worker runs out of stack on (#162, plans/LONG_COMPONENT_EDGE_PLAN.md) ──────────
    //
    // The engine's regex library walks some statements once per character on its stack, and the worker's stack is half
    // the page's: a component edge's label failed from 550 characters with V8's optimizing compilers off and from 1,910
    // in a cold worker with the JIT on (Chromium 147), a step bar written as one token from 880 and 1,040, a `loop`
    // label from 840 and 1,010, a participant's name from 280 and 540, a span name in the activity diagram from 820
    // and 990, a swimlane's name from 540 and 1,000. Each now has a cap of its own, and each fact below draws a value
    // past the old limit through the emitter that writes it.

    /// <summary>
    /// <paramref name="container"/> holds a drawn diagram: an SVG that is not the engine's stack-overflow or error
    /// picture (the error pictures' first drawn line starts <c>PlantUML </c>, and they list the source, so they hold
    /// every name too) and that draws each of <paramref name="expected"/> at least the given number of times.
    /// </summary>
    private static async Task AssertDrawsAsADiagram(ILocator container, params (string Text, int Times)[] expected)
    {
        var verdict = await container.EvaluateAsync<JsonElement>("""
            el => {
              const svg = el.querySelector('svg');
              const texts = svg ? Array.from(svg.querySelectorAll('text')).map(t => t.textContent) : [];
              return { svg: !!svg, text: (svg ? svg.textContent : el.textContent || '').replace(/ /g, ' '), first: (texts[0] || '').trim() };
            }
            """);
        var text = verdict.GetProperty("text").GetString()!;
        Assert.True(verdict.GetProperty("svg").GetBoolean(), $"no diagram was drawn: {text[..Math.Min(300, text.Length)]}");
        Assert.DoesNotMatch("RangeError|Maximum call stack|too much recursion|Syntax Error|An error has occurred", text);
        Assert.False(verdict.GetProperty("first").GetString()!.StartsWith("PlantUML ", StringComparison.Ordinal),
            $"the engine's error picture: {text[..Math.Min(300, text.Length)]}");
        foreach (var (expectedText, times) in expected)
            Assert.True(text.Split(expectedText).Length - 1 >= times, $"'{expectedText}' is drawn fewer than {times} times");
    }

    private static async Task<IBrowser> LaunchWithTheOptimizingCompilersOff(IPlaywright playwright) =>
        await playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--js-flags=--no-opt --no-maglev"] });

    /// <summary>The run report's component panel and ComponentDiagram.html, each drawn in the worker.</summary>
    private static async Task AssertLongComponentEdgeDraws(IPage page, (string Report, string ComponentDiagram, string Caller, string Service) run)
    {
        var (reportUri, componentUri, caller, service) = run;
        (string, int)[] expected = [(caller, 1), (service, 1), ("66 calls across 1 tests", 1)];

        await page.GotoAsync(reportUri);
        await page.Locator("details.feature").First.WaitForAsync();
        await page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await page.WaitForFunctionAsync(
            "() => { const el = document.querySelector('#component-diagram .plantuml-browser[data-plantuml]'); return !!el && el.dataset.rendered === '1'; }",
            null, new() { Timeout = 120_000, PollingInterval = 200 });
        Assert.Equal("worker", await page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        await AssertDrawsAsADiagram(page.Locator("#component-diagram .plantuml-browser").First, expected);

        await page.GotoAsync(componentUri);
        await page.WaitForFunctionAsync(
            "() => { const el = document.getElementById('comp-diagram'); return !!el && (!!el.querySelector('svg') || (el.textContent || '').trim().length > 0); }",
            null, new() { Timeout = 120_000, PollingInterval = 200 });
        Assert.Equal("worker", await page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        await AssertDrawsAsADiagram(page.Locator("#comp-diagram"), expected);
    }

    [Fact]
    public async Task A_component_edge_with_many_statements_draws_in_the_worker()
    {
        await AssertLongComponentEdgeDraws(Page, ReportTestHelper.GenerateRunReportWithLongComponentEdge(TempDir, OutputDir, "LongComponentEdge"));
    }

    [Fact]
    public async Task A_component_edge_with_many_statements_draws_with_the_optimizing_compilers_off()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await LaunchWithTheOptimizingCompilersOff(playwright);
        var page = await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });
        await AssertLongComponentEdgeDraws(page, ReportTestHelper.GenerateRunReportWithLongComponentEdge(TempDir, OutputDir, "LongComponentEdgeNoOpt"));
    }

    /// <summary>A host-shaped name with no whitespace, <paramref name="length"/> characters long.</summary>
    private static string HostName(int length) =>
        string.Concat(Enumerable.Repeat("orders-archive-replica.eu-west-1.internal.", length / 20 + 2))[..length];

    private static string SequenceOf(params RequestResponseLog[] logs) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlainText;

    private static RequestResponseLog Call(string service, string caller = "Api", string? category = null) =>
        new("Capped kinds", "capped-1", HttpMethod.Get, null, new Uri("http://orders.internal/orders/1"), [], service, caller,
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false, null, RequestResponseMetaType.Default, category);

    private static RequestResponseLog[] Spliced(string plantUml, DiagramMarkerKind kind) =>
    [
        new("Capped kinds", "capped-1", "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
            { IsOverrideStart = true, MarkerKind = kind, PlantUml = "\n" + plantUml + "\n\n" },
        new("Capped kinds", "capped-1", "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
            { IsOverrideEnd = true, MarkerKind = kind },
    ];

    private static string ActivityOf(string spanName, string source)
    {
        var start = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        Kronikol.InternalFlow.FlowSpan[] spans =
        [
            new("trace1", "s1", null, "POST /orders", "OrderService", start, TimeSpan.FromMilliseconds(40)),
            new("trace1", "s2", "s1", spanName, source, start.AddMilliseconds(5), TimeSpan.FromMilliseconds(12)),
        ];
        var segment = new Kronikol.InternalFlow.InternalFlowSegment(Guid.NewGuid(), RequestResponseType.Request, "capped-1", null, null, []) { FlowSpans = spans };
        return Kronikol.InternalFlow.InternalFlowRenderer.RenderActivityDiagramBatched(segment).Single();
    }

    /// <summary>
    /// The emitter's source for one statement kind with a value past the limit it failed at, and what the drawn
    /// diagram must show.
    /// </summary>
    private static (string Source, (string Text, int Times)[] Expected) CappedKind(string kind)
    {
        var sql = string.Concat(Enumerable.Repeat("SELECT o.Id, o.Total FROM Orders AS o WHERE o.CustomerId = @p0 ", 15))[..900];
        return kind switch
        {
            // A step written as one token, a JSON array of about 1,600 characters, keeps the coloured form at the emitter's cap.
            "coloured step bar" => (SequenceOf([
                .. Spliced(Kronikol.Ingestion.InteractionRecord.StepDelimiterPlantUml(null,
                    "[" + string.Join(",", Enumerable.Range(1, 60).Select(i => $"{{\"sku\":\"SKU-{i:D4}\",\"n\":{i}}}")) + "]"), DiagramMarkerKind.Step),
                Call("OrderService")]), [("Api", 2), ("OrderService", 2)]),
            // A consumer's own PlantUML opening a block with a 1,200-character label.
            "spliced loop label" => (SequenceOf([
                .. Spliced("loop " + string.Concat(Enumerable.Repeat("retry the order sync ", 60))[..1195] + "\napi -> orderService: again\nend", DiagramMarkerKind.Custom),
                Call("OrderService")]), [("Api", 2), ("OrderService", 2)]),
            "service name" => (SequenceOf(Call(HostName(400))), [("Api", 2), ("orders-archive-replica", 2)]),
            "database name" => (SequenceOf(Call(HostName(400), category: "SQL")), [("Api", 2), ("orders-archive-replica", 2)]),
            "component name" => (Kronikol.ComponentDiagram.ComponentDiagramGenerator.GeneratePlantUml(
                [new("Caller", HostName(400), "HTTP", ["GET /orders"], 3, 1)], useC4: false), [("Caller", 1), ("orders-archive-replica", 1)]),
            "span name" => (ActivityOf(sql, "Microsoft.EntityFrameworkCore"), [("POST /orders", 1), ("SELECT o.Id", 1)]),
            "swimlane name" => (ActivityOf("SELECT 1", HostName(700)), [("POST /orders", 1), ("SELECT 1", 1), ("orders-archive-replica", 1)]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private string DiagramPage(string source, string name) => ServePage($$"""
        <!DOCTYPE html><html><head><title>{{name}}</title>
        <style>{{DiagramContextMenu.GetInlineSvgStyles()}}</style>
        {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
        </head><body><div class="scenario">
        <div class="plantuml-browser" id="puml-1" data-plantuml="{{System.Net.WebUtility.HtmlEncode(source)}}" data-diagram-type="plantuml"></div>
        </div></body></html>
        """, "CappedKind-" + name.Replace(' ', '-'));

    [Theory]
    [InlineData("coloured step bar", false)]
    [InlineData("coloured step bar", true)]
    [InlineData("spliced loop label", false)]
    [InlineData("spliced loop label", true)]
    [InlineData("service name", true)]
    [InlineData("database name", true)]
    [InlineData("component name", true)]
    [InlineData("span name", true)]
    [InlineData("swimlane name", true)]
    public async Task A_statement_past_its_old_limit_draws_in_the_worker(string kind, bool optimizingCompilersOff)
    {
        // The step bar and the loop label failed in a cold worker with the JIT on, the default, under caps of 1,400 and
        // 1,471 set in node; the names and the span name, which had no cap, failed with the optimizing compilers off.
        var (source, expected) = CappedKind(kind);
        using var playwright = optimizingCompilersOff ? await Microsoft.Playwright.Playwright.CreateAsync() : null;
        await using var browser = playwright is null ? null : await LaunchWithTheOptimizingCompilersOff(playwright);
        var page = browser is null ? Page : await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        await page.GotoAsync(DiagramPage(source, kind + (optimizingCompilersOff ? " no-opt" : "")));
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120_000, PollingInterval = 200 });

        Assert.Equal("worker", await page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        await AssertDrawsAsADiagram(page.Locator("#puml-1"), expected);
    }

    [Fact]
    public async Task The_classifier_names_each_statement_past_its_worker_cap()
    {
        // The page names the line a failed diagram choked on. It knew two caps, a message's and a block opener's at
        // 1,471; it now takes every cap from PlantUmlStatementLimits.
        await Page.GotoAsync(ServePage("""
            <!DOCTYPE html><html><head><title>classifier caps</title>
            """ + DiagramContextMenu.GetPlantUmlBrowserRenderScript() + """
            </head><body></body></html>
            """));
        var x = new string('x', 2000);
        (string Line, string Kind, int Length, int Limit)[] cases =
        [
            ("loop " + x[..700], "block label", 705, PlantUmlStatementLimits.MaxBlockLabelChars),
            ("hnote across <<stepDelimiter>> #black:<color:white>" + x[..700], "coloured note bar", 751, PlantUmlStatementLimits.MaxColouredNoteBarChars),
            ("entity \"" + x[..250] + "\" as shortAlias", "participant name", 250, PlantUmlStatementLimits.MaxParticipantNameChars),
            ("entity \"Short\" as " + x[..250], "participant alias", 250, PlantUmlStatementLimits.MaxParticipantNameChars),
            ("rectangle \"**" + x[..120] + "**\\n**" + x[..120] + "**\\n<size:10>[Software System]</size>\" as svc <<system>>", "participant name", 240, PlantUmlStatementLimits.MaxParticipantNameChars),
            ("caller -[#E74C3C]-> warehouse : \"" + x[..400] + "\"", "component edge label", 400, PlantUmlStatementLimits.MaxComponentEdgeLabelChars),
            ("|" + x[..250] + "|", "swimlane name", 250, PlantUmlStatementLimits.MaxParticipantNameChars),
            (":" + x[..700] + " (12ms);", "activity action", 700, PlantUmlStatementLimits.MaxActivityActionChars),
        ];

        foreach (var (line, kind, length, limit) in cases)
        {
            var found = await Page.EvaluateAsync<JsonElement>("(src) => window._findOverLongStatement(src) || {}", "@startuml\n" + line + "\n@enduml");
            Assert.True(found.TryGetProperty("kind", out var k), $"{kind} was not named");
            Assert.Equal(kind, k.GetString());
            Assert.Equal(2, found.GetProperty("line").GetInt32());
            Assert.Equal(length, found.GetProperty("length").GetInt32());
            Assert.Equal(limit, found.GetProperty("limit").GetInt32());
        }

        // At the cap, nothing is named: the check names only what is past it.
        var atCaps = "@startuml\nloop " + x[..(PlantUmlStatementLimits.MaxBlockLabelChars - 5)] + "\nentity \"" + x[..PlantUmlStatementLimits.MaxParticipantNameChars]
            + "\" as a\ncaller -[#E74C3C]-> warehouse : \"" + x[..PlantUmlStatementLimits.MaxComponentEdgeLabelChars] + "\"\n@enduml";
        var none = await Page.EvaluateAsync<JsonElement?>("(src) => window._findOverLongStatement(src)", atCaps);
        Assert.True(none is null || none.Value.ValueKind == JsonValueKind.Null, $"named a statement at its cap: {none}");
    }
}
