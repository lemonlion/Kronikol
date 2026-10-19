namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that verify step delimiters remain hidden after note collapse/expand
/// when the Steps toggle is off.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class DiagramNoteStepsToggleTests : DiagramNotePlaywrightBase
{
    public DiagramNoteStepsToggleTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Steps_remain_hidden_after_collapsing_note()
    {
        // Navigate to report with step delimiters + notes
        await Page.GotoAsync(GenerateStepDelimitersAndNotesReport("StepsHiddenCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // Verify step delimiters are initially present in the source
        var sourceBeforeToggle = await GetDataPlantuml();
        Assert.Contains("stepDelimiter", sourceBeforeToggle);

        // Click Steps, on, to hide the steps
        await Page.Locator("button[data-toggle='steps']").First.ClickAsync();

        // Wait for re-render after steps toggle
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('stepDelimiter');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // Verify steps are hidden in the source
        var sourceAfterToggle = await GetDataPlantuml();
        Assert.DoesNotContain("stepDelimiter", sourceAfterToggle);

        // Now collapse a note by double-clicking it
        await DoubleClickFirstNoteAndWait();

        // After collapsing, step delimiters should still NOT be in the source
        var sourceAfterCollapse = await GetDataPlantuml();
        Assert.DoesNotContain("stepDelimiter", sourceAfterCollapse);
    }

    [Fact]
    public async Task Steps_remain_hidden_after_expanding_collapsed_note()
    {
        await Page.GotoAsync(GenerateStepDelimitersAndNotesReport("StepsHiddenExpand.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // Hide steps
        await Page.Locator("button[data-toggle='steps']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('stepDelimiter');
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });

        // Collapse a note
        await DoubleClickFirstNoteAndWait();
        Assert.DoesNotContain("stepDelimiter", await GetDataPlantuml());

        // Expand the note again
        await DoubleClickFirstNoteAndWait();

        // Steps should still be hidden after expanding
        var sourceAfterExpand = await GetDataPlantuml();
        Assert.DoesNotContain("stepDelimiter", sourceAfterExpand);
    }

    private async Task<string> GetDataPlantuml()
    {
        return await Page.EvaluateAsync<string>("""
            () => document.querySelector('[data-diagram-type="plantuml"]').getAttribute('data-plantuml') || ''
        """);
    }
}
