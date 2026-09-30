namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// #87 on the page (<c>plans/SPAN_ATTRIBUTION_PLAN.md</c> §4.6), on a report the whole pipeline wrote with its
/// defaults: two scenarios' calls ran at the same time and recorded no trace, so the spans both could own went to
/// neither. The popup that kept some says how many it left out, above its diagram, and counts them in its title; the
/// call that kept none stays a link under <c>HideLink</c>, and its popup says why.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class SpansLeftOutPopupTests : PlaywrightTestBase
{
    public SpansLeftOutPopupTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    private async Task OpenAndDrawBothScenarios(string uri)
    {
        await Page.GotoAsync(uri);
        await Page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    /// <summary>The drawn texts of the call to <paramref name="path"/>, the whole-test flow's diagrams left out.</summary>
    private ILocator Arrow(string path) =>
        Page.Locator(".plantuml-browser:not(.iflow-diagram) svg text", new() { HasTextString = path }).First;

    [Fact]
    public async Task A_popup_that_left_spans_out_says_how_many_and_why_above_its_diagram()
    {
        var (uri, _) = ReportTestHelper.GenerateRunReportWithContestedSpans(TempDir, OutputDir, "SpansLeftOut_Note.html");
        await OpenAndDrawBothScenarios(uri);

        // SVG rule: a dispatched click, on the arrow's own text.
        await Arrow("/contested-a").EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true }))");

        var popup = Page.Locator(".iflow-popup");
        await Expect(popup.Locator("h3")).ToHaveTextAsync("Internal Flow (1 span, 2 left out)");
        var note = popup.Locator(".iflow-left-out");
        await Expect(note).ToBeVisibleAsync();
        await Expect(note).ToHaveTextAsync("2 spans that started during this call are left out: a request of another test ran at the same time, and neither the trace nor the span tree says which of the two they belong to.");
        await popup.Locator(".iflow-diagram svg").First.WaitForAsync(new() { Timeout = PopupFirstDrawTimeout });
        var diagram = await popup.Locator(".iflow-diagram svg").First.TextContentAsync();
        Assert.Contains("SELECT a-only", diagram);
        Assert.DoesNotContain("SELECT either", diagram);
    }

    [Fact]
    public async Task An_arrow_whose_spans_all_went_to_neither_call_stays_a_link_and_its_popup_says_why()
    {
        var (uri, _) = ReportTestHelper.GenerateRunReportWithContestedSpans(TempDir, OutputDir, "SpansLeftOut_Message.html");
        await OpenAndDrawBothScenarios(uri);

        // HideLink leaves an arrow with nothing to open at rest; this one has something to say.
        var arrow = Arrow("/contested-b");
        await Expect(arrow).ToBeVisibleAsync();
        Assert.Equal("all", await arrow.EvaluateAsync<string>("t => t.style.pointerEvents"));
        await arrow.EvaluateAsync("t => t.dispatchEvent(new MouseEvent('click', { bubbles: true }))");

        // The message is read as markup: its paragraph, not its tags as text.
        var message = Page.Locator(".iflow-popup .iflow-no-data");
        await Expect(message.Locator(".iflow-left-out")).ToHaveTextAsync("2 spans that started during this call are left out: a request of another test ran at the same time, and neither the trace nor the span tree says which of the two they belong to.");
        Assert.DoesNotContain("<p", await message.TextContentAsync());
    }
}
