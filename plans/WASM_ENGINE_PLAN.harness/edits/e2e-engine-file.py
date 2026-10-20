# The E2E facts on the engine's hash and address name the engine the page draws with (5.0.0, plans/WASM_ENGINE_PLAN.md):
# in the suite's Chromium the render workers fetch plantuml-wasm-bundle.js, and the main thread plantuml.js. A mirror's
# base moves all three files, and the on-demand facts count the Wasm engine among the engine's requests.
# usage: python -I e2e-engine-file.py <repo root>
import sys

root = sys.argv[1]
WASM = 'Constants.TrackingDefaults.PlantUmlWasmEngineIntegrity'


def edit(rel, pairs):
    p = root + '/' + rel
    t = open(p, 'rb').read().decode('utf-8')
    assert '\r' not in t, rel
    for old, new in pairs:
        assert t.count(old) == 1, (rel, t.count(old), old[:120])
        t = t.replace(old, new)
    open(p, 'wb').write(t.encode('utf-8'))


edit('tests/Kronikol.Tests.EndToEnd/BrowserRenderWorkerTests.cs', [
    ("""    internal static void AssertTheConsoleNamesTheRefusedEngine(List<string> errors)
    {
        lock (errors)
            Assert.Contains(errors, e => e.StartsWith("Kronikol: PlantUML engine unavailable: engine integrity check failed: plantuml.js from "
                                                      + $"{Constants.TrackingDefaults.PlantUmlJsCdnBase}/plantuml.js does not match {WrongHash}", StringComparison.Ordinal));
    }

    internal static void AssertEveryDiagramNamesTheRefusedEngine(string[] diagrams)
    {
        Assert.NotEmpty(diagrams);
        Assert.All(diagrams, text =>
        {
            Assert.Contains($"engine integrity check failed: plantuml.js from {Constants.TrackingDefaults.PlantUmlJsCdnBase}/plantuml.js", text);
            Assert.Contains($"does not match {WrongHash}", text);
        });
    }
""",
     """    /// <summary>
    /// The engine file the render workers fetch in the suite's Chromium, which runs Wasm GC: from 5.0.0 the Wasm engine
    /// (plans/WASM_ENGINE_PLAN.md). The main thread fetches plantuml.js.
    /// </summary>
    internal const string WorkerEngineFile = "plantuml-wasm-bundle.js";

    internal static void AssertTheConsoleNamesTheRefusedEngine(List<string> errors, string engineFile)
    {
        lock (errors)
            Assert.Contains(errors, e => e.StartsWith($"Kronikol: PlantUML engine unavailable: engine integrity check failed: {engineFile} from "
                                                      + $"{Constants.TrackingDefaults.PlantUmlJsCdnBase}/{engineFile} does not match {WrongHash}", StringComparison.Ordinal));
    }

    internal static void AssertEveryDiagramNamesTheRefusedEngine(string[] diagrams, string engineFile)
    {
        Assert.NotEmpty(diagrams);
        Assert.All(diagrams, text =>
        {
            Assert.Contains($"engine integrity check failed: {engineFile} from {Constants.TrackingDefaults.PlantUmlJsCdnBase}/{engineFile}", text);
            Assert.Contains($"does not match {WrongHash}", text);
        });
    }
"""),
    ("""        // refuses them, the engine is never evaluated, and every diagram says which file failed.
        var errors = ConsoleErrors();
        await Page.GotoAsync(WithWrongHash(GenerateReport("EngineWrongHash.html"), Constants.TrackingDefaults.PlantUmlJsIntegrity));

        var diagrams = await RenderEveryDiagramAndReadIt();

        AssertEveryDiagramNamesTheRefusedEngine(diagrams);
        AssertTheConsoleNamesTheRefusedEngine(errors);
""",
     """        // refuses them, the engine is never evaluated, and every diagram says which file failed: here the Wasm engine the
        // workers fetch, which gives way to no other engine, since the same mirror serves both.
        var errors = ConsoleErrors();
        await Page.GotoAsync(WithWrongHash(GenerateReport("EngineWrongHash.html"), """ + WASM + """));

        var diagrams = await RenderEveryDiagramAndReadIt();

        AssertEveryDiagramNamesTheRefusedEngine(diagrams, WorkerEngineFile);
        AssertTheConsoleNamesTheRefusedEngine(errors, WorkerEngineFile);
"""),
    ("""        Assert.Contains("engine integrity check failed: plantuml.js", r.GetProperty("fallbackReason").GetString());
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => document.querySelectorAll('script[src*=\\"plantuml.js\\"]').length"));
""",
     """        Assert.Contains($"engine integrity check failed: {WorkerEngineFile}", r.GetProperty("fallbackReason").GetString());
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => document.querySelectorAll('script[src*=\\"plantuml\\"]').length"));
"""),
    ("""    /// Points a written report's two engine URLs at <paramref name="engineBase"/>, as a mirror or a proxy in jsDelivr's
    /// place would serve them. The render script sits in the page as plain text, and no request is touched.
    /// </summary>
    internal static string WithEngineBase(string reportUrl, string engineBase)
    {
        var path = new Uri(reportUrl).LocalPath;
        var html = File.ReadAllText(path);
        foreach (var file in new[] { "viz-global.js", "plantuml.js" })
""",
     """    /// Points a written report's engine URLs (Graphviz, and both engines from 5.0.0) at <paramref name="engineBase"/>, as
    /// a mirror or a proxy in jsDelivr's place would serve them. The render script sits in the page as plain text, and no
    /// request is touched.
    /// </summary>
    internal static string WithEngineBase(string reportUrl, string engineBase)
    {
        var path = new Uri(reportUrl).LocalPath;
        var html = File.ReadAllText(path);
        foreach (var file in new[] { "viz-global.js", "plantuml.js", WorkerEngineFile })
"""),
    ("""        await Page.GotoAsync(WithWrongHash(url, Constants.TrackingDefaults.PlantUmlJsIntegrity));

        var diagrams = await RenderEveryDiagramAndReadIt();

        AssertEveryDiagramNamesTheRefusedEngine(diagrams);
        AssertTheConsoleNamesTheRefusedEngine(errors);
""",
     """        await Page.GotoAsync(WithWrongHash(url, Constants.TrackingDefaults.PlantUmlJsIntegrity));

        var diagrams = await RenderEveryDiagramAndReadIt();

        // the main thread draws with plantuml.js
        AssertEveryDiagramNamesTheRefusedEngine(diagrams, "plantuml.js");
        AssertTheConsoleNamesTheRefusedEngine(errors, "plantuml.js");
"""),
    ("""        var file = new Uri(WithWrongHash(GenerateReport("ServedOverHttpWrongHash.html"), Constants.TrackingDefaults.PlantUmlJsIntegrity)).LocalPath;
""",
     """        var file = new Uri(WithWrongHash(GenerateReport("ServedOverHttpWrongHash.html"), """ + WASM + """)).LocalPath;
"""),
    ("""        Assert.Equal("http:", await Page.EvaluateAsync<string>("() => location.protocol"));
        AssertEveryDiagramNamesTheRefusedEngine(diagrams);
        AssertTheConsoleNamesTheRefusedEngine(errors);
""",
     """        Assert.Equal("http:", await Page.EvaluateAsync<string>("() => location.protocol"));
        AssertEveryDiagramNamesTheRefusedEngine(diagrams, WorkerEngineFile);
        AssertTheConsoleNamesTheRefusedEngine(errors, WorkerEngineFile);
"""),
])

edit('tests/Kronikol.Tests.EndToEnd/NonSecureOriginTests.cs', [
    ("""            BrowserRenderWorkerTests.WithWrongHash(GenerateReport("LanHostWrongHash.html"), TrackingDefaults.PlantUmlJsIntegrity));
""",
     """            BrowserRenderWorkerTests.WithWrongHash(GenerateReport("LanHostWrongHash.html"), TrackingDefaults.PlantUmlWasmEngineIntegrity));
"""),
    ("""        BrowserRenderWorkerTests.AssertEveryDiagramNamesTheRefusedEngine(diagrams.Select(d => d.Text).ToArray());
        BrowserRenderWorkerTests.AssertTheConsoleNamesTheRefusedEngine(lan.ConsoleErrors);
""",
     """        BrowserRenderWorkerTests.AssertEveryDiagramNamesTheRefusedEngine(diagrams.Select(d => d.Text).ToArray(), BrowserRenderWorkerTests.WorkerEngineFile);
        BrowserRenderWorkerTests.AssertTheConsoleNamesTheRefusedEngine(lan.ConsoleErrors, BrowserRenderWorkerTests.WorkerEngineFile);
"""),
])

edit('tests/Kronikol.Tests.EndToEnd/OnDemandRenderingTests.cs', [
    ("""            if (request.Url.EndsWith("/plantuml.js", StringComparison.Ordinal) || request.Url.EndsWith("/viz-global.js", StringComparison.Ordinal))
""",
     """            if (request.Url.EndsWith("/plantuml.js", StringComparison.Ordinal) || request.Url.EndsWith("/plantuml-wasm-bundle.js", StringComparison.Ordinal)
                || request.Url.EndsWith("/viz-global.js", StringComparison.Ordinal))
"""),
])
print('ok')
