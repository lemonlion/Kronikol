using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A scenario's view tabs (Sequence Diagrams, Activity Diagrams, Flame Chart) sit on the "Diagrams" title line, floated
/// right, when they fit beside the title, and go back under the title, full width, when the section is too narrow
/// (diagram-toggle-layout-script.js). Until 5.0.0 the payload controls rode with them; since then they are the
/// scenario's settings, on a row of their own, and never on the title line (plans/OPTION_C_PLAN.md S8).
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DiagramToggleLayoutTests : DiagramNotePlaywrightBase
{
    public DiagramToggleLayoutTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Toggle = "details.example-diagrams > summary + .diagram-toggle-tabs";

    private async Task OpenReport(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateWholeTestFlowToggleDefaultsReport(TempDir, OutputDir, fileName, _ => { }));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await Page.EvaluateAsync("() => document.querySelectorAll('details.example-diagrams').forEach(d => d.open = true)");
    }

    [Fact]
    public async Task Toolbar_floats_right_on_the_title_line_when_it_fits()
    {
        await Page.SetViewportSizeAsync(1400, 900);
        await OpenReport("ToggleLayoutWide.html");

        await Page.WaitForFunctionAsync($"() => document.querySelector('{Toggle}[data-layout=\"inline\"]') !== null", null, new() { PollingInterval = 200 });
        var geometry = await Page.EvaluateAsync<string>($$"""
            () => {
                var t = document.querySelector('{{Toggle}}[data-layout="inline"]');
                var s = t.previousElementSibling;
                var r = document.createRange(); r.selectNodeContents(s);
                var text = r.getBoundingClientRect(), tr = t.getBoundingClientRect(), sr = s.getBoundingClientRect();
                return JSON.stringify({ float: getComputedStyle(t).float, gap: tr.left - text.right,
                    centreDelta: Math.abs((tr.top + tr.height / 2) - (sr.top + sr.height / 2)), rightOf: sr.right - tr.right });
            }
        """);
        var g = System.Text.Json.JsonDocument.Parse(geometry).RootElement;
        Assert.Equal("right", g.GetProperty("float").GetString());
        Assert.True(g.GetProperty("gap").GetDouble() >= 16, $"toolbar must clear the title text, gap {g.GetProperty("gap")}");
        Assert.True(g.GetProperty("centreDelta").GetDouble() <= 3, $"toolbar must be centred on the title line, delta {g.GetProperty("centreDelta")}");
        Assert.True(g.GetProperty("rightOf").GetDouble() >= 0, "toolbar must stay inside the title's box");
    }

    [Fact]
    public async Task Toolbar_stacks_under_the_title_when_the_page_is_too_narrow()
    {
        await Page.SetViewportSizeAsync(1400, 900);
        await OpenReport("ToggleLayoutNarrow.html");
        await Page.WaitForFunctionAsync($"() => document.querySelector('{Toggle}[data-layout=\"inline\"]') !== null", null, new() { PollingInterval = 200 });

        await Page.SetViewportSizeAsync(520, 900);
        await Page.WaitForFunctionAsync($"() => document.querySelector('{Toggle}[data-layout=\"inline\"]') === null", null, new() { PollingInterval = 200 });
        var geometry = await Page.EvaluateAsync<string>($$"""
            () => {
                var t = document.querySelector('{{Toggle}}');
                var s = t.previousElementSibling;
                var tr = t.getBoundingClientRect(), sr = s.getBoundingClientRect();
                return JSON.stringify({ float: getComputedStyle(t).float, marginTop: t.style.marginTop, below: tr.top >= sr.bottom - 1 });
            }
        """);
        var g = System.Text.Json.JsonDocument.Parse(geometry).RootElement;
        Assert.Equal("none", g.GetProperty("float").GetString());
        Assert.Equal("", g.GetProperty("marginTop").GetString());
        Assert.True(g.GetProperty("below").GetBoolean(), "stacked toolbar must sit under the title");

        // And back again when the page grows.
        await Page.SetViewportSizeAsync(1400, 900);
        await Page.WaitForFunctionAsync($"() => document.querySelector('{Toggle}[data-layout=\"inline\"]') !== null", null, new() { PollingInterval = 200 });
    }

    [Fact]
    public async Task A_scenarios_settings_open_on_a_row_of_their_own_never_on_the_title_line()
    {
        // a scenario with only sequence diagrams: no tabs, and its settings are the only bar
        await Page.SetViewportSizeAsync(1400, 900);
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithAssertionNotes(TempDir, OutputDir, "ToggleLayoutSettings.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await OpenScenarioSettingsAsync();
        await TwoAnimationFramesAsync(Page);

        var geometry = await Page.EvaluateAsync<string>("""
            () => {
                var bar = document.querySelector('details.example-diagrams > .scenario-settings > .diagram-options-bar');
                var s = bar.closest('details').querySelector(':scope > summary');
                return JSON.stringify({ layout: bar.getAttribute('data-layout'), float: getComputedStyle(bar).float,
                    below: bar.getBoundingClientRect().top >= s.getBoundingClientRect().bottom - 1 });
            }
            """);
        var g = System.Text.Json.JsonDocument.Parse(geometry).RootElement;
        Assert.Equal(System.Text.Json.JsonValueKind.Null, g.GetProperty("layout").ValueKind);
        Assert.Equal("none", g.GetProperty("float").GetString());
        Assert.True(g.GetProperty("below").GetBoolean(), "the settings must open under the title");
    }
}
