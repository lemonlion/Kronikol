namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class DiagramNoteLongNoteTests : DiagramNotePlaywrightBase
{
    public DiagramNoteLongNoteTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Long_note_dblclick_from_expanded_goes_to_truncated()
    {
        await ExpandAndRenderLongNoteDiagram("LongNoteDblClickExpToTrunc.html");
        await SetScenarioState("expanded");
        await DoubleClickFirstNoteAndWait();

        Assert.True(await Page.Locator("[data-note-btn='minus']").CountAsync() > 0);
    }

    [Fact]
    public async Task Long_note_dblclick_from_truncated_goes_to_collapsed()
    {
        await ExpandAndRenderLongNoteDiagram("LongNoteDblClickTruncToColl.html");
        await SetScenarioState("truncated");

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await DoubleClickFirstNoteAndWait();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Long_note_dblclick_from_collapsed_goes_to_truncated_not_expanded()
    {
        await ExpandAndRenderLongNoteDiagram("LongNoteDblClickCollToTrunc.html");
        await SetScenarioState("collapsed");

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-note-btn=\"plus\"]').length >= 2",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await DoubleClickFirstNoteAndWait();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length < {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        // No up arrows = not expanded
        var hasUpArrow = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || ''))
        """);
        Assert.False(hasUpArrow);
    }

    [Fact]
    public async Task Long_note_full_3_state_cycle_via_dblclick()
    {
        await ExpandAndRenderLongNoteDiagram("LongNote3StateCycle.html");
        await SetScenarioState("expanded");

        await DoubleClickFirstNoteAndWait(); // expanded → truncated
        await DoubleClickFirstNoteAndWait(); // truncated → collapsed

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await DoubleClickFirstNoteAndWait(); // collapsed → truncated

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length < {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Long_note_down_arrow_from_collapsed_goes_to_truncated_not_expanded()
    {
        await ExpandAndRenderLongNoteDiagram("LongNoteDownArrowCollToTrunc.html");
        await SetScenarioState("collapsed");

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await ClickDownArrowAndWait();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length < {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Long_note_down_arrow_from_truncated_goes_to_expanded()
    {
        await ExpandAndRenderLongNoteDiagram("LongNoteDownArrowTruncToExp.html");
        await SetScenarioState("truncated");
        await ClickDownArrowAndWait();

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || ''))
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Long_note_plus_button_from_collapsed_goes_to_truncated()
    {
        await ExpandAndRenderLongNoteDiagram("LongNotePlusCollToTrunc.html");
        await SetScenarioState("collapsed");

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        await ClickNoteButton("[data-note-btn='plus']");

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length < {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Long_note_up_arrow_visible_when_expanded()
    {
        await ExpandAndRenderLongNoteDiagram("LongNoteUpArrowVisible.html");
        await SetScenarioState("expanded");
        await HoverNoteRect(0);

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || '')
                    && i.style.opacity !== '0')
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Long_note_up_arrow_not_visible_when_truncated()
    {
        await ExpandAndRenderLongNoteDiagram("LongNoteUpArrowNotTrunc.html");
        await SetScenarioState("truncated");
        await HoverNoteRect(0);

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => i.style.opacity !== '0')
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        var hasUpArrow = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || ''))
        """);
        Assert.False(hasUpArrow);
    }

    [Fact]
    public async Task Long_note_up_arrow_click_goes_to_truncated()
    {
        await ExpandAndRenderLongNoteDiagram("LongNoteUpArrowToTrunc.html");
        await SetScenarioState("expanded");

        var htmlBefore = await GetSvgHtml();
        await HoverNoteRect(0);

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || '')
                    && i.style.opacity !== '0')
        """, null, new() { Timeout = 10000, PollingInterval = 200 });

        await Page.EvaluateAsync("""
            () => {
                var icon = Array.from(document.querySelectorAll('.note-toggle-icon'))
                    .find(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || ''));
                if (icon) icon.querySelector('rect').dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true}));
            }
        """);
        await WaitForSvgReRender(htmlBefore);

        var hasUpArrow = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || ''))
        """);
        Assert.False(hasUpArrow);
    }

    [Fact]
    public async Task Short_note_dblclick_from_expanded_goes_to_collapsed()
    {
        await ExpandAndRenderLongNoteDiagram("ShortNoteDblClickExpToColl.html");
        await SetScenarioState("expanded");

        var plusBefore = await Page.Locator("[data-note-btn='plus']").CountAsync();
        var htmlBefore = await GetSvgHtml();

        await Page.Locator(".note-hover-rect").Nth(1).EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");
        await WaitForSvgReRender(htmlBefore);

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > {plusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Short_note_dblclick_from_collapsed_goes_to_expanded()
    {
        await ExpandAndRenderLongNoteDiagram("ShortNoteDblClickCollToExp.html");
        await SetScenarioState("collapsed");

        var minusBefore = await Page.Locator("[data-note-btn='minus']").CountAsync();
        var htmlBefore = await GetSvgHtml();

        await Page.Locator(".note-hover-rect").Nth(1).EvaluateAsync(
            "el => el.dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true}))");
        await WaitForSvgReRender(htmlBefore);

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"minus\"]').length > {minusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Short_note_no_up_arrow_when_expanded()
    {
        await ExpandAndRenderLongNoteDiagram("ShortNoteNoUpArrow.html");
        await SetScenarioState("expanded");
        await HoverNoteRect(1);

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => i.style.opacity !== '0')
        """, null, new() { Timeout = 5000, PollingInterval = 200 });

        var hasVisibleUpArrow = await Page.EvaluateAsync<bool>("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .filter(i => i.style.opacity !== '0')
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || ''))
        """);
        Assert.False(hasVisibleUpArrow);
    }

    [Fact]
    public async Task Reducing_truncation_makes_short_note_become_long()
    {
        await ExpandAndRenderLongNoteDiagram("NoteTruncReduceBecomesLong.html");

        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .truncate-lines-select").First.SelectOptionAsync("3");
        await Page.Locator(".note-hover-rect").First.WaitForAsync(new() { Timeout = 10000 });

        await SetScenarioState("expanded");
        await HoverNoteRect(1);

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || '')
                    && i.style.opacity !== '0')
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Scenario_truncation_change_respected_by_note_buttons()
    {
        await ExpandAndRenderLongNoteDiagram("ScenarioTruncChangeButtons.html");

        await OpenScenarioSettingsAsync();
        await Page.Locator(".diagram-toggle .truncate-lines-select").First.SelectOptionAsync("3");
        await Page.Locator(".note-hover-rect").First.WaitForAsync(new() { Timeout = 10000 });

        await SetScenarioState("expanded");
        await HoverNoteRect(0);

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => /^truncate(-bottom)?$/.test(i.getAttribute('data-note-btn') || '')
                    && i.style.opacity !== '0')
        """, null, new() { Timeout = 10000, PollingInterval = 200 });
    }
}