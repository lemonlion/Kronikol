namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A stand-in for the Wasm engine's bundle, for an answer 5.0.0's engine gives for no source a test can write: a crash,
/// a start that fails, or the 4.x engine's error pictures (plans/WASM_ENGINE_PLAN.md). The test serves the bundle from a
/// loopback server beside its page and tells the page its address and hash, as a mirror would; no request is mocked.
/// </summary>
internal static class StubEngines
{
    internal const string WasmBundle = "plantuml-wasm-bundle.js";

    /// <summary>
    /// A bundle whose <c>__plantumlEngineReady()</c> answers <paramref name="ready"/> and whose render runs
    /// <paramref name="renderBody"/> with <c>lines</c> and <c>id</c> in scope and <c>draw(texts)</c> to draw an SVG of one
    /// <c>&lt;text&gt;</c> per string into the render's target. It ends in the export statement the page rewrites.
    /// </summary>
    internal static string Bundle(string renderBody, string ready = "Promise.resolve()") => $$"""
        globalThis.__plantumlEngineReady = function () { return {{ready}}; };
        function render(lines, id) {
            var NS = 'http://www.w3.org/2000/svg', target = document.getElementById(id);
            function draw(texts) {
                var svg = document.createElementNS(NS, 'svg'); svg.setAttribute('width', '10'); svg.setAttribute('height', '10');
                var g = document.createElementNS(NS, 'g'); svg.appendChild(g);
                texts.forEach(function (t, i) {
                    var text = document.createElementNS(NS, 'text'); text.setAttribute('x', '5'); text.setAttribute('y', String(14 * (i + 1)));
                    text.appendChild(document.createTextNode(t)); g.appendChild(text);
                });
                target.appendChild(svg);
            }
            {{renderBody}}
            return Promise.resolve();
        }
        function renderToString() { return ''; }
        export{render as render,renderToString as renderToString};
        """;

    /// <summary>
    /// Points the written page at <paramref name="url"/> for its Wasm engine and tells it the hash of
    /// <paramref name="bundle"/>, which the test serves there. The render script sits in the page as plain text.
    /// </summary>
    internal static void WithWasmEngine(string pagePath, string url, string bundle)
    {
        var html = File.ReadAllText(pagePath);
        var hash = global::Kronikol.PlantUml.EngineCache.Integrity(System.Text.Encoding.UTF8.GetBytes(bundle));
        foreach (var (from, to) in new[]
                 {
                     ($"'{Constants.TrackingDefaults.PlantUmlJsCdnBase}/{WasmBundle}'", $"'{url}'"),
                     ($"'{Constants.TrackingDefaults.PlantUmlWasmEngineIntegrity}'", $"'{hash}'"),
                 })
        {
            var at = html.IndexOf(from, StringComparison.Ordinal);
            Assert.True(at >= 0 && html.IndexOf(from, at + 1, StringComparison.Ordinal) < 0, $"the render script should carry {from} exactly once");
            html = html.Replace(from, to);
        }
        File.WriteAllText(pagePath, html);
    }

    /// <summary>
    /// Writes <paramref name="bundle"/> beside the page at <paramref name="pagePath"/>, starts a loopback server over that
    /// folder, points the page at the bundle, and returns the server and the page's address on it.
    /// </summary>
    internal static (LoopbackFileServer Server, string PageUrl) Serve(string pagePath, string bundle, string bundleName)
    {
        var dir = Path.GetDirectoryName(pagePath)!;
        File.WriteAllText(Path.Combine(dir, bundleName), bundle);
        var server = LoopbackFileServer.Start(dir);
        WithWasmEngine(pagePath, server.UrlOf(bundleName), bundle);
        return (server, server.UrlOf(Path.GetFileName(pagePath)));
    }
}
