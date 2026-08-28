namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Reports)]
public class WholeTestFlowTests : PlaywrightTestBase
{
    public WholeTestFlowTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1280;
    protected override int ViewportHeight => 900;

    [Fact]
    public async Task Whole_test_flow_renders_collapsed_details_block()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateWholeTestFlowPage()));

        var details = Page.Locator("details.whole-test-flow");
        await Expect(details).ToBeVisibleAsync();
        Assert.Null(await details.GetAttributeAsync("open"));

        await Expect(details.Locator("summary")).ToContainTextAsync("Whole Test Flow");
    }

    [Fact]
    public async Task Whole_test_flow_expands_on_click()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateWholeTestFlowPage()));

        await Page.Locator("details.whole-test-flow > summary").ClickAsync();
        Assert.NotNull(await Page.Locator("details.whole-test-flow").GetAttributeAsync("open"));
    }

    [Fact]
    public async Task Whole_test_flow_Both_shows_toggle_buttons()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateWholeTestFlowPage(WholeTestFlowVisualization.Both)));
        await Page.Locator("details.whole-test-flow > summary").ClickAsync();

        var toggleBtns = Page.Locator(".whole-test-flow .iflow-toggle-btn");
        Assert.Equal(2, await toggleBtns.CountAsync());
        await Expect(toggleBtns.First).ToHaveTextAsync("Activity");
        await Expect(toggleBtns.Nth(1)).ToHaveTextAsync("Flame Chart");
    }

    [Fact]
    public async Task Whole_test_flow_toggle_switches_views()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateWholeTestFlowPage(WholeTestFlowVisualization.Both)));
        await Page.Locator("details.whole-test-flow > summary").ClickAsync();

        var toggleBtns = Page.Locator(".whole-test-flow .iflow-toggle-btn");
        var mainView = Page.Locator(".whole-test-flow .iflow-view-main");
        var flameView = Page.Locator(".whole-test-flow .iflow-view-flame");

        // The main view holds only an empty plantuml-browser div until the
        // engine renders the SVG into it — zero-size counts as "hidden", so
        // visibility must wait for the render (which can exceed the 5s
        // Expect default under parallel-suite CPU load).
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('.whole-test-flow .iflow-view-main svg')",
            null, new() { Timeout = 60000, PollingInterval = 200 });
        await Expect(mainView).ToBeVisibleAsync();
        await Expect(flameView).Not.ToBeVisibleAsync();

        await toggleBtns.Nth(1).ClickAsync();
        await Expect(mainView).Not.ToBeVisibleAsync();
        await Expect(flameView).ToBeVisibleAsync();

        await toggleBtns.First.ClickAsync();
        await Expect(mainView).ToBeVisibleAsync();
        await Expect(flameView).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Whole_test_flow_FlameChart_only_shows_flame_bars()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateWholeTestFlowPage(WholeTestFlowVisualization.FlameChart)));
        await Page.Locator("details.whole-test-flow > summary").ClickAsync();

        var flameBars = Page.Locator(".whole-test-flow .iflow-flame-bar");
        Assert.True(await flameBars.CountAsync() >= 2);

        Assert.Equal(0, await Page.Locator(".whole-test-flow .iflow-toggle-btn").CountAsync());
    }

    [Fact]
    public async Task Whole_test_flow_flame_has_boundary_markers()
    {
        await Page.GotoAsync(ServePage(TestPageGenerator.GenerateWholeTestFlowPage(WholeTestFlowVisualization.FlameChart)));
        await Page.Locator("details.whole-test-flow > summary").ClickAsync();

        var markers = Page.Locator(".whole-test-flow .iflow-boundary-marker");
        Assert.True(await markers.CountAsync() >= 1);
    }
}
