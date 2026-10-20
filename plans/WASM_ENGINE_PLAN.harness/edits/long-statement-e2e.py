# LongStatementRenderingTests on 5.0.0's engine (plans/WASM_ENGINE_PLAN.md): it draws the long statements that ran the
# 4.x engine out of stack, so the facts on the page's handling of a stack failure take sources that still overflow it
# (deep nesting: a crash in the Wasm worker, text on the main thread), and the facts on the 4.x engine's pictures (the
# stack picture, Syntax Error? under an over-long action) take a stub engine drawing them. usage: python -I long-statement-e2e.py <repo root>
import sys

root = sys.argv[1]
p = root + '/tests/Kronikol.Tests.EndToEnd/LongStatementRenderingTests.cs'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

PICTURE = """
    /// <summary>
    /// A sequence diagram whose one message sits inside <paramref name="depth"/> nested groups: 5.0.0's engine runs out of
    /// stack on it (from about 2,000 in node), as a crash in the Wasm worker and as text on the main thread.
    /// </summary>
    private static string NestedGroupsDiagram(int depth = 20000) =>
        "@startuml\\n" + string.Concat(Enumerable.Repeat("group g\\n", depth)) + "a -> b: x\\n" + string.Concat(Enumerable.Repeat("end\\n", depth)) + "@enduml";

    /// <summary>
    /// A stub of the Wasm engine that draws, for every source, the 4.x engine's error picture: its version line, the line it
    /// stopped at (<paramref name="line"/>) and <paramref name="last"/>, as that engine drew a stack overflow or a
    /// <c>Syntax Error?</c>. 5.0.0's engine draws neither for any source Kronikol writes.
    /// </summary>
    private static string PictureEngine(int line, string last) => StubEngines.Bundle(
        "draw(['PlantUML version 1.2026.8', '[From textarea (line " + line + ") ]', '@startuml', " + JsonSerializer.Serialize(last) + "]);");
"""

pairs = [
    # --- the stack picture, drawn by a stub, and the real engine's overflow on deep nesting --------------------------
    ("""    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task A_diagram_the_engine_runs_out_of_stack_on_is_reported_as_a_failure(int workers)
    {
        // Past its stack the engine draws a picture of its own error (RangeError: Maximum call stack size exceeded), which
        // the page took for a drawn diagram: it counted no error, cached the picture and said nothing about the cause.
        // Drawn with the optimizing compilers off, where the worker fails from 550 characters of edge label and the main
        // thread from 1,080.
        var (source, labelLength) = OverflowingComponentDiagram();
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await LaunchWithTheOptimizingCompilersOff(playwright);
        var page = await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        await page.GotoAsync(DiagramPage(source, $"stack overflow w{workers}", workers));
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120_000, PollingInterval = 200 });

        Assert.Equal(workers == 0 ? "main-thread" : "worker", await page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        var note = page.Locator("#puml-1 [data-engine-failure='stack']");
        Assert.Equal(1, await note.CountAsync());
        var text = await note.InnerTextAsync();
        Assert.Contains("ran out of stack", text);
        Assert.Contains($"Line 6 is a component edge label of {labelLength} characters (limit {PlantUmlStatementLimits.MaxComponentEdgeLabelChars})", text);
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__kronikolRender.errors"));
        // The engine's own picture stays, below Kronikol's line.
        Assert.Equal(1, await page.Locator("#puml-1 svg").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>(
            "() => { const el = document.querySelector('#puml-1'); return el.firstElementChild === el.querySelector(\\"[data-engine-failure='stack']\\"); }"),
            "Kronikol's line is not above the engine's picture");

        if (workers > 0)
        {
""",
     PICTURE.lstrip('\n') + """
    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task A_diagram_nested_past_the_engines_stack_is_reported_as_a_failure(int workers)
    {
        // Past its stack the engine failed in a way the page took for a drawn diagram: it counted no error, cached the
        // failure and said nothing about the cause. 5.0.0's engine draws the long statements that did it on the 4.x engine
        // (plans/WASM_ENGINE_PLAN.md), and runs out of stack on nesting this deep: the Wasm engine in a worker crashes,
        // and the JavaScript engine on the main thread writes its RangeError as text.
        var source = NestedGroupsDiagram();

        await Page.GotoAsync(DiagramPage(source, $"stack overflow w{workers}", workers));
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120_000, PollingInterval = 200 });

        Assert.Equal(workers == 0 ? "main-thread" : "worker", await Page.EvaluateAsync<string>("() => window.__kronikolRender.mode"));
        var note = Page.Locator("#puml-1 [data-engine-failure='stack']");
        Assert.Equal(1, await note.CountAsync());
        Assert.Contains("ran out of stack", await note.InnerTextAsync());
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__kronikolRender.errors"));
        await AssertTheSameSourceIsDrawnAgain(Page, source, workers);
    }

    [Fact]
    public async Task A_stack_picture_the_engine_draws_is_named_above_it_and_not_cached()
    {
        // Past its stack the 4.x engine drew a picture of its own error (RangeError: Maximum call stack size exceeded),
        // which the page took for a drawn diagram. The picture stays, with Kronikol's line above it naming the statement
        // past its cap; 5.0.0's engine draws no such picture, so a stub engine draws the 4.x engine's.
        var (source, labelLength) = OverflowingComponentDiagram();
        var (server, pageUrl) = StubEngines.Serve(new Uri(DiagramPage(source, "stack picture")).LocalPath,
            PictureEngine(6, " java.lang.RuntimeException: (JavaScript) RangeError: Maximum call stack size exceeded (Assumed diagram type: component)"),
            "stack-picture-engine.js");
        await using var _ = server;

        await Page.GotoAsync(pageUrl);
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#puml-1')?.getAttribute('data-rendered') === '1'",
            null, new() { Timeout = 120_000, PollingInterval = 200 });

        var note = Page.Locator("#puml-1 [data-engine-failure='stack']");
        Assert.Equal(1, await note.CountAsync());
        var text = await note.InnerTextAsync();
        Assert.Contains("ran out of stack", text);
        Assert.Contains($"Line 6 is a component edge label of {labelLength} characters (limit {PlantUmlStatementLimits.MaxComponentEdgeLabelChars})", text);
        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__kronikolRender.errors"));
        // The engine's own picture stays, below Kronikol's line.
        Assert.Equal(1, await Page.Locator("#puml-1 svg").CountAsync());
        Assert.True(await Page.EvaluateAsync<bool>(
            "() => { const el = document.querySelector('#puml-1'); return el.firstElementChild === el.querySelector(\\"[data-engine-failure='stack']\\"); }"),
            "Kronikol's line is not above the engine's picture");
        await AssertTheSameSourceIsDrawnAgain(Page, source, workers: 1);
    }

    /// <summary>A stack failure is not cached: the same source asked for again is drawn again, and counted again.</summary>
    private static async Task AssertTheSameSourceIsDrawnAgain(IPage page, string source, int workers)
    {
        if (workers > 0)
        {
"""),
    ("""            Assert.Equal(0, hits);
            Assert.Equal(2, await page.EvaluateAsync<int>("() => window.__kronikolRender.errors"));
        }
    }
""",
     """            Assert.Equal(0, hits);
            Assert.Equal(2, await page.EvaluateAsync<int>("() => window.__kronikolRender.errors"));
        }
    }
"""),
    # --- the popup ----------------------------------------------------------------------------------------------------
    ("""        // The internal-flow popup looked for the engine's error text 100 ms after asking for the render, before a worker
        // answers, so it found nothing and left the engine's failure as the diagram. It now reads the answer when the
        // engine writes it, and describes a stack failure as the report does.
        var (source, labelLength) = OverflowingComponentDiagram();
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await LaunchWithTheOptimizingCompilersOff(playwright);
        var page = await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        var note = await OpenPopupDiagram(page, "iflow-stack", source, "PopupStack", "stack");
        Assert.Contains($"Line 6 is a component edge label of {labelLength} characters", await note.InnerTextAsync());
    }
""",
     """        // The internal-flow popup looked for the engine's error text 100 ms after asking for the render, before a worker
        // answers, so it found nothing and left the engine's failure as the diagram. It now reads the answer when the
        // engine writes it, and describes a stack failure as the report does: here the real engine's, on deep nesting.
        var note = await OpenPopupDiagram(Page, "iflow-stack", NestedGroupsDiagram(), "PopupStack", "stack");
        Assert.Contains("ran out of stack", await note.InnerTextAsync());
    }
"""),
    ("""        // report's own diagrams name. Its emitter cuts an action at 600 since 4.14.5, so this one is written by hand, in
        // the emitter's wrapped form, past the 820 characters the worker fails from with the optimizing compilers off.
""",
     """        // report's own diagrams name. Its emitter cuts an action at 600 since 4.14.5, so this one is written by hand, in
        // the emitter's wrapped form, past the 820 characters the 4.x engine's worker failed from with the optimizing
        // compilers off. 5.0.0's engine draws it, so a stub engine draws the 4.x engine's picture.
"""),
    ("""        lines[at] = ":" + action + " (12ms);";
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await LaunchWithTheOptimizingCompilersOff(playwright);
        var page = await OpenPageAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });

        var note = await OpenPopupDiagram(page, "iflow-syntax", string.Join('\\n', lines), "PopupSyntax", "syntax");
""",
     """        lines[at] = ":" + action + " (12ms);";

        var note = await OpenPopupDiagram(Page, "iflow-syntax", string.Join('\\n', lines), "PopupSyntax", "syntax",
            PictureEngine(at + 1, " Syntax Error? (Assumed diagram type: activity)"));
"""),
    ("""    private async Task<ILocator> OpenPopupDiagram(IPage page, string id, string source, string name, string failure)
    {
        var content = $"<div class=\\"plantuml-browser\\" id=\\"{id}\\" data-plantuml=\\"{System.Net.WebUtility.HtmlEncode(source)}\\"></div>";
        await page.GotoAsync(ServePage($$\"\"\"
""",
     """    private async Task<ILocator> OpenPopupDiagram(IPage page, string id, string source, string name, string failure, string? engine = null)
    {
        var content = $"<div class=\\"plantuml-browser\\" id=\\"{id}\\" data-plantuml=\\"{System.Net.WebUtility.HtmlEncode(source)}\\"></div>";
        var url = ServePage($$\"\"\"
"""),
    ("""            {{DiagramContextMenu.GetInternalFlowPopupScript()}}
            </body></html>
            \"\"\", name));
        await page.EvaluateAsync("() => window._iflowShowPopup('iflow-seg-1')");
""",
     """            {{DiagramContextMenu.GetInternalFlowPopupScript()}}
            </body></html>
            \"\"\", name);
        // a stub engine is served beside the page, which it then opens from the same server
        LoopbackFileServer? server = null;
        if (engine is not null) (server, url) = StubEngines.Serve(new Uri(url).LocalPath, engine, name + "-engine.js");
        await using var _ = server;
        await page.GotoAsync(url);
        await page.EvaluateAsync("() => window._iflowShowPopup('iflow-seg-1')");
"""),
    # --- the stack text: the JavaScript engine on the main thread ------------------------------------------------------
    ("""        // A coloured note bar past its edge fails as text, with no picture: the page showed the engine's RangeError line
        // as the diagram. A bar of 5,000 characters, which the emitter's cap no longer writes.
        var bar = "hnote across #black:<color:white>" + string.Concat(Enumerable.Repeat("{\\"sku\\":\\"SKU-0001\\",\\"n\\":1},", 200)) + "</color>";
        var source = "@startuml\\nparticipant \\"Api\\" as api\\nparticipant \\"Orders\\" as orders\\napi -> orders: GET /orders\\n" + bar + "\\n@enduml";

        await Page.GotoAsync(DiagramPage(source, "stack overflow text"));
""",
     """        // A diagram past the engine's stack can fail as text, with no picture: the page showed the engine's RangeError line
        // as the diagram. On the 4.x engine a long coloured note bar did it; 5.0.0's engine draws any length of one, and
        // its JavaScript build, which draws on the main thread, writes deep nesting's overflow as text.
        var source = NestedGroupsDiagram();

        await Page.GotoAsync(DiagramPage(source, "stack overflow text", workers: 0));
"""),
    ("""        Assert.Contains("ran out of stack", text);
        Assert.Contains($"Line 5 is a coloured note bar of {bar.Length} characters (limit {PlantUmlStatementLimits.MaxColouredNoteBarChars})", text);
        Assert.Contains("Maximum call stack size exceeded", text);
""",
     """        Assert.Contains("ran out of stack", text);
        Assert.Contains("Maximum call stack size exceeded", text);
"""),
]
for old, new in pairs:
    assert t.count(old) == 1, (t.count(old), old[:140])
    t = t.replace(old, new)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
