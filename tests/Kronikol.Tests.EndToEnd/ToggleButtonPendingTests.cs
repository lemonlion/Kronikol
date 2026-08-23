using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A toolbar control that triggers a re-render carries <c>details-pending</c> (throb + spinner, delayed
/// 0.2 s in CSS) from the click until the last diagram of that action is drawn — and never afterwards.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ToggleButtonPendingTests : DiagramNotePlaywrightBase
{
    public ToggleButtonPendingTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenRendered(string fileName)
    {
        // Long notes with headers: Expand and the Headers toggle really re-render (short notes would queue nothing).
        await Page.GotoAsync(GenerateLongNoteWithHeadersReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await RenderAllDiagramsAndWait(1);
    }

    [Fact]
    public async Task Clicked_scenario_button_is_pending_until_its_rerender_completes()
    {
        await OpenRendered("PendingScenarioExpand.html");
        var scenario = Page.Locator("details.scenario").First;
        var expand = scenario.Locator(".details-radio-btn[data-state='expanded']");

        // Pending is set synchronously in the click handler…
        var pendingRightAfterClick = await expand.EvaluateAsync<bool>("b => { b.click(); return b.classList.contains('details-pending') && b.getAttribute('aria-busy') === 'true'; }");
        Assert.True(pendingRightAfterClick, "the clicked button must be marked pending as soon as the re-render starts");

        // …and cleared once the queue has drawn everything.
        await expand.EvaluateAsync("b => new Promise(r => { (function w() { if (!b.classList.contains('details-pending')) r(); else setTimeout(w, 50); })(); })", null, new() { Timeout = 30000 });
        Assert.False(await expand.EvaluateAsync<bool>("b => b.hasAttribute('data-pending') || b.hasAttribute('aria-busy')"));
        Assert.Equal("", await expand.EvaluateAsync<string>("b => getComputedStyle(b, '::after').content === 'none' ? '' : 'spinner-still-there'"));
    }

    [Fact]
    public async Task Report_level_toggle_marks_every_synced_button_and_a_no_op_clears_at_once()
    {
        await OpenRendered("PendingReportHeaders.html");
        var reportHeaders = Page.Locator(".toolbar-right .toggle-btn[data-toggle='headers']");

        var counts = await reportHeaders.EvaluateAsync<string>("""
            b => { b.click();
                   var all = document.querySelectorAll('.toggle-btn[data-toggle="headers"]');
                   var pending = document.querySelectorAll('.toggle-btn[data-toggle="headers"].details-pending');
                   return all.length + ':' + pending.length; }
        """);
        var parts = counts.Split(':');
        Assert.True(int.Parse(parts[0]) >= 2, "report + scenario headers buttons expected");
        Assert.Equal(parts[0], parts[1]); // every synced peer is pending together
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('.details-pending').length === 0", null, new() { Timeout = 30000 });

        // Re-applying the state the scenario is already in queues nothing: pending must not linger.
        var scenario = Page.Locator("details.scenario").First;
        var truncate = scenario.Locator(".details-radio-btn[data-state='truncated']");
        await truncate.EvaluateAsync("b => b.click()");
        await Page.WaitForFunctionAsync("() => document.querySelectorAll('.details-pending').length === 0", null, new() { Timeout = 5000 });
    }
}
