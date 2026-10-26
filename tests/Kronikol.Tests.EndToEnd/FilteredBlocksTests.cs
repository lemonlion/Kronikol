using System.Net;
using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A block goes with what it holds (5.9.1, plans/REPORT_UI_FIXES_PLAN.md I3). The Databases and Events filters take a
/// call's arrows and notes out of the source, Assertions and Steps their notes, and until 5.9.1 the block around them
/// stayed: a loop of database calls was drawn as an empty loop box, a Setup box of them as an empty Setup box, and a
/// user's own group as an empty frame. Every drawing, every redraw and the copied source read the one filtered source, so
/// the facts read that source and the picture drawn from it. The emitter's own source is used where it writes the block
/// (a loop, the Setup box), a hand-written one where a user does (TrackingDiagramOverride.InsertPlantUml).
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class FilteredBlocksTests : DiagramNotePlaywrightBase
{
    public FilteredBlocksTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly DateTimeOffset Start = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static IEnumerable<RequestResponseLog> Call(string caller, string service, string method, string url, int at, string? category = null)
    {
        var trace = Guid.NewGuid();
        var id = Guid.NewGuid();
        yield return new RequestResponseLog("Collect milk", "s1", method, "{}", new Uri(url), [], service, caller,
            RequestResponseType.Request, trace, id, false, DependencyCategory: category) { Timestamp = Start.AddMilliseconds(at) };
        yield return new RequestResponseLog("Collect milk", "s1", method, "{}", new Uri(url), [], service, caller,
            RequestResponseType.Response, trace, id, false, HttpStatusCode.OK, DependencyCategory: category) { Timestamp = Start.AddMilliseconds(at + 5) };
    }

    // three alike calls, which the emitter folds into a loop
    private static IEnumerable<RequestResponseLog> Thrice(string caller, string service, string method, string url, int at, string? category = null) =>
        Enumerable.Range(0, 3).SelectMany(i => Call(caller, service, method, url, at + i * 10, category));

    // a marker as TrackingDiagramOverride.InsertPlantUml logs one, its markup on lines of its own
    private static IEnumerable<RequestResponseLog> Marker(string plantUml, DiagramMarkerKind kind, int at)
    {
        yield return new RequestResponseLog("Collect milk", "s1", "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = "\n" + plantUml + "\n", MarkerKind = kind, Timestamp = Start.AddMilliseconds(at) };
        yield return new RequestResponseLog("Collect milk", "s1", "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideEnd = true, MarkerKind = kind, Timestamp = Start.AddMilliseconds(at) };
    }

    private static IEnumerable<RequestResponseLog> ActionStart(int at)
    {
        yield return new RequestResponseLog("Collect milk", "s1", "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { IsActionStart = true, Timestamp = Start.AddMilliseconds(at) };
    }

    // an assertion as Track.That writes one: its note, then the comment naming the line it was written on
    private static string Assertion(string text, int line) =>
        $"hnote across <<assertionNote>> #d4edda\n\u2713 {text}\nend note\n'__^*__:Dairy.cs:L{line}";

    private static string Emit(params IEnumerable<RequestResponseLog>[] parts) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId(parts.SelectMany(p => p).ToList(), separateSetup: true,
            collapseConsecutiveIdenticalCalls: true, systemUiFont: true).Single().PlantUmls.Single().PlainText;

    // a source as a user's own markup writes one, teoz first as the emitter's is (the role pass reads sequence diagrams by it)
    private static string Written(string body) =>
        "@startuml\n!pragma teoz true\nparticipant \"Api\" as api\nparticipant \"Worker\" as worker\ndatabase \"MilkDb\" as milkDb\n"
        + body.Trim().Replace("\r\n", "\n") + "\n@enduml";

    // the milking run: a setup call, then a loop of database calls and a loop of calls to the barn
    private static string MilkingSource(string? category = "SQL", string service = "MilkDb") => Emit(
        Call("Test", "CowService", "PUT", "http://cows.internal/herd", 0),
        ActionStart(40),
        Call("Test", "CowService", "POST", "http://cows.internal/milk", 50),
        Thrice("CowService", service, "Query", "sql://milk-db/Cows", 60, category),
        Thrice("CowService", "Barn", "GET", "http://barn.internal/stalls", 100));

    private async Task OpenAsync(string fileName, string source, ResolvedToggleDefaults? defaults = null, bool roles = true)
    {
        Feature[] features =
        [
            new()
            {
                DisplayName = "Dairy",
                Scenarios = [new() { Id = "s1", DisplayName = "Collect milk", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Dairy", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, toggleDefaults: defaults);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        if (roles)
            await Page.WaitForFunctionAsync(
                "() => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return !!s && !!s.getAttribute('data-k-roles'); }",
                null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    // the source the diagram is drawn from, which every reader of the filtered source reads
    private Task<string> DrawnSourceAsync() => Page.EvaluateAsync<string>(
        "() => document.querySelector('[data-diagram-type=\"plantuml\"]').getAttribute('data-plantuml')");

    // a scenario's own toggle, clicked as a reader clicks it, and the drawing it starts waited for
    private async Task ToggleAsync(string toggle)
    {
        var before = await DrawnSourceAsync();
        await OpenScenarioSettingsAsync();
        await Page.Locator($"details.scenario .toggle-btn[data-toggle='{toggle}']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            before => {
                const c = document.querySelector('[data-diagram-type="plantuml"]');
                return !!c && c.getAttribute('data-plantuml') !== before && !c._noteRendering && !window._plantumlRendering
                    && !!c.querySelector('svg');
            }
            """, before, new() { Timeout = 30000, PollingInterval = 200 });
    }

    private static string[] Lines(string source) => source.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToArray();

    private static int Count(string source, string pattern) => Lines(source).Count(l => Regex.IsMatch(l, pattern));

    // how many loop boxes the picture draws, by the keyword in a loop's tab, and whether it draws the Setup box, by its
    // title (which the engine writes as two texts)
    private Task<int[]> DrawnBoxesAsync() => Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<int[]>("""
        el => {
            const texts = Array.from(el.querySelectorAll('text')).map(t => t.textContent);
            return [texts.filter(t => t === 'loop').length, texts.some(t => t === 'Setup') ? 1 : 0];
        }
        """);

    private Task<bool> SyntaxErrorAsync() => Page.EvaluateAsync<bool>(
        "() => /Syntax Error/.test(document.querySelector('[data-diagram-type=\"plantuml\"]').textContent)");

    // L1: a loop of database calls goes with them and comes back with them, and the loop of other calls stays
    [Fact]
    public async Task Hiding_the_databases_takes_their_loop_with_them_and_showing_them_brings_it_back()
    {
        var source = MilkingSource();
        Assert.Contains("\ndatabase \"", source);
        Assert.Equal(2, Count(source, "^loop "));
        await OpenAsync("BlocksDatabasesLoop.html", source);
        var shown = await DrawnSourceAsync();
        Assert.Equal(2, (await DrawnBoxesAsync())[0]);

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        Assert.DoesNotContain("milkDb", hidden);
        Assert.Equal(1, Count(hidden, "^loop "));
        Assert.Equal(Count(hidden, "^(loop|partition)\\b"), Count(hidden, "^end$"));
        Assert.Equal(1, (await DrawnBoxesAsync())[0]);

        await ToggleAsync("databases");
        Assert.Equal(shown, await DrawnSourceAsync());
        Assert.Equal(2, (await DrawnBoxesAsync())[0]);
    }

    // L1: the same for a broker's calls under the Events filter
    [Fact]
    public async Task Hiding_the_events_takes_their_loop_with_them()
    {
        var source = MilkingSource(category: "MessageQueue", service: "HerdEvents");
        Assert.Contains("\nqueue \"", source);
        await OpenAsync("BlocksEventsLoop.html", source);
        Assert.Equal(2, (await DrawnBoxesAsync())[0]);

        await ToggleAsync("events");
        var hidden = await DrawnSourceAsync();
        Assert.DoesNotContain("herdEvents", hidden);
        Assert.Equal(1, Count(hidden, "^loop "));
        Assert.Equal(Count(hidden, "^(loop|partition)\\b"), Count(hidden, "^end$"));
        Assert.Equal(1, (await DrawnBoxesAsync())[0]);
    }

    // L4: a parent whose only content was an emptied block goes too, so a Setup box holding only a loop of database calls
    // goes with the loop; one that still holds a step bar stays
    [Fact]
    public async Task A_Setup_box_left_empty_goes_and_one_still_holding_a_step_bar_stays()
    {
        var onlyCalls = Emit(Thrice("CowService", "MilkDb", "Query", "sql://milk-db/Cows", 0, "SQL"),
            ActionStart(40), Call("Test", "CowService", "POST", "http://cows.internal/milk", 50));
        Assert.Equal(1, Count(onlyCalls, "^partition\\b"));
        await OpenAsync("BlocksSetupEmptied.html", onlyCalls);
        Assert.Equal(new[] { 1, 1 }, await DrawnBoxesAsync());

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        Assert.Equal(0, Count(hidden, "^partition\\b"));
        Assert.Equal(0, Count(hidden, "^loop "));
        Assert.Equal(0, Count(hidden, "^end$"));
        Assert.Equal(new[] { 0, 0 }, await DrawnBoxesAsync());

        var withBar = Emit(Marker(StepBarPlantUml.Build("Given a herd of three"), DiagramMarkerKind.Step, 0),
            Thrice("CowService", "MilkDb", "Query", "sql://milk-db/Cows", 10, "SQL"),
            ActionStart(50), Call("Test", "CowService", "POST", "http://cows.internal/milk", 60));
        await OpenAsync("BlocksSetupWithBar.html", withBar);
        await ToggleAsync("databases");
        hidden = await DrawnSourceAsync();
        Assert.Equal(1, Count(hidden, "^partition\\b"));
        Assert.Equal(0, Count(hidden, "^loop "));
        Assert.Equal(1, Count(hidden, "^end$"));
        Assert.Equal(new[] { 0, 1 }, await DrawnBoxesAsync());
    }

    // L3: an assertion's location comment is no content, so a Setup box left holding only that comment goes (the emitter
    // writes a Setup box for a setup with a call in it, here a database's)
    [Fact]
    public async Task An_emptied_Setup_box_holding_an_assertions_location_comment_goes()
    {
        var source = Emit(Marker(StepBarPlantUml.Build("Given a herd of three"), DiagramMarkerKind.Step, 0),
            Marker(Assertion("the herd is three cows", 12), DiagramMarkerKind.Assertion, 5),
            Call("CowService", "MilkDb", "Query", "sql://milk-db/Cows", 10, "SQL"),
            ActionStart(20),
            Marker(StepBarPlantUml.Build("When the milk is collected"), DiagramMarkerKind.Step, 30),
            Call("Test", "CowService", "POST", "http://cows.internal/milk", 40));
        Assert.Equal(1, Count(source, "^partition\\b"));
        await OpenAsync("BlocksSetupComment.html", source);

        await ToggleAsync("databases");
        await ToggleAsync("assertions");
        await ToggleAsync("steps");
        var hidden = await DrawnSourceAsync();
        Assert.Contains("'__^*__:Dairy.cs:L12", hidden);
        Assert.Equal(0, Count(hidden, "^partition\\b"));
        Assert.Equal(0, Count(hidden, "^end$"));
        Assert.Equal(new[] { 0, 0 }, await DrawnBoxesAsync());
    }

    // L2, L4: a user's box around a database's declaration goes when the filter takes the declaration out
    [Fact]
    public async Task A_user_box_emptied_by_the_databases_filter_goes()
    {
        var source = "@startuml\n!pragma teoz true\nparticipant \"Api\" as api\nbox \"Storage\" #EEEEEE\ndatabase \"MilkDb\" as milkDb\nend box\n"
            + "api -> milkDb: SELECT cows\nmilkDb --> api: rows\napi -> api: count them\n@enduml";
        await OpenAsync("BlocksUserBox.html", source);

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        Assert.Equal(0, Count(hidden, "^box\\b"));
        Assert.Equal(0, Count(hidden, "^end box$"));
        Assert.Contains("api -> api: count them", hidden);
        Assert.False(await SyntaxErrorAsync());
    }

    // L2: an opener in any case, par2 and a parallel group are blocks, and so is `end group` as a closer; `end box`,
    // `end note` and `end ref` close their own blocks and are left alone
    [Fact]
    public async Task Openers_and_ends_are_read_as_the_engine_reads_them()
    {
        var source = "@startuml\n!pragma teoz true\nbox \"Edge\" #EEEEEE\nparticipant \"Api\" as api\nend box\n"
            + "participant \"Worker\" as worker\ndatabase \"MilkDb\" as milkDb\n" + """
            api -> worker: start
            Loop each cow
            api -> milkDb: SELECT 1
            end
            GROUP audit
            api -> milkDb: SELECT 2
            end group
            par2 lanes
            api -> milkDb: SELECT 3
            end
            api -> worker: next
            & group parallel
            api -> milkDb: SELECT 4
            end
            note over api
            a note
            end note
            ref over api, worker
            a reference
            end ref
            @enduml
            """.Replace("\r\n", "\n");
        await OpenAsync("BlocksOpeners.html", source);
        Assert.False(await SyntaxErrorAsync());

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        foreach (var gone in new[] { "Loop each cow", "GROUP audit", "end group", "par2 lanes", "& group parallel" })
            Assert.DoesNotContain(gone, Lines(hidden));
        Assert.Equal(0, Count(hidden, "^end$"));
        foreach (var kept in new[] { "box \"Edge\" #EEEEEE", "end box", "note over api", "end note", "ref over api, worker", "end ref" })
            Assert.Contains(kept, Lines(hidden));
        Assert.False(await SyntaxErrorAsync());
    }

    // L3: a destroy or a return is content, so the block holding one stays
    [Fact]
    public async Task A_destroy_or_a_return_keeps_its_block()
    {
        await OpenAsync("BlocksDestroyReturn.html", Written("""
            api -> worker: ask
            activate worker
            group answer
            worker -> milkDb: SELECT
            milkDb --> worker: rows
            return answer
            end
            group cleanup
            api -> milkDb: DELETE
            destroy worker
            end
            """));

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        Assert.Contains("group answer", Lines(hidden));
        Assert.Contains("group cleanup", Lines(hidden));
        Assert.Equal(2, Count(hidden, "^end$"));
        Assert.False(await SyntaxErrorAsync());
    }

    // L3, L4: a group with a call left stays, and an alt with one live branch stays whole, its emptied branch and its
    // else included
    [Fact]
    public async Task A_user_group_partly_emptied_stays_and_an_alt_with_one_live_branch_stays_whole()
    {
        await OpenAsync("BlocksPartlyEmptied.html", Written("""
            group mixed
            api -> milkDb: SELECT
            api -> worker: work
            end
            alt cached
            api -> milkDb: GET
            else fresh
            api -> worker: compute
            end
            """));

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        foreach (var kept in new[] { "group mixed", "alt cached", "else fresh", "api -> worker: work", "api -> worker: compute" })
            Assert.Contains(kept, Lines(hidden));
        Assert.Equal(2, Count(hidden, "^end$"));
    }

    // L1: a block written empty is the user's; only a block the filters emptied goes
    [Fact]
    public async Task A_user_block_written_empty_is_left_as_written()
    {
        await OpenAsync("BlocksWrittenEmpty.html", Written("""
            api -> worker: start
            group written empty
            end
            group database only
            api -> milkDb: SELECT
            end
            """));

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        Assert.Contains("group written empty", Lines(hidden));
        Assert.DoesNotContain("group database only", Lines(hidden));
        Assert.Equal(1, Count(hidden, "^end$"));
    }

    // L2: a note's lines are no statements, an opener or an `end` among them included, and a style block's `group {` is
    // no block: a note the filter takes out held `loop over rows`, which read as an opener left the blocks before and
    // after the filter out of step
    [Fact]
    public async Task A_note_line_reading_end_and_a_style_block_with_group_are_not_blocks()
    {
        var source = "@startuml\n!pragma teoz true\n<style>\ngroup {\n  LineColor #888888\n}\n</style>\n"
            + "participant \"Api\" as api\nparticipant \"Worker\" as worker\ndatabase \"MilkDb\" as milkDb\n" + """
            api -> worker: start
            note right
            end
            end note
            api -> milkDb: SELECT
            note right
            loop over rows
            end
            end note
            group database only
            api -> milkDb: UPDATE
            end
            @enduml
            """.Replace("\r\n", "\n");
        await OpenAsync("BlocksNoteLines.html", source);

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        Assert.DoesNotContain("group database only", Lines(hidden));
        Assert.DoesNotContain("loop over rows", Lines(hidden));
        // the kept note's body line `end` is all that reads `end`
        Assert.Equal(1, Count(hidden, "^end$"));
        Assert.Contains("group {", Lines(hidden));
        Assert.False(await SyntaxErrorAsync());
    }

    // L5: the first drawing of a report whose Databases toggle starts off has no empty loop
    [Fact]
    public async Task A_first_drawing_with_the_databases_hidden_has_no_empty_loop()
    {
        await OpenAsync("BlocksDatabasesOffAtStart.html", MilkingSource(), ResolvedToggleDefaults.BuiltIn with { DatabasesShown = false });

        var drawn = await DrawnSourceAsync();
        Assert.DoesNotContain("milkDb", drawn);
        Assert.Equal(1, Count(drawn, "^loop "));
        Assert.Equal(1, (await DrawnBoxesAsync())[0]);
    }

    // L5: "Copy current PlantUML source" copies the filtered source, so it has no empty loop either
    [Fact]
    public async Task The_current_source_copied_has_no_empty_loop()
    {
        await OpenAsync("BlocksCopiedSource.html", MilkingSource());
        await ToggleAsync("databases");
        await Page.EvaluateAsync("() => navigator.clipboard.writeText('nothing copied yet')");
        await Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync("""
            el => {
                const r = el.getBoundingClientRect();
                el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: r.left + 8, clientY: r.top + 8 }));
            }
            """);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });
        var parent = menu.Locator(".submenu-parent", new() { HasTextString = "Copy PlantUML source" });
        if (await parent.CountAsync() > 0)
        {
            await parent.First.HoverAsync();
            await parent.First.Locator(".submenu").GetByText("Copy current PlantUML source", new() { Exact = true }).First.ClickAsync();
        }
        else
            await menu.Locator(":scope > div", new() { HasTextString = "Copy PlantUML source" }).First.ClickAsync();

        var copied = await Page.EvaluateAsync<string>("""
            async () => {
                let text = '';
                for (let k = 0; k < 50 && !text.startsWith('@startuml'); k++) {
                    text = await navigator.clipboard.readText();
                    if (!text.startsWith('@startuml')) await new Promise(res => setTimeout(res, 200));
                }
                return text;
            }
            """);
        Assert.StartsWith("@startuml", copied);
        Assert.DoesNotContain("milkDb", copied);
        Assert.Equal(1, Count(copied, "^loop "));
        Assert.Equal(Count(copied, "^(loop|partition)\\b"), Count(copied, "^end$"));
    }

    // E5: a service called Group is aliased `group`, and its calls are messages: the splitter closed and re-opened
    // `group -> ...` as a block at every cut, adding an `end` to one part and the call again to the next
    [Fact]
    public async Task A_participant_called_Group_is_a_message()
    {
        await OpenAsync("BlocksGroupSplit.html", MilkingSource());
        var split = await Page.EvaluateAsync<string>("""
            () => {
                var body = [];
                for (var i = 0; i < 12; i++) {
                    body.push('caller -[#438DD5]> group: GET: /orders/' + i);
                    body.push('group -[#438DD5]-> caller: 200 for ' + i);
                }
                var source = ['@startuml', '!pragma teoz true', 'autonumber 1', 'participant "Caller" as caller',
                    'participant "Group" as group'].concat(body, ['@enduml']).join('\n');
                var frags = window._splitDiagramSource(source, 300);
                if (frags.length < 2) return 'NOT_SPLIT';
                var errors = [], seen = {};
                frags.forEach(function (f, k) {
                    f.split('\n').forEach(function (l) {
                        if (l.trim() === 'end') errors.push('part ' + (k + 1) + ' has an end');
                        var m = /^group -\[#438DD5\]-> caller: (200 for \d+)$/.exec(l.trim());
                        if (m) { if (seen[m[1]]) errors.push('part ' + (k + 1) + ' repeats ' + m[1]); seen[m[1]] = true; }
                    });
                });
                return errors.length ? errors.join('; ') : 'OK';
            }
            """);
        Assert.Equal("OK", split);

        // the Setup filter: a setup holding a call back from `group` was read as never closing, and left in
        var source = Emit(Call("Test", "Group", "PUT", "http://group.internal/herd", 0),
            ActionStart(40), Call("Test", "CowService", "POST", "http://cows.internal/milk", 50));
        Assert.Contains(Lines(source), l => l.StartsWith("group -", StringComparison.Ordinal));
        await OpenAsync("BlocksGroupSetup.html", source);
        await ToggleAsync("setup");
        var hidden = await DrawnSourceAsync();
        Assert.Equal(0, Count(hidden, "^partition\\b"));
        Assert.DoesNotContain("PUT", hidden);
        Assert.Contains("POST", hidden);
    }

    // E13: a style block's `group {` line in a part cut at a forced boundary (a note too long for one diagram) opened no
    // block, where it had the part closed with an `end` and the next part opened with `group {`
    [Fact]
    public async Task A_style_block_with_group_in_a_part_cut_at_a_forced_boundary_opens_no_block()
    {
        await OpenAsync("BlocksStyleSplit.html", MilkingSource());
        var split = await Page.EvaluateAsync<string>("""
            () => {
                var note = [];
                for (var i = 0; i < 400; i++) note.push('"line' + i + '": "' + 'x'.repeat(60) + '",');
                var source = ['@startuml', '!pragma teoz true', 'participant "Caller" as caller', 'participant "Api" as api',
                    '<style>', 'group {', '  LineColor #888888', '}', '</style>',
                    'caller -[#438DD5]> api: POST: /big', 'note left'].concat(note, ['end note',
                    'api -[#438DD5]-> caller: 200', '@enduml']).join('\n');
                var frags = window._splitWithChunkedNotes(source);
                if (frags.length < 2) return 'NOT_SPLIT';
                var errors = [];
                frags.forEach(function (f, k) {
                    var lines = f.split('\n').map(function (l) { return l.trim(); });
                    var inNote = false;
                    lines.forEach(function (t) {
                        if (inNote) { if (t === 'end note') inNote = false; return; }
                        if (/^note (left|right)/.test(t)) { inNote = true; return; }
                        if (t === 'end') errors.push('part ' + (k + 1) + ' has an end');
                    });
                    if (k > 0 && lines.indexOf('group {') >= 0 && lines.indexOf('<style>') < 0) errors.push('part ' + (k + 1) + ' opens group {');
                });
                return errors.length ? errors.join('; ') : 'OK';
            }
            """);
        Assert.Equal("OK", split);
    }

    // E9: a kept note's lines are never read as statements: one reading like a database's call, its activation or a
    // database's declaration was deleted from the note
    [Fact]
    public async Task A_note_line_naming_a_database_survives_the_databases_filter()
    {
        await OpenAsync("BlocksNoteNamesDatabase.html", Written("""
            api -> worker: go
            note right
            milkDb -> api: cached rows
            activate milkDb
            database "Archive" as archive
            end note
            api -> milkDb: SELECT
            """));

        await ToggleAsync("databases");
        var hidden = await DrawnSourceAsync();
        Assert.DoesNotContain("api -> milkDb: SELECT", Lines(hidden));
        foreach (var kept in new[] { "milkDb -> api: cached rows", "activate milkDb", "database \"Archive\" as archive" })
            Assert.Contains(kept, Lines(hidden));
    }

    // E14: an assertion's tooltip names the line of the assertion drawn, read from the source the picture was drawn from:
    // with the setup's assertion hidden, the action's took the setup's line
    [Fact]
    public async Task An_assertions_tooltip_names_its_own_line_after_the_setup_is_hidden()
    {
        var source = Emit(Marker(Assertion("the herd is three cows", 12), DiagramMarkerKind.Assertion, 0),
            Call("Test", "CowService", "PUT", "http://cows.internal/herd", 5),
            ActionStart(20),
            Call("Test", "CowService", "POST", "http://cows.internal/milk", 30),
            Marker(Assertion("the milk is collected", 20), DiagramMarkerKind.Assertion, 40));
        await OpenAsync("BlocksAssertionTooltip.html", source);

        await ToggleAsync("setup");
        await Page.WaitForFunctionAsync(
            "() => Array.from(document.querySelectorAll('[data-diagram-type=\"plantuml\"] svg title')).some(t => / L:\\d+/.test(t.textContent))",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        var titles = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.querySelectorAll('[data-diagram-type=\"plantuml\"] svg title')).map(t => t.textContent).filter(t => / L:\\d+/.test(t))");
        Assert.Equal(["Dairy.cs L:20"], titles);
    }
}
