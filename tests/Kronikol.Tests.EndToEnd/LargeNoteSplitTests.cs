namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Tests that PlantUML diagrams with notes exceeding _maxNoteChars (15000 chars)
/// are correctly split by chunkLargeNotes without producing unclosed notes.
/// Regression test for a bug where Part 0 of a chunked split (which has @startuml
/// but no @enduml) had its last 'end note' line excluded by parseDiagramStructure,
/// leaving an unclosed note in the height-split fragment.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class LargeNoteSplitTests : DiagramNotePlaywrightBase
{
    public LargeNoteSplitTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task SplitWithChunkedNotes_produces_closed_notes_in_all_fragments()
    {
        await Page.GotoAsync(GenerateLargeNoteReport("LargeNoteClosedNotes.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var result = await Page.EvaluateAsync<string>("""
            () => {
                // Build a diagram with 50 colored arrow pairs (small notes) + one very large note
                var prefix = '@startuml\n!pragma teoz true\nskinparam wrapWidth 800\nautonumber 1\nactor "Caller" as caller\nentity "Service" as svc\ndatabase "DB" as db\n\n';

                var body = '';
                for (var i = 1; i <= 50; i++) {
                    body += 'caller -[#438DD5]> svc : GET /api/item/' + i + '\n';
                    body += 'note left\n<color:#686868>[traceparent=00-abc-' + i + '-00]\nend note\n';
                    body += 'svc -[#438DD5]-> caller : OK\n';
                    body += 'note right\n{"id":' + i + '}\nend note\n';
                }

                // One arrow pair with a very large note (>15000 chars)
                body += 'svc -[#E74C3C]> db : Query /data\n';
                body += 'note left\nSELECT * FROM items\nend note\n';
                body += 'db -[#E74C3C]-> svc : OK\n';
                body += 'note right\n{\n';
                for (var j = 0; j < 500; j++) {
                    body += '  "item_' + ('000' + j).slice(-4) + '": "value_' + ('000' + j).slice(-4) + '_xxxxxxxxxxxx",\n';
                }
                body += '}\nend note\n';

                // One more arrow after
                body += 'svc -[#438DD5]-> caller : Done\n';

                var source = prefix + body + '@enduml';

                if (!window._splitWithChunkedNotes) return 'NO_FUNCTION';
                var frags = window._splitWithChunkedNotes(source);
                if (frags.length < 2) return 'NOT_SPLIT:' + frags.length;

                var errors = [];
                for (var i = 0; i < frags.length; i++) {
                    if (frags[i].indexOf('@startuml') < 0)
                        errors.push('frag ' + i + ': missing @startuml');
                    if (frags[i].indexOf('@enduml') < 0)
                        errors.push('frag ' + i + ': missing @enduml');

                    // Check that all note left/right have matching end note
                    var lines = frags[i].split('\n');
                    var inNote = false;
                    for (var li = 0; li < lines.length; li++) {
                        var t = lines[li].trim();
                        if (/^note(?:<<\w+>>)?\s+(left|right)/.test(t) && !/^note over/.test(t)) {
                            if (inNote) errors.push('frag ' + i + ' line ' + li + ': nested note open');
                            inNote = true;
                        } else if (t === 'end note') {
                            inNote = false;
                        }
                    }
                    if (inNote) errors.push('frag ' + i + ': unclosed note at end of fragment');
                }

                return errors.length > 0 ? 'ERRORS: ' + errors.join('; ') : 'OK:' + frags.length;
            }
        """);

        Assert.StartsWith("OK:", result);
    }

    [Fact]
    public async Task Large_note_diagram_renders_all_fragments_without_syntax_error()
    {
        await Page.GotoAsync(GenerateLargeNoteReport("LargeNoteRender.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();

        // Wait for all fragments to render
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

    [Fact]
    public async Task Intermediate_note_chunks_are_anchored_to_participant()
    {
        await Page.GotoAsync(GenerateLargeNoteReport("LargeNoteAnchored.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Build a source where the large note produces 3+ chunks,
        // so at least one intermediate chunk (no arrows) exists.
        // Verify that intermediate fragments anchor the note to a participant
        // (via 'note ... of <participant>' or by having a preceding arrow),
        // otherwise PlantUML renders an empty diagram (just participants, no content).
        var result = await Page.EvaluateAsync<string>("""
            () => {
                var prefix = '@startuml\n!pragma teoz true\nskinparam wrapWidth 800\nautonumber 1\nactor "Caller" as caller\nentity "Service" as svc\ndatabase "DB" as db\n\n';

                var body = '';
                for (var i = 1; i <= 10; i++) {
                    body += 'caller -[#438DD5]> svc : GET /api/item/' + i + '\n';
                    body += 'note left\n<color:#686868>[traceparent=00-abc-' + i + '-00]\nend note\n';
                    body += 'svc -[#438DD5]-> caller : OK\n';
                    body += 'note right\n{"id":' + i + '}\nend note\n';
                }

                // One arrow pair with a very large note (>30000 chars → 3 chunks)
                body += 'svc -[#E74C3C]> db : Query /data\n';
                body += 'note left\nSELECT * FROM items\nend note\n';
                body += 'db -[#E74C3C]-> svc : OK\n';
                body += 'note right\n{\n';
                for (var j = 0; j < 750; j++) {
                    body += '  "item_' + ('000' + j).slice(-4) + '": "value_' + ('000' + j).slice(-4) + '_xxxxxxxxxxxxxxxxxxxx",\n';
                }
                body += '}\nend note\n';
                body += 'svc -[#438DD5]-> caller : Done\n';

                var source = prefix + body + '@enduml';

                if (!window._splitWithChunkedNotes) return 'NO_FUNCTION';
                var frags = window._splitWithChunkedNotes(source);
                if (frags.length < 3) return 'NOT_ENOUGH_FRAGS:' + frags.length;

                // For each fragment, check that any 'note right' or 'note left' either:
                // 1. Uses 'of <participant>' syntax, OR
                // 2. Is preceded by an arrow line in the body
                var errors = [];
                for (var i = 0; i < frags.length; i++) {
                    var lines = frags[i].split('\n');
                    var bodyStarted = false;
                    var seenArrowInBody = false;
                    for (var li = 0; li < lines.length; li++) {
                        var t = lines[li].trim();
                        if (t === '@enduml') break;
                        // Detect body start (after participants/autonumber/etc)
                        if (!bodyStarted) {
                            if (t === '' || t.startsWith('@startuml') || t.startsWith('!') ||
                                t.startsWith('skinparam') || t.startsWith('hide ') ||
                                t.startsWith('autonumber') || t.startsWith('participant ') ||
                                t.startsWith('actor ') || t.startsWith('entity ') ||
                                t.startsWith('database ') || t.startsWith('<style')) {
                                continue;
                            }
                            // Check for multi-line style block
                            if (t === '}') continue;
                            bodyStarted = true;
                        }
                        if (/^.+-(?:\[[^\]]*\])?-?>/.test(t)) {
                            seenArrowInBody = true;
                        }
                        // Check bare 'note left' or 'note right' without 'of'
                        var noteMatch = t.match(/^note(?:<<\w+>>)?\s+(left|right)\s*$/);
                        if (noteMatch && !seenArrowInBody) {
                            errors.push('frag ' + i + ': bare "' + t + '" without preceding arrow or "of <participant>"');
                        }
                    }
                }

                return errors.length > 0 ? 'ERRORS: ' + errors.join('; ') : 'OK:' + frags.length;
            }
        """);

        Assert.StartsWith("OK:", result);
    }

    /// <summary>
    /// Regression test: when the arrow before a large note uses colon-adjacent format
    /// (e.g. "db -[#E74C3C]-> svc: OK" with NO space before the colon), the anchor
    /// participant regex must not capture the trailing colon. A captured colon produces
    /// "note right of svc:" which PlantUML interprets as single-line note syntax,
    /// breaking the multi-line note structure and causing a syntax error.
    /// </summary>
    [Fact]
    public async Task Anchor_participant_does_not_include_trailing_colon()
    {
        await Page.GotoAsync(GenerateLargeNoteReport("LargeNoteColonAnchor.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        var result = await Page.EvaluateAsync<string>("""
            () => {
                // Build source with colon-adjacent arrow format (no space before colon)
                var prefix = '@startuml\n!pragma teoz true\n<style>\n .eventNote {\n     BackgroundColor #cfecf7\n     FontSize 11\n     RoundCorner 10\n }\n</style>\n<style>\n .assertionNote {\n     FontSize 11\n     RoundCorner 5\n }\n</style>\nskinparam wrapWidth 800\nautonumber 1\n\nactor "Caller" as caller\nentity "Breakfast Provider" as breakfastProvider\ndatabase "CosmosDB" as cosmosDB\n\n';

                var body = '';
                for (var i = 1; i <= 20; i++) {
                    body += 'caller -[#438DD5]> breakfastProvider: GET /api/item/' + i + '\n';
                    body += 'note left\n<color:#686868>[traceparent=00-abc-' + i + '-00]\nend note\n';
                    body += 'breakfastProvider -[#438DD5]-> caller: OK\n';
                    body += 'note right\n{"id":' + i + '}\nend note\n';
                }

                // Arrow with colon directly adjacent to participant name (NO space before colon)
                body += 'breakfastProvider -[#E74C3C]> cosmosDB: Query /orders\n';
                body += 'note left\nSELECT * FROM orders\nend note\n';
                body += 'cosmosDB -[#E74C3C]-> breakfastProvider: OK\n';
                body += 'note right\n{\n';
                for (var j = 0; j < 750; j++) {
                    body += '  "item_' + ('000' + j).slice(-4) + '": "value_' + ('000' + j).slice(-4) + '_xxxxxxxxxxxxxxxxxxxx",\n';
                }
                body += '}\nend note\n';
                body += 'breakfastProvider -[#438DD5]-> caller: Done\n';

                var source = prefix + body + '@enduml';

                if (!window._splitWithChunkedNotes) return 'NO_FUNCTION';
                var frags = window._splitWithChunkedNotes(source);
                if (frags.length < 3) return 'NOT_ENOUGH_FRAGS:' + frags.length;

                // Check that NO fragment contains 'note ... of participant:' (colon in participant)
                var errors = [];
                for (var i = 0; i < frags.length; i++) {
                    var lines = frags[i].split('\n');
                    for (var li = 0; li < lines.length; li++) {
                        var t = lines[li].trim();
                        var m = t.match(/^note\s+(?:left|right)\s+of\s+(\S+)/);
                        if (m && m[1].indexOf(':') >= 0) {
                            errors.push('frag ' + i + ' line ' + li + ': anchor has colon: "' + t + '"');
                        }
                    }
                    // Also verify fragment has participant declarations
                    if (frags[i].indexOf('actor ') < 0 && frags[i].indexOf('participant ') < 0
                        && frags[i].indexOf('entity ') < 0) {
                        errors.push('frag ' + i + ': missing participant declarations');
                    }
                }

                return errors.length > 0 ? 'ERRORS: ' + errors.join('; ') : 'OK:' + frags.length;
            }
        """);

        Assert.StartsWith("OK:", result);
    }
}
