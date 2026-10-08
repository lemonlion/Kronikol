using System.Text.Json;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Graphviz, which lays the component diagram out, is WebAssembly. Where no WebAssembly module can be compiled (V8's
/// <c>--jitless</c>, a content security policy without <c>'wasm-unsafe-eval'</c>, a browser or policy that turns
/// WebAssembly off), <c>viz-global.js</c> still loaded, the engine handed it the diagram, and the diagram's place held
/// the engine's "dot/GraphViz has crashed" picture, in the worker and on the main thread alike. The engine lays a
/// diagram out with its Smetana port when Graphviz is absent, so the page loads Graphviz only where a module compiles.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class WebAssemblyOffRenderingTests : PlaywrightTestBase
{
    public WebAssemblyOffRenderingTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>What a rendered diagram container holds: its text, and whether an SVG is in it.</summary>
    private sealed record Rendered(string Text, bool HasSvg);

    /// <summary>The report's first sequence diagram and its component diagram once every diagram has rendered, the
    /// page's render telemetry, and what the console said.</summary>
    private sealed record Outcome(Rendered Sequence, Rendered Component, JsonElement Telemetry, IReadOnlyList<string> Console);

    private static async Task<Outcome> RenderComponentReport(IPage page, string reportUri)
    {
        var console = new List<string>();
        page.Console += (_, m) => { lock (console) console.Add(m.Type + ": " + m.Text); };
        await page.GotoAsync(reportUri);
        await page.Locator("details.feature").First.WaitForAsync();
        // The component diagram's section is hidden until toggled; this renders every diagram in the report.
        await page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await page.WaitForFunctionAsync(
            "() => { const all = document.querySelectorAll('.plantuml-browser[data-plantuml]'); return all.length > 1 && Array.from(all).every(el => el.dataset.rendered === '1'); }",
            null, new() { Timeout = 180_000, PollingInterval = 200 });
        async Task<Rendered> Read(string selector) => new(
            await page.Locator(selector).First.EvaluateAsync<string>("el => el.textContent"),
            await page.Locator(selector + " svg").CountAsync() > 0);
        var sequence = await Read(".scenario .plantuml-browser[data-plantuml]");
        var component = await Read("#component-diagram .plantuml-browser[data-plantuml]");
        var telemetry = await page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
        lock (console) return new(sequence, component, telemetry, console.ToList());
    }

    /// <summary>The diagram drew: an SVG holding the expected names, and not the engine's error picture, which is an
    /// SVG too.</summary>
    private static void AssertDrawn(Rendered rendered, params string[] expected)
    {
        var excerpt = rendered.Text[..Math.Min(300, rendered.Text.Length)];
        Assert.True(rendered.HasSvg, $"no diagram was drawn: {excerpt}");
        Assert.False(rendered.Text.Contains("has crashed", StringComparison.Ordinal), $"the engine drew its error picture: {excerpt}");
        Assert.False(rendered.Text.Contains("Render error", StringComparison.Ordinal), excerpt);
        foreach (var name in expected) Assert.Contains(name, rendered.Text);
    }

    /// <summary>The page found no WebAssembly, never fetched Graphviz, and said why on the console.</summary>
    private static void AssertGraphvizLeftOut(Outcome outcome, string reason)
    {
        Assert.False(outcome.Telemetry.GetProperty("webAssembly").GetBoolean());
        Assert.Equal(JsonValueKind.Null, outcome.Telemetry.GetProperty("vizIntegrity").ValueKind);
        Assert.Equal("verified", outcome.Telemetry.GetProperty("engineIntegrity").GetString());
        Assert.Contains(outcome.Console, m => m.StartsWith("warning: Kronikol: WebAssembly is not available here (", StringComparison.Ordinal)
                                              && m.Contains(reason, StringComparison.Ordinal)
                                              && m.Contains("Graphviz is not loaded", StringComparison.Ordinal)
                                              && m.Contains("Smetana", StringComparison.Ordinal));
    }

    private static Task<IBrowser> LaunchJitless(IPlaywright playwright) =>
        playwright.Chromium.LaunchAsync(new() { Headless = true, Args = ["--js-flags=--jitless"] });

    [Fact]
    public async Task Component_diagram_draws_in_the_worker_under_jitless()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await LaunchJitless(playwright);
        var page = await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        var outcome = await RenderComponentReport(page,
            ReportTestHelper.GenerateReportWithEmbeddedComponentDiagram(TempDir, OutputDir, "WasmOffJitlessWorker.html"));

        AssertDrawn(outcome.Sequence, "OrderService");
        AssertDrawn(outcome.Component, "Client", "API", "CosmosDB", "ServiceBus");
        Assert.Equal("worker", outcome.Telemetry.GetProperty("mode").GetString());
        AssertGraphvizLeftOut(outcome, "WebAssembly is not defined");
    }

    [Fact]
    public async Task Component_diagram_draws_on_the_main_thread_under_jitless()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await LaunchJitless(playwright);
        var page = await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        var outcome = await RenderComponentReport(page,
            ReportTestHelper.GenerateReportWithEmbeddedComponentDiagram(TempDir, OutputDir, "WasmOffJitlessMainThread.html", browserRenderWorkers: 0));

        AssertDrawn(outcome.Sequence, "OrderService");
        AssertDrawn(outcome.Component, "Client", "API", "CosmosDB", "ServiceBus");
        Assert.Equal("main-thread", outcome.Telemetry.GetProperty("mode").GetString());
        AssertGraphvizLeftOut(outcome, "WebAssembly is not defined");
    }

    [Fact]
    public async Task Component_diagram_draws_under_a_content_security_policy_without_wasm_unsafe_eval()
    {
        // A report served with a policy that lets its own scripts, the engine and the render worker run, but compiles
        // no WebAssembly: WebAssembly is defined there, and compiling a module throws.
        const string policy = "default-src 'none'; script-src 'unsafe-inline' https://cdn.jsdelivr.net blob:; worker-src blob:; "
                              + "connect-src https://cdn.jsdelivr.net; style-src 'unsafe-inline'; img-src data: blob:; font-src data:";
        var uri = ReportTestHelper.GenerateReportWithEmbeddedComponentDiagram(TempDir, OutputDir, "WasmOffCsp.html");
        var path = new Uri(uri).LocalPath;
        var html = File.ReadAllText(path);
        var head = html.IndexOf("<head>", StringComparison.Ordinal);
        Assert.True(head >= 0, "the report has no <head>");
        File.WriteAllText(path, html.Insert(head + "<head>".Length, $"<meta http-equiv=\"Content-Security-Policy\" content=\"{policy}\">"));

        var outcome = await RenderComponentReport(Page, uri);

        AssertDrawn(outcome.Sequence, "OrderService");
        AssertDrawn(outcome.Component, "Client", "API", "CosmosDB", "ServiceBus");
        Assert.Equal("worker", outcome.Telemetry.GetProperty("mode").GetString());
        AssertGraphvizLeftOut(outcome, "Content Security");
    }

    [Fact]
    public async Task Graphviz_lays_the_component_diagram_out_where_webassembly_compiles()
    {
        // The control: with WebAssembly on, Graphviz is fetched, checked and used, and the engine never falls back.
        var outcome = await RenderComponentReport(Page,
            ReportTestHelper.GenerateReportWithEmbeddedComponentDiagram(TempDir, OutputDir, "WasmOn.html"));

        AssertDrawn(outcome.Sequence, "OrderService");
        AssertDrawn(outcome.Component, "Client", "API", "CosmosDB", "ServiceBus");
        Assert.Equal("worker", outcome.Telemetry.GetProperty("mode").GetString());
        Assert.Equal("verified", outcome.Telemetry.GetProperty("vizIntegrity").GetString());
        Assert.DoesNotContain(outcome.Console, m => m.Contains("Smetana", StringComparison.Ordinal) || m.Contains("WebAssembly", StringComparison.Ordinal));
        Assert.True(outcome.Telemetry.GetProperty("webAssembly").GetBoolean());
    }
}
