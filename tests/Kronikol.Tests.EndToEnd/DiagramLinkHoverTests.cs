namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A request's link in a sequence diagram, drawn on hover (or always, where links are always shown), takes the report's
/// accent and not the engine's #0000FF, which read as purple beside the rest of the palette (5.0.0,
/// plans/OPTION_C_PLAN.md S10, the owner's round 17 item 5). At rest it reads in the ink of the text beside it.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramLinkHoverTests : PlaywrightTestBase
{
    public DiagramLinkHoverTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_link_takes_the_themes_accent(string theme)
    {
        var (uri, _) = ReportTestHelper.GenerateRunReportWithFlowArrows(TempDir, OutputDir, $"LinkAccent-{theme}.html", withFlow: 1, withoutFlow: 0);
        await Page.GotoAsync(uri);
        await Page.EvaluateAsync("t => document.documentElement.setAttribute('data-theme', t)", theme);
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('.plantuml-browser:not(.iflow-diagram) svg text[data-iflow-fill]')", null,
            new() { Timeout = 300000, PollingInterval = 200 });

        // SVG rule: the hover is dispatched on the link's own text
        var r = await Page.EvaluateAsync<string[]>("""
            () => {
                const t = document.querySelector('.plantuml-browser:not(.iflow-diagram) svg text[data-iflow-fill]');
                t.dispatchEvent(new MouseEvent('mouseenter'));
                const p = document.createElement('div'); p.style.color = 'var(--kron-accent)'; document.body.appendChild(p);
                const accent = getComputedStyle(p).color; p.remove();
                return [t.getAttribute('text-decoration') || '', getComputedStyle(t).fill, accent];
            }
            """);

        Assert.Equal("underline", r[0]);
        Assert.Equal(r[2], r[1]);
    }
}
