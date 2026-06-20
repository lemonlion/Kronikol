using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// End-to-end tests for the combined report produced by `kronikol merge` (merging several mergeable
/// TestRunReport.json files from parallel runners into one TestRunReport.html).
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class MergedReportTests : PlaywrightTestBase
{
    public MergedReportTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Merged_report_shows_features_from_all_runners()
    {
        await Page.GotoAsync(GenerateMergedReport("MergedFeatures.html"));
        await Page.Locator("details.feature").First.WaitForAsync(new() { Timeout = 10000 });

        var bodyText = await Page.Locator("body").TextContentAsync();
        Assert.Contains("Order Feature", bodyText!);
        Assert.Contains("Payment Feature", bodyText!);
        Assert.Contains("Create order successfully", bodyText!);
        Assert.Contains("Process payment", bodyText!);
    }

    [Fact]
    public async Task Merged_report_counts_scenarios_across_runners()
    {
        await Page.GotoAsync(GenerateMergedReport("MergedCounts.html"));
        await Page.Locator("details.feature").First.WaitForAsync(new() { Timeout = 10000 });

        // Two features rendered (one per runner).
        Assert.Equal(2, await Page.Locator("details.feature").CountAsync());
        // Three scenarios total (2 from runner 1 + 1 from runner 2).
        Assert.Equal(3, await Page.Locator("details.scenario").CountAsync());
    }

    [Fact]
    public async Task Merged_report_renders_combined_component_diagram()
    {
        await Page.GotoAsync(GenerateMergedReport("MergedComponent.html"));

        var button = Page.Locator("button[onclick*='toggle_component_diagram']");
        await button.WaitForAsync(new() { Timeout = 5000 });
        await button.ClickAsync();

        var svg = Page.Locator("#component-diagram [data-diagram-type='plantuml'] svg");
        await svg.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // The merged component diagram contains services contributed by both runners.
        var svgText = await svg.TextContentAsync();
        Assert.Contains("OrderService", svgText!);
        Assert.Contains("PaymentService", svgText!);
    }

    [Fact]
    public async Task Merged_report_preserves_step_parameter_table()
    {
        await Page.GotoAsync(GenerateMergedReport("MergedStepDetail.html"));
        await Page.Locator("details.feature").First.WaitForAsync(new() { Timeout = 10000 });

        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();

        // The inline table-ref toggle and its backing parameter table survived the merge round-trip.
        var toggleBtn = Page.Locator("button.step-table-ref").First;
        await toggleBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.Contains("recipe", await toggleBtn.InnerTextAsync());

        var table = Page.Locator(".step-param-table[data-param='recipe']").First;
        await Expect(table).ToBeVisibleAsync();
        Assert.Contains("Plain Flour", await table.InnerTextAsync());
    }

    [Fact]
    public async Task Merged_report_sequence_diagram_renders()
    {
        await Page.GotoAsync(GenerateMergedReport("MergedSeq.html"));
        await Page.Locator("details.feature").First.WaitForAsync(new() { Timeout = 10000 });
        await ExpandFirstScenarioWithDiagram();

        var svg = (await WaitForDiagramSvg()).First;
        var svgHtml = await svg.EvaluateAsync<string>("el => el.outerHTML");
        Assert.Contains("svg", svgHtml);
        Assert.True(svgHtml.Length > 100, "Merged report should render a real sequence diagram SVG.");
    }
}
