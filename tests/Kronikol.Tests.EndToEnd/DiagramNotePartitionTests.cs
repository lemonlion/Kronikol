namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Notes)]
public class DiagramNotePartitionTests : DiagramNotePlaywrightBase
{
    public DiagramNotePartitionTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Note_hover_rects_found_inside_partition_groups()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionNoteHoverRects.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.note-hover-rect').length >= 3",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Note_toggle_icons_found_inside_partition_groups()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionNoteToggleIcons.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.note-toggle-icon').length >= 3",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Partition_note_buttons_respond_to_hover()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionNoteHover.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await HoverNoteRect(0);

        await Page.WaitForFunctionAsync("""
            () => Array.from(document.querySelectorAll('.note-toggle-icon'))
                .some(i => i.style.opacity !== '0')
        """, null, new() { Timeout = 5000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Partition_note_double_click_cycles_state()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionNoteDblClick.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await DoubleClickFirstNoteAndWait();
    }

    [Fact]
    public async Task Partition_note_scenario_collapse_shows_plus_buttons()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionNoteCollapse.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await SetScenarioState("collapsed");

        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-note-btn=\"plus\"]').length > 0",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Partition_svg_structure_has_expected_note_groups()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionSvgStructure.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        var noteGroupCount = await Page.EvaluateAsync<int>("""
            () => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                return svg ? svg.querySelectorAll('.note-hover-rect').length : 0;
            }
        """);
        Assert.True(noteGroupCount >= 3);
    }

    [Fact]
    public async Task Partition_long_notes_have_hover_rects()
    {
        await Page.GotoAsync(GeneratePartitionLongNoteReport("PartitionLongNoteHoverRects.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        Assert.True(await Page.Locator(".note-hover-rect").CountAsync() >= 2);
    }

    [Fact]
    public async Task Partition_long_note_double_click_cycles_state()
    {
        await Page.GotoAsync(GeneratePartitionLongNoteReport("PartitionLongNoteDblClick.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await SetScenarioState("expanded");
        await DoubleClickFirstNoteAndWait();
    }

    [Fact]
    public async Task Partition_long_note_expand_click_works()
    {
        await Page.GotoAsync(GeneratePartitionLongNoteReport("PartitionLongNoteExpand.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        await SetScenarioState("collapsed");
        await Page.Locator("[data-note-btn='plus']").First.WaitForAsync(new() { Timeout = 10000 });

        var minusBefore = await Page.Locator("[data-note-btn='minus']").CountAsync();
        await ClickNoteButton("[data-note-btn='plus']");

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-note-btn=\"minus\"]').length > {minusBefore}",
            null, new() { Timeout = 10000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Safety_net_excludes_non_note_fills_from_collapsible_buttons()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionExcludeParticipant.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // After initial render, count the hover-rects (one per actual note)
        var result = await Page.EvaluateAsync<System.Text.Json.JsonElement>("""
            (() => {
                var svg = document.querySelector('[data-diagram-type="plantuml"] svg');
                var mainG = null;
                for (var i = 0; i < svg.children.length; i++) {
                    if (svg.children[i].tagName === 'g') { mainG = svg.children[i]; break; }
                }
                var SVGNS = 'http://www.w3.org/2000/svg';

                // findNoteGroups uses fold-triangle detection to find only real notes
                var allGroups = window._findNoteGroups(svg).length;

                // But makeNotesCollapsible correctly reconciles — count hover rects as proof
                var hoverRects = svg.querySelectorAll('.note-hover-rect').length;

                // Inject a fake participant-style rectangle (no fold triangle) at the start
                var fakePath = document.createElementNS(SVGNS, 'path');
                fakePath.setAttribute('fill', '#F6F6F6');
                fakePath.setAttribute('d', 'M10,10 L200,10 L200,50 L10,50 Z');
                var fakeText = document.createElementNS(SVGNS, 'text');
                fakeText.textContent = 'FakePartition';
                fakeText.setAttribute('x', '50');
                fakeText.setAttribute('y', '30');

                var firstChild = mainG.firstChild;
                mainG.insertBefore(fakeText, firstChild);
                mainG.insertBefore(fakePath, fakeText);

                // After injection, findNoteGroups should NOT detect the fake rectangle
                // because it lacks the fold triangle that real notes have
                var afterGroups = window._findNoteGroups(svg).length;

                return { allGroups: allGroups, afterGroups: afterGroups, hoverRects: hoverRects };
            })()
        """);

        var allGroups = result.GetProperty("allGroups").GetInt32();
        var afterGroups = result.GetProperty("afterGroups").GetInt32();
        var hoverRects = result.GetProperty("hoverRects").GetInt32();

        // Fold detection correctly excludes the fake rectangle — count stays the same
        Assert.Equal(allGroups, afterGroups);
        // makeNotesCollapsible created the correct number of hover-rects
        Assert.True(hoverRects > 0, "Should have note hover rects from initial render");
    }

    [Fact]
    public async Task FindNoteGroups_still_detects_note_fill_FEFFDD()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionDetectNoteFill.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        Assert.True(await Page.Locator(".note-hover-rect").CountAsync() >= 1);
    }

    [Fact]
    public async Task MakeNotesCollapsible_matches_groups_to_blocks_correctly_when_extra_groups_exist()
    {
        await Page.GotoAsync(GeneratePartitionReport("PartitionGroupBlockMatch.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();

        // One hover rect per matched note, and exactly one of each button kind
        // per note: the − contract button plus the (hidden until eligible)
        // JSON ⇄ YAML format button. A mismatch means groups and blocks
        // misaligned and a note got extra or missing buttons.
        var hoverCount = await Page.Locator(".note-hover-rect").CountAsync();
        var minusCount = await Page.Locator("[data-note-btn='minus']").CountAsync();
        var formatCount = await Page.Locator("[data-note-btn='format']").CountAsync();
        Assert.Equal(hoverCount, minusCount);
        Assert.Equal(hoverCount, formatCount);
    }
}