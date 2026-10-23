using System.Text.Json;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The engine a report page draws with (5.0.0, plans/WASM_ENGINE_PLAN.md): PlantUML's Wasm GC build in the render workers
/// wherever the browser runs Wasm GC and lets a script build functions from text, and plantuml.js of the same build
/// everywhere else, on the main thread among them. A Wasm engine that passes its hash check and does not start gives way
/// to plantuml.js, and a worker an error takes down while it draws is replaced, its diagram drawn once more and then
/// failed alone. The stub engines (<see cref="StubEngines"/>) are served from a loopback server beside the page, and the
/// page is told their hashes, as a mirror would be: nothing the page requests is mocked.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class WasmEngineTests : PlaywrightTestBase
{
    public WasmEngineTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>Every engine file the page asks for, by name.</summary>
    private List<string> EngineRequests()
    {
        var requests = new List<string>();
        Page.Request += (_, request) =>
        {
            var name = request.Url[(request.Url.LastIndexOf('/') + 1)..];
            if (name is "plantuml.js" or StubEngines.WasmBundle or "viz-global.js") lock (requests) requests.Add(name);
        };
        return requests;
    }

    private List<string> ConsoleMessages()
    {
        var messages = new List<string>();
        Page.Console += (_, m) => { lock (messages) messages.Add(m.Type + ": " + m.Text); };
        return messages;
    }

    private async Task<JsonElement> RenderTheFirstDiagram()
    {
        await Page.WaitForFunctionAsync("() => document.body.classList.contains('plantuml-ready')", null, new() { Timeout = 60000, PollingInterval = 200 });
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        // the first diagram sits in a section that may be closed: drawn is attached, not visible
        await Page.Locator(".plantuml-browser svg").First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 120000 });
        return await Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
    }

    /// <summary>
    /// Draws each source through <c>window.plantuml.render</c> into a fresh element and answers, per source, what it
    /// drew (<c>drawn</c>, the SVG's text) or the failure its <c>onError</c> was handed (<c>error</c>).
    /// </summary>
    private Task<JsonElement> RenderEach(params string[] sources) => Page.EvaluateAsync<JsonElement>("""
        (sources) => new Promise(function (resolve) {
            var out = {}, left = sources.length;
            function settle(i, value) { if (out[i] !== undefined) return; out[i] = value; if (--left === 0) resolve(out); }
            // A promise that never settles hangs the whole test run (EvaluateAsync has no timeout).
            setTimeout(function () { resolve(out); }, 90000);
            sources.forEach(function (source, i) {
                var el = document.createElement('div'); el.id = 'each-' + i + '-' + Math.random().toString(36).slice(2); document.body.appendChild(el);
                new MutationObserver(function (m, mo) {
                    if (!el.querySelector('svg')) return; mo.disconnect(); settle(i, { drawn: el.textContent });
                }).observe(el, { childList: true, subtree: true });
                window.plantuml.render(source.split('\n'), el.id, { onError: function (message) { settle(i, { error: message }); return true; } });
            });
        })
        """, sources);

    /// <summary>
    /// A page holding only the render script, with its Wasm engine pointed at <paramref name="bundle"/>, served beside it.
    /// </summary>
    private (LoopbackFileServer Server, string PageUrl) StubPage(string name, string bundle)
    {
        var dir = Directory.CreateDirectory(Path.Combine(TempDir, name)).FullName;
        var page = Path.Combine(dir, name + ".html");
        File.WriteAllText(page, $$"""
            <!DOCTYPE html><html><head><meta charset="utf-8"><title>{{name}}</title>
            {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
            </head><body></body></html>
            """);
        return StubEngines.Serve(page, bundle, name + "-stub.js");
    }

    /// <summary>
    /// A stub whose CRASH source throws from a timer in the worker, as an engine's crash reaches the page in Firefox, and
    /// whose OVERFLOW source throws the stack overflow's RangeError the same way; any other source draws its second line.
    /// </summary>
    private static readonly string CrashingBundle = StubEngines.Bundle("""
        var source = lines.join('\n');
        if (source.indexOf('OVERFLOW') >= 0) {
            setTimeout(function () { throw new RangeError('Maximum call stack size exceeded'); }, 0);
            return new Promise(function () {});
        }
        if (source.indexOf('CRASH') >= 0) {
            setTimeout(function () { throw new Error('the stub engine crashed'); }, 0);
            return new Promise(function () {});
        }
        draw(['drawn: ' + lines[1]]);
        """);

    [Fact]
    public async Task Chromium_draws_with_the_wasm_engine_in_its_workers()
    {
        var requests = EngineRequests();
        await Page.GotoReportAsync(GenerateReport("WasmEngineChromium.html"));

        var r = await RenderTheFirstDiagram();

        Assert.Equal("worker", r.GetProperty("mode").GetString());
        Assert.Equal("wasm", r.GetProperty("engine").GetString());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("engineProblem").ValueKind);
        Assert.Equal("verified", r.GetProperty("engineIntegrity").GetString());
        Assert.Equal(0, r.GetProperty("workerCrashes").GetInt32());
        lock (requests)
        {
            Assert.Contains(StubEngines.WasmBundle, requests);
            Assert.DoesNotContain("plantuml.js", requests);
        }
    }

    [Fact]
    public async Task The_main_thread_draws_with_the_javascript_engine()
    {
        var requests = EngineRequests();
        await Page.GotoReportAsync(LargeReportFixture.Generate(TempDir, OutputDir, "WasmEngineMainThread.html", diagrams: 2, stepsPerDiagram: 2, browserRenderWorkers: 0));

        var r = await RenderTheFirstDiagram();

        Assert.Equal("main-thread", r.GetProperty("mode").GetString());
        Assert.Equal("js", r.GetProperty("engine").GetString());
        Assert.Equal("the page draws on its main thread", r.GetProperty("engineProblem").GetString());
        Assert.Equal("verified", r.GetProperty("engineIntegrity").GetString());
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => document.querySelectorAll('script[src$=\"/plantuml.js\"]').length"));
        lock (requests) Assert.DoesNotContain(StubEngines.WasmBundle, requests);
    }

    [Fact]
    public async Task A_policy_that_refuses_functions_built_from_text_draws_with_the_javascript_engine()
    {
        // WebAssembly compiles here, so Graphviz loads, but the Wasm engine's runtime builds functions from text, which a
        // policy without 'unsafe-eval' refuses: the page asks first and fetches plantuml.js instead.
        const string policy = "default-src 'none'; script-src 'unsafe-inline' 'wasm-unsafe-eval' https://cdn.jsdelivr.net blob:; worker-src blob:; "
                              + "connect-src https://cdn.jsdelivr.net; style-src 'unsafe-inline'; img-src data: blob:; font-src data:";
        var uri = GenerateReportWithEmbeddedComponentDiagram("WasmEnginePolicy.html");
        var path = new Uri(uri).LocalPath;
        var html = File.ReadAllText(path);
        var head = html.IndexOf("<head>", StringComparison.Ordinal);
        Assert.True(head >= 0, "the report has no <head>");
        File.WriteAllText(path, html.Insert(head + "<head>".Length, $"<meta http-equiv=\"Content-Security-Policy\" content=\"{policy}\">"));
        var requests = EngineRequests();
        var console = ConsoleMessages();
        await Page.GotoReportAsync(uri);

        var r = await RenderTheFirstDiagram();

        Assert.Equal("worker", r.GetProperty("mode").GetString());
        Assert.Equal("js", r.GetProperty("engine").GetString());
        Assert.StartsWith("functions cannot be built from text: ", r.GetProperty("engineProblem").GetString());
        Assert.True(r.GetProperty("webAssembly").GetBoolean());
        Assert.Equal("verified", r.GetProperty("engineIntegrity").GetString());
        Assert.Equal("verified", r.GetProperty("vizIntegrity").GetString());
        lock (requests)
        {
            Assert.Contains("plantuml.js", requests);
            Assert.DoesNotContain(StubEngines.WasmBundle, requests);
        }
        lock (console)
            Assert.Contains(console, m => m.StartsWith("info: Kronikol: the PlantUML Wasm engine cannot run here (functions cannot be built from text: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_wasm_engine_that_does_not_start_gives_way_to_the_javascript_engine()
    {
        var report = new Uri(GenerateReport("WasmEngineDoesNotStart.html")).LocalPath;
        var (server, pageUrl) = StubEngines.Serve(report,
            StubEngines.Bundle("draw(['drawn: ' + lines[1]]);", "Promise.reject(new Error('the stub engine cannot start'))"), "stub-does-not-start.js");
        await using var _ = server;
        var console = ConsoleMessages();
        await Page.GotoReportAsync(pageUrl);

        var r = await RenderTheFirstDiagram();

        // drawn by the JavaScript engine from the CDN, in workers
        Assert.Equal("worker", r.GetProperty("mode").GetString());
        Assert.Equal("js", r.GetProperty("engine").GetString());
        Assert.Contains("the stub engine cannot start", r.GetProperty("engineProblem").GetString());
        Assert.Equal("verified", r.GetProperty("engineIntegrity").GetString());
        Assert.DoesNotContain("drawn: ", await Page.Locator(".plantuml-browser").First.TextContentAsync());
        lock (console)
            Assert.Contains(console, m => m.StartsWith("warning: Kronikol: the PlantUML Wasm engine did not start (", StringComparison.Ordinal)
                                          && m.Contains("the stub engine cannot start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_worker_an_error_takes_down_is_replaced_and_its_diagram_fails_alone_after_a_second_try()
    {
        var (server, pageUrl) = StubPage("wasm-engine-crash", CrashingBundle);
        await using var _ = server;
        await Page.GotoReportAsync(pageUrl);

        var outcome = await RenderEach("@startuml\nfirst\n@enduml", "@startuml\nCRASH\n@enduml", "@startuml\nthird\n@enduml");

        Assert.Equal("drawn: first", outcome.GetProperty("0").GetProperty("drawn").GetString());
        Assert.Equal("drawn: third", outcome.GetProperty("2").GetProperty("drawn").GetString());
        var error = outcome.GetProperty("1").GetProperty("error").GetString();
        Assert.StartsWith("the PlantUML engine crashed drawing this diagram: ", error);
        Assert.Contains("the stub engine crashed", error);
        var r = await Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
        Assert.Equal("wasm", r.GetProperty("engine").GetString());
        Assert.Equal(2, r.GetProperty("workerCrashes").GetInt32());
        Assert.True(r.GetProperty("errors").GetInt32() >= 1);
        // the page still draws: a later diagram goes to a worker that took a crashed one's place
        Assert.Equal("drawn: later", (await RenderEach("@startuml\nlater\n@enduml")).GetProperty("0").GetProperty("drawn").GetString());
    }

    [Fact]
    public async Task A_stack_overflow_that_takes_a_worker_down_fails_its_diagram_at_once()
    {
        // An overflow is the source's, not the worker's: drawn again, it overflows again. So the diagram fails at the
        // first crash, naming the overflow, which the page then describes as the engine's stack failure.
        var (server, pageUrl) = StubPage("wasm-engine-overflow", CrashingBundle);
        await using var _ = server;
        await Page.GotoReportAsync(pageUrl);

        var outcome = await RenderEach("@startuml\nfirst\n@enduml", "@startuml\nOVERFLOW\n@enduml");

        Assert.Equal("drawn: first", outcome.GetProperty("0").GetProperty("drawn").GetString());
        var error = outcome.GetProperty("1").GetProperty("error").GetString();
        Assert.StartsWith("the PlantUML engine crashed drawing this diagram: ", error);
        Assert.Contains("Maximum call stack size exceeded", error);
        Assert.True(await Page.EvaluateAsync<bool>("(e) => window._isStackOverflow(e)", error));
        Assert.Equal(1, (await Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender")).GetProperty("workerCrashes").GetInt32());
    }

    [Fact]
    public async Task A_diagram_nested_past_the_wasm_engines_stack_fails_alone_and_the_page_goes_on()
    {
        // 5.0.0's engine draws any length of statement Kronikol writes, but nesting this deep still runs it out of stack
        // (plans/WASM_ENGINE_PLAN.md): the real engine, in a worker, fails that diagram with the overflow named and draws
        // the next one.
        await Page.GotoReportAsync(ServePage($$"""
            <!DOCTYPE html><html><head><meta charset="utf-8"><title>nested</title>
            {{DiagramContextMenu.GetPlantUmlBrowserRenderScript()}}
            </head><body></body></html>
            """));
        var nested = "@startuml\n" + string.Concat(Enumerable.Repeat("group g\n", 20000)) + "a -> b: x\n" + string.Concat(Enumerable.Repeat("end\n", 20000)) + "@enduml";

        var outcome = await RenderEach(nested, "@startuml\nAlice -> Bob: after\n@enduml");

        var first = outcome.GetProperty("0");
        var failure = first.TryGetProperty("error", out var e) ? e.GetString()! : first.GetProperty("drawn").GetString()!;
        Assert.True(await Page.EvaluateAsync<bool>("(e) => window._isStackOverflow(e)", failure), $"not a stack failure: {failure[..Math.Min(300, failure.Length)]}");
        Assert.Contains("after", outcome.GetProperty("1").GetProperty("drawn").GetString());
        Assert.Equal("wasm", (await Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender")).GetProperty("engine").GetString());
    }
}
