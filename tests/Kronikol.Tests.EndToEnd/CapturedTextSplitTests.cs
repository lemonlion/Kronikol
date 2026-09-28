namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The browser's fragment splitter and the captured text it cuts through (DIAGRAM_COLOURS_PLAN §12.6). Captured text
/// lives inside notes: a request's body, an assertion's message. The splitter counted any line holding <c>-&gt;</c> or
/// <c>--&gt;</c> as an arrow, so a body quoting <c>a -&gt; b</c> or an HTML comment's <c>--&gt;</c> numbered every later
/// fragment too high, and an assertion message holding one started a unit inside its <c>hnote</c>, so a fragment could
/// end inside the note. Separately, the statement-length check's arrow and block patterns shared their names with the
/// splitter's and replaced them when the page loaded (from 3.0.48): <c>else</c> counted as a block of its own.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class CapturedTextSplitTests : DiagramNotePlaywrightBase
{
    public CapturedTextSplitTests(PlaywrightFixture fixture) : base(fixture) { }

    // Twelve calls in Kronikol's form, each request note holding the given lines, then whatever `extra` adds.
    private const string SequenceJs = """
        function sequence(n, noteLines, extra) {
            var body = [];
            for (var i = 0; i < n; i++) {
                body.push('Caller -[#438DD5]> OrdersAPI: GET: /orders/' + i);
                body.push('note left');
                noteLines.forEach(function (l) { body.push(l); });
                body.push('end note');
                body.push('OrdersAPI -[#438DD5]-> Caller: 200');
                if (extra) extra(i).forEach(function (l) { body.push(l); });
            }
            return ['@startuml', '!pragma teoz true', 'skinparam wrapWidth 800', 'autonumber 1', '',
                'participant "Caller" as Caller', 'participant "Orders API" as OrdersAPI'].concat(body, ['@enduml']).join('\n');
        }
        function realArrows(fragment) {
            return fragment.split('\n').filter(function (l) { return /^\S+ -\[#[0-9A-F]+\]-?> /.test(l); }).length;
        }
        """;

    private async Task OpenPage([System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        await Page.GotoAsync(GenerateColoredArrowReport($"CapturedTextSplit_{testName}.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.WaitForFunctionAsync("() => typeof window._splitDiagramSource === 'function'", null, new() { PollingInterval = 200 });
    }

    [Fact]
    public async Task Arrows_quoted_in_a_note_do_not_number_the_next_fragment()
    {
        await OpenPage();

        var result = await Page.EvaluateAsync<string>($$"""
            () => {
                {{SequenceJs}}
                var frags = window._splitDiagramSource(sequence(12, ['<!-- a comment -->', 'a -> b', 'x --> y']), 400);
                if (frags.length < 3) return 'NOT_SPLIT:' + frags.length;
                var expected = 1, errors = [];
                frags.forEach(function (f, k) {
                    var start = +(f.match(/autonumber (\d+)/) || [])[1];
                    if (start !== expected) errors.push('fragment ' + (k + 1) + ' starts at ' + start + ', expected ' + expected);
                    expected += realArrows(f);
                });
                return errors.length ? errors.join('; ') : 'OK';
            }
        """);

        Assert.Equal("OK", result);
    }

    [Fact]
    public async Task No_fragment_ends_inside_an_assertion_note_whose_message_quotes_an_arrow()
    {
        await OpenPage();

        var result = await Page.EvaluateAsync<string>($$"""
            () => {
                {{SequenceJs}}
                var source = sequence(12, ['{', '  "id": 1', '}'], function (i) {
                    return ['hnote across <<assertionNote>> #F8D7DA', '✗ Expected order ' + i + ' -> shipped', 'but it went a --> b', 'end note'];
                });
                var errors = [];
                for (var height = 150; height <= 1500; height += 25) {
                    window._splitDiagramSource(source, height).forEach(function (f, k) {
                        var open = 0;
                        f.split('\n').forEach(function (l) {
                            var t = l.trim();
                            if (/^[hr]?note\b/i.test(t) && t.indexOf(':') < 0) open++;
                            if (/^end\s*[hr]?note$/i.test(t)) open--;
                        });
                        if (open !== 0) errors.push(height + 'px fragment ' + (k + 1));
                    });
                }
                return errors.length ? errors.length + ' fragments end inside a note: ' + errors.slice(0, 5).join(', ') : 'OK';
            }
        """);

        Assert.Equal("OK", result);
    }

    [Fact]
    public async Task An_else_is_a_branch_of_its_alt_and_not_a_block_of_its_own()
    {
        await OpenPage();

        var result = await Page.EvaluateAsync<string>($$"""
            () => {
                {{SequenceJs}}
                var source = sequence(0, []).replace('@enduml', '');
                var body = [];
                for (var i = 0; i < 40; i++) {
                    body.push('alt stock ' + i);
                    body.push('Caller -[#438DD5]> OrdersAPI: GET: /stock/' + i);
                    body.push('OrdersAPI -[#438DD5]-> Caller: 200');
                    body.push('else no stock');
                    body.push('Caller -[#438DD5]> OrdersAPI: POST: /backorder/' + i);
                    body.push('OrdersAPI -[#438DD5]-> Caller: 202');
                    body.push('end');
                }
                source += body.join('\n') + '\n@enduml';
                var errors = [];
                for (var height = 200; height <= 1200; height += 50) {
                    var frags = window._splitDiagramSource(source, height);
                    if (frags.length < 2) { errors.push(height + 'px: not split'); continue; }
                    frags.forEach(function (f, k) {
                        var depth = 0, bad = null;
                        f.split('\n').forEach(function (l) {
                            var t = l.trim();
                            if (/^alt\b/.test(t)) depth++;
                            else if (t === 'end') { depth--; if (depth < 0) bad = bad || 'an end with no block'; }
                            else if (/^else\b/.test(t) && depth === 0) bad = bad || 'an else outside its alt';
                        });
                        if (!bad && depth !== 0) bad = depth + ' block(s) left open';
                        if (bad) errors.push(height + 'px fragment ' + (k + 1) + ': ' + bad);
                    });
                }
                return errors.length ? errors.length + ' bad fragments: ' + errors.slice(0, 5).join('; ') : 'OK';
            }
        """);

        Assert.Equal("OK", result);
    }

    [Fact]
    public async Task A_service_named_Note_draws_messages_the_splitter_counts()
    {
        // A participant's alias is its service's name camelized (SanitizePlantUmlAlias), so a service called "Note" draws its
        // calls as `Note -[#…]> OrdersAPI: …`, which both engines draw as a message. From 3.31.8 the splitter took any
        // line starting with note, hnote or rnote, in any case, for a note statement, so it counted none of that service's
        // calls: every later fragment was numbered too low. The statement-length check read such a line as a note too,
        // and never named one that was too long.
        await OpenPage();

        var result = await Page.EvaluateAsync<string>($$"""
            () => {
                {{SequenceJs}}
                var errors = [];
                var counted = window._countArrows(['Note -[#438DD5]> OrdersAPI: GET: /orders/1', 'OrdersAPI -[#438DD5]-> Note: 200']);
                if (counted !== 2) errors.push('counted ' + counted + ' of 2 messages');
                var frags = window._splitDiagramSource(sequence(12, ['{"id": 1}']).replace(/\bCaller\b/g, 'Note'), 400);
                if (frags.length < 3) errors.push('not split: ' + frags.length);
                var expected = 1;
                frags.forEach(function (f, k) {
                    var start = +(f.match(/autonumber (\d+)/) || [])[1];
                    if (start !== expected) errors.push('fragment ' + (k + 1) + ' starts at ' + start + ', expected ' + expected);
                    expected += realArrows(f);
                });
                var long = window._findOverLongStatement('@startuml\nNote -[#438DD5]> OrdersAPI: ' + 'x'.repeat(2100) + '\n@enduml');
                if (!long || long.kind !== 'message statement') errors.push('the over-long message was ' + JSON.stringify(long));
                // Every note Kronikol writes is still a note: a request note with its class, an assertion note, a bar.
                var notes = ['note<<requestNote>> left', 'note right', 'hnote across <<assertionNote>> #pink', 'note over OrdersAPI'];
                notes.forEach(function (n) {
                    if (window._countArrows([n, 'a -> b', 'end note']) !== 0) errors.push('the lines of "' + n + '" were counted');
                });
                return errors.length ? errors.join('; ') : 'OK';
            }
        """);

        Assert.Equal("OK", result);
    }
}
