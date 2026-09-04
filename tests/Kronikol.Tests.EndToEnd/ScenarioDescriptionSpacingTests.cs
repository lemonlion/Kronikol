namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Pins the vertical breathing room around a scenario description. The div shipped unstyled,
/// so the description sat flush against the scenario summary above it and the Steps section
/// below it (user-requested, 3.0.82). Asserted off the PAINTED layout: the computed margins
/// must equal one em of the element's own font size, and the gaps to the neighbouring boxes
/// must actually open up.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ScenarioDescriptionSpacingTests : PlaywrightTestBase
{
    public ScenarioDescriptionSpacingTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateDescriptionReport(string fileName) =>
        ReportTestHelper.GenerateReportWithScenarioDescriptions(TempDir, OutputDir, fileName);

    private async Task ExpandAll()
    {
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }

    /// <summary>Computed margin-top / margin-bottom / font-size of the nth description, in px.</summary>
    private async Task<(double Top, double Bottom, double FontSize)> GetDescriptionMargins(int index)
    {
        var json = await Page.EvaluateAsync<string>($$"""
            () => {
                var el = document.querySelectorAll('.scenario-description')[{{index}}];
                if (!el) return '';
                var cs = getComputedStyle(el);
                return JSON.stringify({
                    top: parseFloat(cs.marginTop),
                    bottom: parseFloat(cs.marginBottom),
                    fontSize: parseFloat(cs.fontSize)
                });
            }
        """);
        Assert.False(string.IsNullOrEmpty(json), $"no .scenario-description at index {index}");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("top").GetDouble(),
                doc.RootElement.GetProperty("bottom").GetDouble(),
                doc.RootElement.GetProperty("fontSize").GetDouble());
    }

    [Fact]
    public async Task Scenario_description_has_one_em_of_margin_above_and_below()
    {
        await Page.GotoAsync(GenerateDescriptionReport("ScenarioDescMargins.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        await Page.Locator(".scenario-description").First.WaitForAsync(new() { Timeout = 5000 });
        var (top, bottom, fontSize) = await GetDescriptionMargins(0);

        // 1em resolves against the element's OWN font size
        Assert.Equal(fontSize, top, 1);
        Assert.Equal(fontSize, bottom, 1);
        Assert.True(top > 0, $"expected a real top margin, got {top}px");
    }

    [Fact]
    public async Task Parameterized_detail_panel_description_has_the_same_margins()
    {
        await Page.GotoAsync(GenerateDescriptionReport("ScenarioDescParamMargins.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();

        // The grouped parameterized scenario emits its description inside each detail panel —
        // a second, independent emission site that must not be left unstyled.
        var count = await Page.Locator(".param-detail-panel .scenario-description").CountAsync();
        Assert.True(count > 0, "expected a description inside a parameterized detail panel");

        var json = await Page.EvaluateAsync<string>("""
            () => {
                var el = document.querySelector('.param-detail-panel .scenario-description');
                var cs = getComputedStyle(el);
                return JSON.stringify({
                    top: parseFloat(cs.marginTop),
                    bottom: parseFloat(cs.marginBottom),
                    fontSize: parseFloat(cs.fontSize)
                });
            }
        """);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var top = doc.RootElement.GetProperty("top").GetDouble();
        var bottom = doc.RootElement.GetProperty("bottom").GetDouble();
        var fontSize = doc.RootElement.GetProperty("fontSize").GetDouble();

        Assert.Equal(fontSize, top, 1);
        Assert.Equal(fontSize, bottom, 1);
    }

    [Fact]
    public async Task Description_actually_paints_a_gap_to_the_elements_around_it()
    {
        await Page.GotoAsync(GenerateDescriptionReport("ScenarioDescPaintedGap.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandAll();
        await Page.Locator(".scenario-description").First.WaitForAsync(new() { Timeout = 5000 });

        // Measure what the browser PAINTS: the description box must clear the summary above it
        // and the first section below it. A collapsed/zero margin puts these gaps at ~0.
        var json = await Page.EvaluateAsync<string>("""
            () => {
                var desc = document.querySelector('.scenario-description');
                var scenario = desc.closest('details.scenario');
                var summary = scenario.querySelector('summary');
                var next = desc.nextElementSibling;
                var d = desc.getBoundingClientRect();
                return JSON.stringify({
                    gapAbove: d.top - summary.getBoundingClientRect().bottom,
                    gapBelow: next ? next.getBoundingClientRect().top - d.bottom : -1,
                    hasNext: !!next
                });
            }
        """);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var gapAbove = doc.RootElement.GetProperty("gapAbove").GetDouble();
        var gapBelow = doc.RootElement.GetProperty("gapBelow").GetDouble();

        Assert.True(gapAbove >= 12, $"expected a painted gap above the description, got {gapAbove}px");
        Assert.True(doc.RootElement.GetProperty("hasNext").GetBoolean(), "expected an element after the description");
        Assert.True(gapBelow >= 12, $"expected a painted gap below the description, got {gapBelow}px");
    }
}
