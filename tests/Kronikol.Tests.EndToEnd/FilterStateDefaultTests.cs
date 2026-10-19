namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A diagram drawn without filter state of its own (one the page's pre-processor never saw) takes the report's: the
/// note script read a missing Databases flag as shown in the Databases toggle and as hidden everywhere else, so
/// collapsing its notes hid such a diagram's databases while the report showed them (4.26.8).
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class FilterStateDefaultTests : DiagramNotePlaywrightBase
{
    public FilterStateDefaultTests(PlaywrightFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("_databasesVisible")]
    [InlineData("_stepsVisible")]
    [InlineData("_assertionsVisible")]
    public async Task A_diagram_without_its_own_filter_state_keeps_the_reports_when_its_notes_change(string flag)
    {
        await Page.GotoAsync(GenerateDatabaseStepNoteCollapseReport($"FilterStateDefault{flag}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        var before = await Page.EvaluateAsync<string>($$"""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                var shown = c.{{flag}};
                delete c.{{flag}};
                window.{{flag}} = shown;
                return c.getAttribute('data-plantuml');
            }
        """);
        // Collapsing every note re-composes the diagram: the forgotten flag must read as the report's.
        await Page.Locator(".toolbar-right .details-radio-btn[data-state='collapsed']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            (before) => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.getAttribute('data-plantuml') !== before;
            }
        """, before, new() { Timeout = 15000, PollingInterval = 200 });

        var after = await Page.EvaluateAsync<string>("() => document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml')");
        Assert.Equal(before.Contains("mongoDB"), after.Contains("mongoDB"));
        Assert.Equal(before.Contains("stepDelimiter"), after.Contains("stepDelimiter"));
        Assert.Equal(before.Contains("assertionNote"), after.Contains("assertionNote"));
    }

    /// <summary>The report's toggle draws such a diagram again with the new state: the Steps and Assertions toggles
    /// took a missing flag for hidden and skipped the diagram when hiding them.</summary>
    [Theory]
    [InlineData("_databasesVisible", "databases", "mongoDB")]
    [InlineData("_stepsVisible", "steps", "stepDelimiter")]
    // the notes' stereotype: the fixture's style block names .assertionNote too, so the bare word is always in the source
    [InlineData("_assertionsVisible", "assertions", "<<assertionNote>>")]
    public async Task A_diagram_without_its_own_filter_state_follows_the_reports_toggle(string flag, string toggle, string token)
    {
        await Page.GotoAsync(GenerateDatabaseStepNoteCollapseReport($"FilterStateToggle{flag}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await Page.EvaluateAsync($$"""() => { delete document.querySelector('[data-diagram-type="plantuml"]').{{flag}}; }""");
        await Page.Locator($".toolbar-right .toggle-btn[data-toggle='{toggle}']").First.ClickAsync();
        await Page.WaitForFunctionAsync($$"""
            () => {
                var c = document.querySelector('[data-diagram-type="plantuml"]');
                return c && !c._noteRendering && !window._plantumlRendering && c.{{flag}} !== undefined;
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        var shown = await Page.EvaluateAsync<bool>($"() => window.{flag}");
        var source = await Page.EvaluateAsync<string>("() => document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml')");
        Assert.Equal(shown, source.Contains(token));
    }
}
