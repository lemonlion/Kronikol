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

    /// <summary>The SVG the engine drew, without the processing instruction that encodes its source.</summary>
    private Task<string> EngineSvg(string id) => Page.EvaluateAsync<string>(
        "id => document.getElementById(id).querySelector('svg').outerHTML.replace(/<\\?[\\s\\S]*?\\?>/g, '').replace(/<!--[\\s\\S]*?-->/g, '')", id);

    [Fact]
    public async Task A_themed_source_renders_unthemed_in_the_worker_and_the_engine_says_why()
    {
        var console = new List<string>();
        Page.Console += (_, message) => { lock (console) console.Add(message.Text); };

        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateBrowserJsPage(("d-themed", Themed), ("d-plain", Plain))));
        await RenderAll("d-themed", "d-plain");

        var themed = await EngineSvg("d-themed");
        var plain = await EngineSvg("d-plain");
        // When the worker registers PLANTUML_THEMES (THEME_PLAN 6.1) the two differ and the warning is
        // gone: this fact then fails on purpose. At that point remove the OptionNotApplied emitter for
        // BrowserJs, and update the PlantUmlTheme doc comments and the wiki's theme pages.
        Assert.Equal(plain, themed);
        lock (console)
            Assert.Contains(console, m => m.Contains("themes.js could not be loaded", StringComparison.Ordinal));
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
}
