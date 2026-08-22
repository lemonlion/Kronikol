namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that PlantUML diagrams with colored arrow syntax (-[#color]> and -[#color]->)
/// are correctly split into fragments when they exceed the max diagram height.
/// Regression test for a bug where colored arrows were not detected by indexOf('->'),
/// causing parseTraceUnits, countArrows, and estimateUnitHeight to miss them entirely,
/// leading to malformed fragment splits and PlantUML syntax errors.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class ColoredArrowSplitTests : DiagramNotePlaywrightBase
{
    public ColoredArrowSplitTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CountArrows_detects_colored_forward_arrows()
    {
        await Page.GotoAsync(GenerateColoredArrowReport("CountColoredForward.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var count = await Page.EvaluateAsync<int>("""
            () => {
                var lines = [
                    'caller -[#438DD5]> svc : GET /api',
                    'svc -[#438DD5]-> caller : OK',
                    'caller -> svc : GET /plain',
                    'svc --> caller : OK'
                ];
                return window._countArrows ? window._countArrows(lines) : -1;
            }
        """);

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task CountArrows_detects_colored_return_arrows()
    {
        await Page.GotoAsync(GenerateColoredArrowReport("CountColoredReturn.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var count = await Page.EvaluateAsync<int>("""
            () => {
                var lines = [
                    'svc -[#E74C3C]-> caller : Created',
                    'db -[#9B59B6]-> svc : OK'
                ];
                return window._countArrows ? window._countArrows(lines) : -1;
            }
        """);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task SplitDiagramSource_produces_valid_fragments_with_colored_arrows()
    {
        await Page.GotoAsync(GenerateColoredArrowReport("SplitColoredValid.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var result = await Page.EvaluateAsync<string>("""
            () => {
                var prefix = '@startuml\n!pragma teoz true\nskinparam wrapWidth 800\nautonumber 1\nactor "Caller" as caller\nentity "Service" as svc\n';
                var arrows = Array.from({length: 150}, (_, i) =>
                    'caller -[#438DD5]> svc : GET /api/item/' + (i+1) + '\nsvc -[#438DD5]-> caller : OK'
                ).join('\n');
                var source = prefix + arrows + '\n@enduml';

                if (!window._splitDiagramSource) return 'NO_FUNCTION';
                var frags = window._splitDiagramSource(source);
                if (frags.length < 2) return 'NOT_SPLIT:' + frags.length;

                var errors = [];
                for (var i = 0; i < frags.length; i++) {
                    if (frags[i].indexOf('@startuml') < 0) errors.push('frag ' + i + ': missing @startuml');
                    var hasArrow = /-(?:\[[^\]]*\])?-?>/.test(frags[i]);
                    if (!hasArrow) errors.push('frag ' + i + ': no arrows detected');
                    var autoMatch = frags[i].match(/autonumber\s+(\d+)/);
                    if (!autoMatch) errors.push('frag ' + i + ': missing autonumber');
                }
                return errors.length > 0 ? errors.join('; ') : 'OK:' + frags.length;
            }
        """);

        Assert.StartsWith("OK:", result);
    }

    [Fact]
    public async Task SplitDiagramSource_keeps_loop_blocks_balanced_across_fragments()
    {
        // Collapsed repeated calls render as `loop ×N … end` blocks. A height split that lands inside
        // one used to strand the `end` in the next fragment (and leave the opener unclosed in the
        // previous one) — plantuml.js then reports "Syntax Error? (Assumed diagram type: class)".
        await Page.GotoAsync(GenerateColoredArrowReport("SplitLoopBalanced.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var result = await Page.EvaluateAsync<string>("""
            () => {
                var prefix = '@startuml\n!pragma teoz true\nskinparam wrapWidth 800\nautonumber 1\nactor "Caller" as caller\nentity "Service" as svc\n';
                var body = [];
                for (var i = 0; i < 120; i++) {
                    if (i % 7 === 0) body.push('loop ×3 · 12–40 ms');
                    body.push('caller -[#438DD5]> svc : GET /api/item/' + (i+1));
                    body.push('note left\nx-request-id: ' + i + '\nend note');
                    body.push('svc -[#438DD5]-> caller : OK');
                    if (i % 7 === 0) body.push('end');
                }
                var source = prefix + body.join('\n') + '\n@enduml';

                if (!window._splitDiagramSource) return 'NO_FUNCTION';
                var frags = window._splitDiagramSource(source);
                if (frags.length < 2) return 'NOT_SPLIT:' + frags.length;

                var errors = [];
                for (var i = 0; i < frags.length; i++) {
                    var lines = frags[i].split('\n').map(function (l) { return l.trim(); });
                    var opens = lines.filter(function (l) { return /^loop\b/.test(l); }).length;
                    var closes = lines.filter(function (l) { return l === 'end'; }).length;
                    if (opens !== closes) errors.push('frag ' + i + ': ' + opens + ' loop vs ' + closes + ' end');
                    var firstBody = lines.find(function (l) { return l && !/^(@startuml|!pragma|skinparam|autonumber|actor |entity )/.test(l); });
                    if (firstBody === 'end') errors.push('frag ' + i + ': starts with a stray end');
                    if (!/-(?:\[[^\]]*\])?-?>/.test(frags[i])) errors.push('frag ' + i + ': no arrows');
                }
                return errors.length > 0 ? errors.join('; ') : 'OK:' + frags.length;
            }
        """);

        Assert.StartsWith("OK:", result);
    }

    [Fact]
    public async Task SplitWithChunkedNotes_keeps_loop_blocks_balanced_across_forced_note_boundaries()
    {
        // A huge note forces a split boundary *inside* the note (chunkLargeNotes); when that note sits
        // inside a `loop … end` block the opener and the `end` land in different parts.
        await Page.GotoAsync(GenerateColoredArrowReport("SplitChunkedLoopBalanced.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var result = await Page.EvaluateAsync<string>("""
            () => {
                var prefix = '@startuml\n!pragma teoz true\nskinparam wrapWidth 800\nautonumber 1\nactor "Caller" as caller\nentity "Service" as svc\n';
                var hugeNote = 'note left\n' + Array.from({length: 1200}, (_, i) => '{"row": ' + i + ', "value": "' + 'x'.repeat(40) + '"}').join('\n') + '\nend note';
                var body = [
                    'caller -[#438DD5]> svc : GET /api/first', 'svc -[#438DD5]-> caller : OK',
                    'loop ×4 · 10–20 ms',
                    'caller -[#438DD5]> svc : GET /api/poll', hugeNote, 'svc -[#438DD5]-> caller : OK',
                    'end',
                    'caller -[#438DD5]> svc : GET /api/last', 'svc -[#438DD5]-> caller : OK'
                ];
                var source = prefix + body.join('\n') + '\n@enduml';

                if (!window._splitWithChunkedNotes) return 'NO_FUNCTION';
                var frags = window._splitWithChunkedNotes(source);
                if (frags.length < 2) return 'NOT_SPLIT:' + frags.length;

                var errors = [];
                for (var i = 0; i < frags.length; i++) {
                    var lines = frags[i].split('\n').map(function (l) { return l.trim(); });
                    var inNote = false, opens = 0, closes = 0;
                    for (var k = 0; k < lines.length; k++) {
                        var l = lines[k];
                        if (!inNote && /^h?note\b/.test(l) && l.indexOf(':') < 0) { inNote = true; continue; }
                        if (inNote) { if (l === 'end note') inNote = false; continue; }
                        if (/^loop\b/.test(l)) opens++;
                        else if (l === 'end') closes++;
                    }
                    if (opens !== closes) errors.push('frag ' + i + ': ' + opens + ' loop vs ' + closes + ' end');
                }
                return errors.length > 0 ? errors.join('; ') : 'OK:' + frags.length;
            }
        """);

        Assert.StartsWith("OK:", result);
    }

    [Fact]
    public async Task SplitDiagramSource_autonumber_accounts_for_colored_arrows()
    {
        await Page.GotoAsync(GenerateColoredArrowReport("SplitColoredAutonum.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var result = await Page.EvaluateAsync<string>("""
            () => {
                var prefix = '@startuml\nautonumber 1\nactor "Caller" as caller\nentity "Service" as svc\n';
                var arrows = Array.from({length: 150}, (_, i) =>
                    'caller -[#438DD5]> svc : GET /item/' + (i+1) + '\nsvc -[#438DD5]-> caller : OK'
                ).join('\n');
                var source = prefix + arrows + '\n@enduml';

                if (!window._splitDiagramSource) return 'NO_FUNCTION';
                var frags = window._splitDiagramSource(source);
                if (frags.length < 2) return 'NOT_SPLIT:' + frags.length;

                var autonums = [];
                for (var i = 0; i < frags.length; i++) {
                    var m = frags[i].match(/autonumber\s+(\d+)/);
                    autonums.push(m ? parseInt(m[1]) : -1);
                }
                if (autonums[0] !== 1) return 'FIRST_NOT_1:' + autonums[0];
                for (var i = 1; i < autonums.length; i++) {
                    if (autonums[i] <= autonums[i-1])
                        return 'NON_INCREASING:' + JSON.stringify(autonums);
                }
                return 'OK:' + JSON.stringify(autonums);
            }
        """);

        Assert.StartsWith("OK:", result);
    }

    [Fact]
    public async Task Colored_arrow_diagram_renders_all_fragments_without_syntax_error()
    {
        await Page.GotoAsync(GenerateColoredArrowReport("ColoredArrowRender.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Wait for all fragments to render (including any re-splits from "too large" errors)
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var svgs = container.querySelectorAll('svg');
                return svgs.length >= 1;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });

        // Check that no SVG contains "Syntax Error" text
        var syntaxErrorText = await Page.EvaluateAsync<string>("""
            () => {
                var texts = document.querySelectorAll('[data-diagram-type="plantuml"] svg text');
                for (var i = 0; i < texts.length; i++) {
                    if (texts[i].textContent.indexOf('Syntax Error') >= 0)
                        return texts[i].textContent;
                }
                return '';
            }
        """);

        Assert.True(string.IsNullOrEmpty(syntaxErrorText),
            $"Found PlantUML syntax error in rendered diagram: {syntaxErrorText}");
    }
}
