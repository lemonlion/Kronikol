using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Text Kronikol copies in, drawn as captured (DIAGRAM_COLOURS_PLAN S4). A captured body quoting Rust's
/// <c>Vec&lt;&amp;str&gt;</c> is PlantUML's OpenIconic syntax, <c>&lt;:name:&gt;</c> its emoji syntax: both
/// made the engine load a bundle the render worker cannot, and before 3.29.6 such a diagram was never
/// drawn, nor was anything the same worker rendered after it. When it was the first diagram on the page,
/// the report drew nothing at all. LightBDD writes a table parameter as <c>&lt;$name&gt;</c>, the sprite
/// syntax, which the engine dropped from the step bar.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class LoaderMarkupInCapturedTextTests : PlaywrightTestBase
{
    public LoaderMarkupInCapturedTextTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenReport(NotePayloadFormat format, [System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithLoaderMarkupPayloads(TempDir, OutputDir, $"LoaderMarkup_{testName}.html", format));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null,
            new() { Timeout = 60_000, PollingInterval = 200 });
    }

    /// <summary>Every diagram's painted text, one string per diagram, in page order.</summary>
    private Task<string[]> PaintedTexts() => Page.EvaluateAsync<string[]>("""
        () => Array.from(document.querySelectorAll('.plantuml-browser')).map(el =>
            Array.from(el.querySelectorAll('svg text')).map(t => t.textContent).join(' ').replace(/\s+/g, ' '))
        """);

    private async Task AssertEveryDiagramDrawn()
    {
        var states = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.plantuml-browser')).map(el =>
                el.id + ':' + (el.querySelector('svg') ? 'svg' : 'none') + (el.querySelector('.engine-failure') ? ':failure' : ''))
            """);
        Assert.Equal(3, states.Length);
        Assert.All(states, s => Assert.EndsWith(":svg", s));
    }

    [Fact]
    public async Task Captured_loader_markup_in_the_first_diagram_leaves_every_diagram_drawn()
    {
        await OpenReport(NotePayloadFormat.Json);

        await AssertEveryDiagramDrawn();
        var texts = string.Join(" | ", await PaintedTexts());
        Assert.Contains("expected Vec<&str>, found String", texts);
        Assert.Contains("<:rocket:>", texts);
        Assert.DoesNotContain("~<", texts);
    }

    [Fact]
    public async Task A_lightbdd_table_parameter_keeps_its_name_in_the_step_bar()
    {
        await OpenReport(NotePayloadFormat.Json);

        // Before 3.29.6 the bar painted `[inputs: ""]`: the engine read <$inputs> as a sprite and dropped it.
        var texts = string.Join(" | ", await PaintedTexts());
        Assert.Contains("Given I have data [inputs: \"<$inputs>\"]", texts);
    }

    [Fact]
    public async Task The_yaml_view_of_a_note_quoting_loader_markup_is_drawn()
    {
        // The YAML view rebuilds the note's lines in the browser (escapeNoteLine): it needs the same rule,
        // or switching a note to YAML sends the markup to the engine again.
        await OpenReport(NotePayloadFormat.Yaml);

        await AssertEveryDiagramDrawn();
        var texts = string.Join(" | ", await PaintedTexts());
        Assert.Contains("error: expected Vec<&str>, found String", texts);
        Assert.Contains("launch: \"<:rocket:>\"", texts);
    }

    [Fact]
    public async Task Copy_all_caller_request_payloads_is_offered_with_the_default_coloured_arrows()
    {
        // DIAGRAM_COLOURS_PLAN F26. The emitter colours arrows by default (`caller -[#…]> svc`), and the menu
        // looked for a plain `caller -> ` arrow, so it never offered the item on a default report. Its one
        // older test passed on a hand-typed source with a plain arrow; this diagram is the emitter's own.
        await OpenReport(NotePayloadFormat.Json);
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        await DispatchContextMenu(Page.Locator(".plantuml-browser svg").First);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Copy all caller request payloads");
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();

        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.Contains("\"input\": \"Vec\"", clipboard);
        Assert.DoesNotContain("color:", clipboard);
        Assert.DoesNotContain("expected Vec", clipboard); // the response body is not a request payload
    }

    [Fact]
    public async Task Copy_box_text_gives_back_the_captured_body_without_the_escapes()
    {
        await OpenReport(NotePayloadFormat.Json);
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);

        // The response note of the first diagram: the one carrying the captured error body.
        var note = Page.Locator("#" + await Page.EvaluateAsync<string>("() => document.querySelector('.plantuml-browser').id") + " .note-hover-rect");
        await note.Last.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        await DispatchContextMenu(note.Last);
        var item = Page.Locator(".diagram-ctx-menu").GetByText("Copy box text");
        await item.WaitForAsync(new() { Timeout = 5000 });
        await item.ClickAsync();

        var clipboard = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.Contains("\"error\": \"expected Vec<&str>, found String\"", clipboard);
        Assert.DoesNotContain("~<", clipboard);
    }
}
