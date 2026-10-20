using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The engine's script loader through the shipped render path (DIAGRAM_COLOURS_PLAN S3a): the render
/// script, the worker host and the real engine in a Blob worker. The engine loads four bundles by
/// appending a <c>&lt;script&gt;</c> to <c>document.head</c> (themes.js, a stdlib module, openiconic.js,
/// emoji.js). The worker cannot load any of them, and until 3.29.6 its mock head never answered: a
/// diagram that asked for one was never drawn, nor was anything the same worker rendered after it.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ThemedSourceRendersTests : PlaywrightTestBase
{
    private const int RenderWaitMs = 60_000;

    public ThemedSourceRendersTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Plain = "@startuml\nparticipant Alice\nparticipant Bob\nAlice -> Bob: Hello\nBob --> Alice: Hi\n@enduml";
    private const string Themed = "@startuml\n!theme cerulean\nparticipant Alice\nparticipant Bob\nAlice -> Bob: Hello\nBob --> Alice: Hi\n@enduml";

    private async Task RenderAll(params string[] ids)
    {
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(
            "ids => ids.every(id => { var el = document.getElementById(id); return el && el.dataset.rendered === '1' && (el.querySelector('svg') || (el.textContent || '').trim()); })",
            ids, new() { Timeout = RenderWaitMs, PollingInterval = 200 });
    }

    /// <summary>
    /// The SVG the engine drew, without the processing instruction that encodes its source, and without the empty style
    /// the page's zoom check leaves on it, which is there or not by when the check's frame came.
    /// </summary>
    private Task<string> EngineSvg(string id) => Page.EvaluateAsync<string>("""
        id => {
            var svg = document.getElementById(id).querySelector('svg').cloneNode(true);
            if (svg.getAttribute('style') === '') svg.removeAttribute('style');
            return svg.outerHTML.replace(/<\?[\s\S]*?\?>/g, '').replace(/<!--[\s\S]*?-->/g, '');
        }
        """, id);

    [Fact]
    public async Task A_themed_source_renders_unthemed_in_the_worker_and_the_engine_says_why()
    {
        var console = new List<string>();
        Page.Console += (_, message) => { lock (console) console.Add(message.Text); };

        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage(("d-themed", Themed), ("d-plain", Plain))));
        await RenderAll("d-themed", "d-plain");
        // The page measures each diagram a frame after it is drawn, to decide whether it needs a zoom button, and the
        // measuring leaves an empty style on the svg. A full run under load read the themed diagram after its frame and
        // the plain one before its own; the plain one is put back as it is before its frame, so that is the case every time.
        await Page.EvaluateAsync("""
            () => {
                window._getDiagramNaturalWidth(document.getElementById('d-themed'));
                var plain = document.querySelector('#d-plain svg');
                if (plain.getAttribute('style') === '') plain.removeAttribute('style');
            }
            """);

        var themed = await EngineSvg("d-themed");
        var plain = await EngineSvg("d-plain");
        bool warned;
        lock (console)
            warned = console.Any(m => m.Contains("themes.js could not be loaded", StringComparison.Ordinal));
        // The 6.1 guard: when the worker registers PLANTUML_THEMES the two differ and the warning is gone,
        // and this fact fails on purpose. Two renders that differ while the worker still says it could not load
        // the themes are not a theme, and the message says so (one failed so in a full run under load, 4.30.0).
        Assert.True(plain == themed, warned
            ? "The themed and plain renders differ, but the worker said themes.js could not be loaded, so this is not "
              + "a theme: the same source drew two ways. " + FirstDifference(plain, themed)
            : "The worker now applies a theme (THEME_PLAN 6.1). Remove the OptionNotApplied emitter for BrowserJs " +
              "(ReportGenerator.RecordOptionDiagnostics, pinned by OptionDiagnosticsTests), and update the PlantUmlTheme " +
              "doc comments and the wiki's theme pages to say BrowserJs applies a theme.");
        Assert.True(warned, "the worker did not say themes.js could not be loaded");
    }

    /// <summary>Where two SVGs first part, with a little of each from there.</summary>
    private static string FirstDifference(string plain, string themed)
    {
        var at = 0;
        while (at < plain.Length && at < themed.Length && plain[at] == themed[at])
            at++;
        static string From(string text, int at) => text.Length <= at ? "(end)" : text.Substring(at, Math.Min(120, text.Length - at));
        return $"They part at character {at} of {plain.Length} (plain) and {themed.Length} (themed): plain has "
               + $"\"{From(plain, at)}\", themed has \"{From(themed, at)}\".";
    }

    [Fact]
    public async Task A_stdlib_include_renders_instead_of_hanging()
    {
        const string include = "@startuml\n!include <C4/C4_Context>\nPerson(user, \"User\")\n@enduml";

        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage(("d-include", include), ("d-after", Plain))));
        await RenderAll("d-include", "d-after");

        // The stdlib is not in the engine build, so what it draws is its own error picture. The point is
        // that it answers, and that the diagram after it on the same worker is drawn.
        Assert.True(await Page.Locator("#d-include svg").CountAsync() > 0, "the include should end in an SVG");
        Assert.True(await Page.Locator("#d-after svg").CountAsync() > 0, "the diagram after it should be drawn");
    }

    [Fact]
    public async Task A_page_whose_first_diagram_needs_a_bundle_still_draws_the_rest()
    {
        // A user's own markup (InsertPlantUml) asking for an OpenIconic icon. `copy` is also an HTML entity
        // name, so the raw PlantUML shown under the failure proves the source is escaped as text.
        const string icon = "@startuml\nAlice -> Bob : <&copy> done\n@enduml";
        var page = TestPageGenerator.GenerateBrowserJsPage(
            ("d-icon", icon),
            ("d-one", Plain.Replace("Hello", "One")),
            ("d-two", Plain.Replace("Hello", "Two")),
            ("d-three", Plain.Replace("Hello", "Three")));

        await Page.GotoAsync(ServePage(page));
        await RenderAll("d-icon", "d-one", "d-two", "d-three");

        foreach (var id in new[] { "d-one", "d-two", "d-three" })
            Assert.True(await Page.Locator($"#{id} svg").CountAsync() > 0, $"{id} should be drawn");

        var failure = Page.Locator("#d-icon [data-engine-failure='loader']");
        await Expect(failure).ToBeVisibleAsync();
        await Expect(failure).ToContainTextAsync("icons");
        Assert.DoesNotContain("java.lang.RuntimeException", await Page.Locator("#d-icon").TextContentAsync() ?? "");
        Assert.Equal(icon, await failure.Locator("details pre").TextContentAsync());
    }

    [Fact]
    public async Task A_themed_report_draws_every_diagram_and_records_that_the_theme_was_not_applied()
    {
        var (uri, reportsDir) = ReportTestHelper.GenerateThemedRunReport(TempDir, OutputDir, "ThemedRunReport.html");

        await Page.GotoAsync(uri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null,
            new() { Timeout = RenderWaitMs, PollingInterval = 200 });

        var states = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.plantuml-browser')).map(el =>
                (el.querySelector('svg') ? 'svg' : 'none') + (el.querySelector('.engine-failure') ? ':failure' : ''))
            """);
        Assert.Equal(["svg", "svg"], states);

        using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(reportsDir, "TestRunReport.json")));
        var kinds = report.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("kind").GetString()).ToList();
        Assert.Single(kinds, k => k == "OptionNotApplied");
        Assert.DoesNotContain("RenderFailure", kinds);
    }
}
