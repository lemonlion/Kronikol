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
        // diagram out (V8's --jitless turns WebAssembly off too, and then no component diagram draws at all).
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--js-flags=--no-opt --no-maglev"] });
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        var (sequence, component) = await RenderLongLinkedLabelReport(page,
            ReportTestHelper.GenerateReportWithLongLinkedLabels(TempDir, OutputDir, "LongLinkedLabelsNoOpt.html"));

        Assert.Equal("worker", await page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        AssertDrawn(sequence, "Full path");
        AssertDrawn(component, "calls across");
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
}
