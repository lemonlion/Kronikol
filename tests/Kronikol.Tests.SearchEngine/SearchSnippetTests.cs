using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;

namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// Where the deep search matched (plans/SEARCH_PLAN.md 4.6.2, 4.6.3 and 4.6.5): the worker records each piece of a
/// scenario's text, reports the kinds a match hit, and answers the lines of the results asked about, naming the place of
/// each. The worker is built as the page builds it, from the functions <c>buildWorker</c> lists and nothing else, and is
/// driven by its own messages: <c>init</c>, <c>docmap</c>, <c>query</c> and <c>snippets</c>. Its index has one bucket
/// whose row holds every scenario, so every scenario is a candidate and the verify pass decides; the streams it gunzips
/// pass their bytes through, so each value is posted as base64 of its text.
/// </summary>
public sealed class SearchSnippetTests : IDisposable
{
    private readonly Engine _engine = new();

    // A sequence diagram as the generator writes one: the header, two participants, the Setup partition, an arrow with
    // its request note, a second arrow with a note on the right, and an assertion's hnote block. Lines from 0.
    private const string Diagram =
        "@startuml\n" +                                     // 0
        "!pragma teoz true\n" +                             // 1
        "skinparam wrapWidth 300\n" +                       // 2
        "participant \"Test\" as T\n" +                     // 3
        "participant \"Kitchen\" as K\n" +                  // 4
        "partition Setup\n" +                               // 5
        "T -[#1f77b4]> K: POST: /api/Flour/order\n" +       // 6
        "note left\n" +                                     // 7
        "<color:#686868>[Content-Type=application/json]\n" + // 8
        "{\n" +                                             // 9
        "  \"Name\": \"Plain FLOUR\",\n" +                  // 10
        "  \"grams\": 200\n" +                              // 11
        "}\n" +                                             // 12
        "end note\n" +                                      // 13
        "end\n" +                                           // 14
        "T -> K: GET: /api/menu\n" +                        // 15
        "note right\n" +                                    // 16
        "Menu with flour\n" +                               // 17
        "end note\n" +                                      // 18
        "hnote across <<assertionNote>> #lightgreen\n" +    // 19
        "expected Flour to be fresh\n" +                    // 20
        "end note\n" +                                      // 21
        "@enduml";                                          // 22

    private const string Failure = "Error: Expected FLOUR\n   at Kitchen.Bake() in Kitchen.cs:line 12";

    private const string Flame = "{\"s\":[\"Kitchen\"],\"f\":[[0,\"Toaster.Heat\",0,1,0,2],[0,\"Flour.Sift\",1,2,1,3]],\"m\":[[5,\"GET: /marker\"]]}";

    public SearchSnippetTests()
    {
        _engine.Execute(WorkerSource());
        _engine.Execute(StreamStubs);
    }

    // ---- W4: a line, cut to 160 around the hit ----

    [Fact]
    public void A_line_is_the_piece_s_line_cut_to_160_around_the_hit()
    {
        // a short line is the whole line, and only the line holding the hit
        var shortCut = LineAround("first line\nsecond flour line\nthird", "flour");
        Assert.Equal("second flour line", shortCut.Text);
        Assert.Equal(0, shortCut.Lead);

        // a long line is cut to 160 around the hit at word boundaries, with "…" where it is cut
        var words = string.Join(" ", Enumerable.Range(0, 60).Select(i => "word" + i));
        var text = words + " flour " + words;
        var cut = LineAround(text, "flour");
        Assert.StartsWith("…", cut.Text);
        Assert.EndsWith("…", cut.Text);
        Assert.Contains("flour", cut.Text);
        Assert.True(cut.Text.Length <= 162, $"{cut.Text.Length} characters: {cut.Text}");
        Assert.Equal(' ', text[cut.From - 1]);
        Assert.Equal(' ', text[cut.To]);
        Assert.Equal(1, cut.Lead);
        Assert.Equal(text.Substring(cut.From, cut.To - cut.From), cut.Text[1..^1]);
        Assert.True(cut.Text.Length >= 120, $"the cut keeps most of its room: {cut.Text}");
    }

    [Fact]
    public void A_hit_at_either_end_of_a_long_line_is_cut_on_the_other_side_only()
    {
        var words = string.Join(" ", Enumerable.Range(0, 60).Select(i => "word" + i));
        var atStart = LineAround("flour " + words, "flour");
        Assert.StartsWith("flour ", atStart.Text);
        Assert.EndsWith("…", atStart.Text);
        Assert.Equal(0, atStart.Lead);

        var atEnd = LineAround(words + " flour", "flour");
        Assert.StartsWith("…", atEnd.Text);
        Assert.EndsWith(" flour", atEnd.Text);
    }

    [Fact]
    public void A_line_with_no_spaces_is_cut_inside_its_word_and_its_own_spaces_are_left_out()
    {
        var blob = new string('x', 200) + "flour" + new string('y', 200);
        var cut = LineAround(blob, "flour");
        Assert.Equal(162, cut.Text.Length);
        Assert.Contains("flour", cut.Text);

        Assert.Equal("indented flour", LineAround("   indented flour   \nnext", "flour").Text);
    }

    [Fact]
    public void A_hit_longer_than_the_line_s_room_shows_its_start()
    {
        var hit = string.Concat(Enumerable.Repeat("flour", 40));
        var cut = LineAround("before " + hit + " after", hit);
        Assert.Equal("…" + hit[..160] + "…", cut.Text);
    }

    // ---- W5: the kinds a match hit ----

    [Fact]
    public void The_worker_reports_the_kinds_a_match_hit()
    {
        Start(Breakfast());

        var flour = Query(1, "flour");
        Assert.Equal([0, 1, 2], flour.Matches);
        Assert.Equal(["flour"], flour.Terms);
        // the name and the diagram, the failure's text, the whole-test flow
        Assert.Equal(["ng"], flour.Kinds[0]);
        Assert.Equal(["e"], flour.Kinds[1]);
        Assert.Equal(["w"], flour.Kinds[2]);

        // each positive term its own kinds, in the order the scenario's text has them
        var both = Query(2, "pancakes && flour");
        Assert.Equal([0], both.Matches);
        Assert.Equal(["pancakes", "flour"], both.Terms);
        Assert.Equal(["n", "ng"], both.Kinds[0]);

        // a word of the steps and of a call's path; a term under !! is no positive term
        var order = Query(3, "order && !! syrup");
        Assert.Equal([0], order.Matches);
        Assert.Equal(["order"], order.Terms);
        Assert.Equal(["sg"], order.Kinds[0]);
    }

    [Fact]
    public void Every_matches_batch_carries_the_kinds_of_its_matches()
    {
        var items = Enumerable.Range(0, 205).Select(i => Item($"breakfast item {i} with flour", "f0 n10")).ToArray();
        Start(items);
        Send("{ type: 'query', gen: 1, input: 'flour' }");
        var results = Posted("result");
        Assert.Equal(3, results.Count);
        foreach (var r in results)
        {
            var matches = r.GetProperty("matches").GetArrayLength();
            Assert.Equal(matches, r.GetProperty("kinds").GetArrayLength());
            Assert.All(r.GetProperty("kinds").EnumerateArray(), k => Assert.Equal("[\"n\"]", k.GetRawText()));
        }
        Assert.Equal([100, 200, 205], results.Select(r => r.GetProperty("matches").GetArrayLength()));
    }

    // ---- W6: snippets for the items asked ----

    [Fact]
    public void The_worker_answers_snippets_for_the_items_asked_and_drops_stale_ones()
    {
        Start(Breakfast());
        Query(1, "flour");

        var answer = Snippets(1, 7, "flour", [0, 2], 2);
        Assert.Equal([0, 2], answer.EnumerateArray().Select(a => a.GetProperty("item").GetInt32()));

        var pancakes = answer[0];
        Assert.Equal(4, pancakes.GetProperty("count").GetInt32());
        Assert.Equal(4, pancakes.GetProperty("hits").GetInt32());
        var lines = pancakes.GetProperty("lines");
        Assert.Equal(2, lines.GetArrayLength());
        AssertLine(lines[0], "g", "puml-0", "l6", "arrow", 6, "T -[#1f77b4]> K: POST: /api/Flour/order", [28, 33]);
        AssertLine(lines[1], "g", "puml-0", "n0", "note", 10, "\"Name\": \"Plain FLOUR\",", [15, 20]);

        var toast = answer[1];
        Assert.Equal(1, toast.GetProperty("count").GetInt32());
        AssertLine(toast.GetProperty("lines")[0], "w", null, "f2", "flow", 2, "Flour.Sift", [0, 5]);

        // a request a newer query has overtaken is dropped, whether it arrived stale or went stale while it was read
        Clear();
        Send("""
            { type: 'query', gen: 5, input: 'flour' }
            { type: 'snippets', gen: 5, id: 1, input: 'flour', items: [0], max: 3 }
            { type: 'query', gen: 6, input: 'kitchen' }
            { type: 'snippets', gen: 5, id: 2, input: 'flour', items: [0], max: 3 }
            { type: 'snippets', gen: 6, id: 3, input: 'kitchen', items: [0, 1], max: 3 }
            """);
        var replies = Posted("snippets");
        Assert.Equal([1, 2, 3], replies.Select(r => r.GetProperty("id").GetInt32()).Order());
        Assert.True(replies.Single(r => r.GetProperty("id").GetInt32() == 1).GetProperty("stale").GetBoolean());
        Assert.True(replies.Single(r => r.GetProperty("id").GetInt32() == 2).GetProperty("stale").GetBoolean());
        var fresh = replies.Single(r => r.GetProperty("id").GetInt32() == 3);
        Assert.False(fresh.TryGetProperty("stale", out _));
        Assert.Equal([0, 1], fresh.GetProperty("answers").EnumerateArray().Select(a => a.GetProperty("item").GetInt32()));
        // the query it overtook posts nothing more
        Assert.DoesNotContain(Posted("result"), r => r.GetProperty("gen").GetInt32() == 5);
    }

    [Fact]
    public void A_scenario_whose_text_holds_no_hit_answers_no_lines()
    {
        Start(Breakfast());
        Query(1, "flour");
        var answer = Snippets(1, 1, "flour", [3, 1], 5);
        Assert.Equal(0, answer[0].GetProperty("count").GetInt32());
        Assert.Equal(0, answer[0].GetProperty("lines").GetArrayLength());
        // the failure's line as written, its words marked
        AssertLine(answer[1].GetProperty("lines")[0], "e", null, "e", "error", 0, "Error: Expected FLOUR", [16, 21]);
    }

    [Fact]
    public void Every_hit_on_a_line_is_marked_and_the_line_counts_once()
    {
        Start([Item("breakfast porridge", "f0 n10", diagram: "@startuml\nA -> B: oats and more oats\n@enduml")]);
        Query(1, "oats");
        var answer = Snippets(1, 1, "oats", [0], 5)[0];
        Assert.Equal(1, answer.GetProperty("count").GetInt32());
        Assert.Equal(2, answer.GetProperty("hits").GetInt32());
        var marks = answer.GetProperty("lines")[0].GetProperty("marks");
        Assert.Equal("[[8,12],[22,26]]", marks.GetRawText());
    }

    // ---- H27: a match's offset names its place ----

    [Fact]
    public void A_match_s_offset_names_its_note_hnote_arrow_or_partition()
    {
        Start(Breakfast());

        Query(1, "flour");
        var flour = Snippets(1, 1, "flour", [0], 10)[0].GetProperty("lines");
        Assert.Equal(["l6 arrow 6", "n0 note 10", "n1 note 17", "l19 hnote 20"],
            flour.EnumerateArray().Select(l => $"{l.GetProperty("place").GetString()} {l.GetProperty("what").GetString()} {l.GetProperty("row").GetInt32()}"));
        Assert.Equal(["T -[#1f77b4]> K: POST: /api/Flour/order", "\"Name\": \"Plain FLOUR\",", "Menu with flour", "expected Flour to be fresh"],
            flour.EnumerateArray().Select(l => l.GetProperty("text").GetString()));

        Query(2, "setup");
        AssertLine(Snippets(2, 2, "setup", [0], 10)[0].GetProperty("lines")[0], "g", "puml-0", "l5", "partition", 5, "partition Setup", [10, 15]);

        Query(3, "kitchen");
        var kitchen = Snippets(3, 3, "kitchen", [0], 10)[0].GetProperty("lines");
        AssertLine(kitchen[0], "g", "puml-0", "l4", "participant", 4, "participant \"Kitchen\" as K", [13, 20]);
    }

    [Fact]
    public void A_note_s_place_counts_its_hits_and_a_line_outside_every_statement_names_none()
    {
        var source = "@startuml\nskinparam ArrowColor #888\nnote left\noats\nend note\nnote right\noats then oats\nend note\n@enduml";
        Start([Item("breakfast porridge", "f0 n10", diagram: source)]);
        Query(1, "oats");
        var lines = Snippets(1, 1, "oats", [0], 10)[0].GetProperty("lines");
        Assert.Equal(["n0 1", "n1 1"], lines.EnumerateArray().Select(l => $"{l.GetProperty("place").GetString()} {l.GetProperty("n").GetInt32()}"));

        Query(2, "arrowcolor");
        var directive = Snippets(2, 2, "arrowcolor", [0], 10)[0].GetProperty("lines")[0];
        Assert.Equal(JsonValueKind.Null, directive.GetProperty("place").ValueKind);
        Assert.Equal("skinparam ArrowColor #888", directive.GetProperty("text").GetString());
    }

    [Fact]
    public void The_statements_of_a_source_are_its_notes_by_the_page_s_rule_and_its_one_line_statements()
    {
        _engine.SetValue("__src", Diagram);
        var statements = JsonDocument.Parse(_engine.Evaluate("JSON.stringify(kronDiagramStatements(__src))").AsString()).RootElement;
        Assert.Equal(
            ["participant 3-3", "participant 4-4", "partition 5-5", "arrow 6-6", "note 7-13 #0", "arrow 15-15", "note 16-18 #1", "hnote 19-21"],
            statements.EnumerateArray().Select(s =>
                $"{s.GetProperty("what").GetString()} {s.GetProperty("line").GetInt32()}-{s.GetProperty("last").GetInt32()}"
                + (s.GetProperty("note").GetInt32() >= 0 ? $" #{s.GetProperty("note").GetInt32()}" : "")));
    }

    [Fact]
    public void The_note_blocks_are_read_by_one_rule_the_page_and_the_worker_share()
    {
        _engine.SetValue("__src", "a -> b: x\nnote<<eventNote>> right\nevent\nend note\nhnote across #lightyellow : Row 1\n  note left\nbody\n   end note  \nnote right\nnever closed");
        var blocks = _engine.Evaluate("JSON.stringify(kronParseNoteBlocks(__src).map(function(n) { return [n.start, n.end, n.contentLines.length]; }))").AsString();
        Assert.Equal("[[1,3,1],[5,7,1],[8,10,1]]", blocks);
        Assert.Equal("[]", _engine.Evaluate("JSON.stringify(kronParseNoteBlocks(''))").AsString());

        // a line that holds the words is not an end or a header unless it is one: a block runs to a line that is only its end
        _engine.SetValue("__src", "note left\nthe end note is not here\nend note: neither\nend note\nNote right\nnotes left\n  note right  \nbody\nend note");
        blocks = _engine.Evaluate("JSON.stringify(kronParseNoteBlocks(__src).map(function(n) { return [n.start, n.end, n.contentLines.length]; }))").AsString();
        Assert.Equal("[[0,3,2],[6,8,1]]", blocks);
    }

    [Fact]
    public void A_batch_goes_once_however_many_candidates_follow_it()
    {
        // every other scenario holds the word: the hundredth match is followed by one that does not match
        var items = Enumerable.Range(0, 300).Select(i => Item(i % 2 == 0 ? $"breakfast item {i} with flour" : $"breakfast item {i}", "f0 n10")).ToArray();
        Start(items);
        Send("{ type: 'query', gen: 1, input: 'flour' }");
        var results = Posted("result");
        Assert.Equal([100, 150], results.Select(r => r.GetProperty("matches").GetArrayLength()));
        Assert.Equal([false, true], results.Select(r => r.GetProperty("done").GetBoolean()));
    }

    [Fact]
    public void The_kinds_of_the_data_search_follow_its_text_through_the_normaliser()
    {
        // the offsets of data-search-kinds are the attribute's; doubled spaces and a tag shorten the text the worker reads
        const string name = "pancakes  <b>with</b> flour", steps = "given  an  order";
        var text = "breakfast  " + name + "  " + steps;
        Start([Item(text, $"f0 n{text.IndexOf(name, StringComparison.Ordinal)} s{text.IndexOf(steps, StringComparison.Ordinal)}")]);
        Assert.Equal(["s"], Query(1, "order").Kinds[0]);
        Assert.Equal(["n"], Query(2, "with").Kinds[0]);
        Assert.Equal(["f", "n"], Query(3, "breakfast && pancakes").Kinds[0]);
    }

    [Fact]
    public void A_scenario_written_without_data_search_kinds_reads_as_its_name()
    {
        // as the page's own reading of the attribute has it (report-search-results-script.js)
        Start([Item("breakfast porridge", "")]);
        Assert.Equal(["n"], Query(1, "porridge").Kinds[0]);
    }

    [Fact]
    public void Each_diagram_of_a_scenario_is_named_by_its_element()
    {
        // an outline's rows each have a diagram of their own, and the whole-test flow's activity diagram is held in the page
        const string row = "@startuml\nA -> B: GET: /api/x\nnote left\n{ \"flour\": {0} }\nend note\n@enduml";
        Start([Item("breakfast outline", "f0 n10",
            diagrams: [("row-0-diagram", row.Replace("{0}", "100")), ("row-1-diagram", row.Replace("{0}", "200"))],
            activities: [("iflow-puml-whole-7", "@startuml\n|Kitchen|\n:Sift flour (12ms);\n@enduml")])]);
        Query(1, "flour");
        var lines = Snippets(1, 1, "flour", [0], 10)[0].GetProperty("lines");
        Assert.Equal(["puml 0 row-0-diagram n0 note", "puml 1 row-1-diagram n0 note", "z 0 iflow-puml-whole-7 l2 action"],
            lines.EnumerateArray().Select(l =>
                $"{l.GetProperty("sort").GetString()} {l.GetProperty("index").GetInt32()} {l.GetProperty("ref").GetString()} {l.GetProperty("place").GetString()} {l.GetProperty("what").GetString()}"));
        Assert.Equal(["{ \"flour\": 100 }", "{ \"flour\": 200 }", ":Sift flour (12ms);"], lines.EnumerateArray().Select(l => l.GetProperty("text").GetString()));
    }

    [Fact]
    public void A_Raw_PlantUML_section_s_line_names_the_section()
    {
        Start([Item("breakfast server drawn", "f0 n10", rawPlantUml: "@startuml\nA -> B: POST: /api/Flour\n@enduml")]);
        Query(1, "flour");
        AssertLine(Snippets(1, 1, "flour", [0], 3)[0].GetProperty("lines")[0], "g", null, "x", "source", 1, "A -> B: POST: /api/Flour", [19, 24]);
    }

    [Fact]
    public void A_line_names_which_hit_of_its_place_it_shows_first()
    {
        Start([Item("breakfast porridge", "f0 n10", diagram: "@startuml\nnote right\noats\nthen\nmore oats\nend note\n@enduml")]);
        Query(1, "oats");
        var lines = Snippets(1, 1, "oats", [0], 10)[0].GetProperty("lines");
        Assert.Equal(["n0 1 2", "n0 2 4"], lines.EnumerateArray().Select(l =>
            $"{l.GetProperty("place").GetString()} {l.GetProperty("n").GetInt32()} {l.GetProperty("row").GetInt32()}"));
    }

    [Fact]
    public void A_long_line_s_marks_follow_its_cut()
    {
        var words = string.Join(" ", Enumerable.Range(0, 40).Select(i => "Word" + i));
        Start([Item("breakfast porridge", "f0 n10", diagram: $"@startuml\nnote left\n{words} Flour {words}\nend note\n@enduml")]);
        Query(1, "flour");
        var line = Snippets(1, 1, "flour", [0], 3)[0].GetProperty("lines")[0];
        var text = line.GetProperty("text").GetString()!;
        Assert.StartsWith("…Word", text);
        Assert.EndsWith("…", text);
        var mark = line.GetProperty("marks")[0];
        Assert.Equal("Flour", text[mark[0].GetInt32()..mark[1].GetInt32()]);
    }

    // ---- the page's side: _kronDeepSnippets and _kronDeepKinds ----

    [Fact]
    public void The_page_asks_for_snippets_and_gets_them_with_their_elements()
    {
        using var page = new Engine();
        page.Execute(PageStub);
        page.Execute(StreamStubs);
        foreach (var script in new[] { "advanced-search.js", "report-scenario-feature-map-helper.js", "report-search-function.js", "report-search-index.js" })
            page.Execute(LoadEmbedded(script));
        page.SetValue("__diagram", Diagram);
        page.SetValue("__failure", Failure);
        page.SetValue("__index", Convert.ToBase64String(AllOnesIndex(2)));
        page.Execute("__buildPage(); window._kronDeepQuery('flour');");
        Assert.Equal("ready", page.Evaluate("window.__kronikolSearch.indexState").AsString());
        Assert.Equal("{\"terms\":[\"flour\"],\"kinds\":[\"ng\"]}", page.Evaluate("JSON.stringify(window._kronDeepKinds(0))").AsString());
        Assert.Equal("{\"terms\":[\"flour\"],\"kinds\":[\"e\"]}", page.Evaluate("JSON.stringify(window._kronDeepKinds(__scenarios[1]))").AsString());

        // the answers in the order asked, a scenario asked by its element, each line with the element it came from
        page.Execute("var __answer; window._kronDeepSnippets([__scenarios[1], 0], 2).then(function (a) { __answer = a; });");
        Assert.Equal("[1,0]", page.Evaluate("JSON.stringify(__answer.map(function (a) { return a.item; }))").AsString());
        Assert.True(page.Evaluate("__answer[0].el === __scenarios[1] && __answer[1].el === __scenarios[0]").AsBoolean());
        Assert.True(page.Evaluate("__answer[0].lines[0].el === __failurePre && __answer[1].lines[0].el === __diagramEl").AsBoolean());
        Assert.Equal("l6 n0", page.Evaluate("__answer[1].lines.map(function (l) { return l.place; }).join(' ')").AsString());
        Assert.Equal(4, (int)page.Evaluate("__answer[1].count").AsNumber());

        // a request a newer query overtakes holds null at once, while the worker has not read either, and so does one with
        // no deep search in effect
        page.Execute("var __stale = 'unset'; __hold = true; window._kronDeepSnippets([0], 2).then(function (a) { __stale = a; }); window._kronDeepQuery('kitchen');");
        Assert.True(page.Evaluate("__stale === null").AsBoolean());
        page.Execute("__release();");
        Assert.Equal("{\"terms\":[\"kitchen\"],\"kinds\":[\"g\"]}", page.Evaluate("JSON.stringify(window._kronDeepKinds(0))").AsString());
        page.Execute("var __none = 'unset'; window._kronDeepReset(); window._kronDeepSnippets([0], 2).then(function (a) { __none = a; });");
        Assert.True(page.Evaluate("__none === null").AsBoolean());
        Assert.True(page.Evaluate("window._kronDeepKinds(0) === null").AsBoolean());
    }

    // A page of two scenarios as the generator writes them, with what the deep search reads of it: the index and the
    // diagram sources in their scripts, the diagram element, the failure's pre, and a worker that runs the shipped
    // worker's code in this engine, its messages going each way as the page's do, a turn later and as a copy
    private const string PageStub = """
        function __el(attrs, classes, kids) {
            var el = {
                _a: attrs || {}, _c: classes || [], _q: kids || {}, style: {}, textContent: '', parentElement: null,
                getAttribute: function (n) { return Object.prototype.hasOwnProperty.call(this._a, n) ? this._a[n] : null; },
                hasAttribute: function (n) { return Object.prototype.hasOwnProperty.call(this._a, n); },
                setAttribute: function (n, v) { this._a[n] = String(v); },
                removeAttribute: function (n) { delete this._a[n]; },
                getElementsByClassName: function (c) { return (this._kids || []).filter(function (k) { return k._c.indexOf(c) >= 0; }); },
                querySelectorAll: function (s) { return this._q[s] || []; },
                querySelector: function () { return null; }
            };
            el.id = el._a.id || '';
            el.classList = { contains: function (c) { return el._c.indexOf(c) >= 0; } };
            return el;
        }
        var __features = [], __scenarios = [], __byId = {}, __events = [], __diagramEl, __failurePre;
        function __buildPage() {
            __diagramEl = __el({ id: 'puml-0' }, ['plantuml-browser']);
            __failurePre = __el({}, []);
            __failurePre.textContent = __failure;
            var pancakes = __el({ 'data-search': 'breakfast pancakes with flour given an order get: /api/menu', 'data-search-kinds': 'f0 n10 s30 g45', 'data-status': 'Passed' }, ['scenario'],
                { '.plantuml-browser[id], .plantuml-inline-svg[id]': [__diagramEl] });
            var waffles = __el({ 'data-search': 'breakfast waffles', 'data-search-kinds': 'f0 n10', 'data-status': 'Failed' }, ['scenario'],
                { '.failure-result pre': [__failurePre] });
            var feature = __el({}, ['feature']);
            feature._kids = [pancakes, waffles];
            __features.push(feature);
            __scenarios.push(pancakes, waffles);
            __byId['sid-0'] = pancakes; __byId['sid-1'] = waffles; __byId['puml-0'] = __diagramEl;
            __byId['kron-search-index'] = { textContent: JSON.stringify(__index) };
            var puml = {}; puml['puml-0'] = btoa(__diagram);
            __byId['puml-data'] = { textContent: JSON.stringify(puml) };
        }
        function btoa(s) {
            var abc = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/', out = '';
            for (var i = 0; i < s.length; i += 3) {
                var n = (s.charCodeAt(i) << 16) | ((s.charCodeAt(i + 1) || 0) << 8) | (s.charCodeAt(i + 2) || 0);
                out += abc.charAt(n >> 18) + abc.charAt((n >> 12) & 63) + (i + 1 < s.length ? abc.charAt((n >> 6) & 63) : '=') + (i + 2 < s.length ? abc.charAt(n & 63) : '=');
            }
            return out;
        }
        var document = {
            getElementsByClassName: function (c) { return c === 'feature' ? __features : c === 'scenario' ? __scenarios : []; },
            getElementById: function (id) { return __byId[id] || null; },
            querySelector: function () { return null; },
            querySelectorAll: function () { return []; },
            addEventListener: function () { },
            dispatchEvent: function (e) { __events.push(e); },
            createElement: function () { return __el(); },
            createTextNode: function () { return {}; }
        };
        var window = {};
        function CustomEvent(type, init) { this.type = type; this.detail = init && init.detail; }
        function Blob() { }
        var URL = { createObjectURL: function () { return 'blob:worker'; }, revokeObjectURL: function () { } };
        function update_url_hash() { }
        // __hold keeps what the page posts from the worker until __release(), as a worker busy reading would
        var __hold = false, __held = [], __release;
        function Worker() {
            var page = this;
            var inside = { postMessage: function (m) { var copy = JSON.parse(JSON.stringify(m)); Promise.resolve().then(function () { page.onmessage({ data: copy }); }); } };
            kronSearchWorkerMain(inside);
            function deliver(copy) { Promise.resolve().then(function () { inside.onmessage({ data: copy }); }); }
            this.postMessage = function (m) { var copy = JSON.parse(JSON.stringify(m)); if (__hold) __held.push(copy); else deliver(copy); };
            __release = function () { __hold = false; var held = __held; __held = []; held.forEach(deliver); };
        }
        """;

    // ---- the harness ----

    private sealed record Cut(string Text, int From, int To, int Lead);

    private Cut LineAround(string text, string hit)
    {
        _engine.SetValue("__text", text);
        _engine.SetValue("__hit", text.IndexOf(hit, StringComparison.Ordinal));
        _engine.SetValue("__length", hit.Length);
        var r = JsonDocument.Parse(_engine.Evaluate("JSON.stringify(kronLineAround(__text, __hit, __length, 160))").AsString()).RootElement;
        return new Cut(r.GetProperty("text").GetString()!, r.GetProperty("from").GetInt32(), r.GetProperty("to").GetInt32(), r.GetProperty("lead").GetInt32());
    }

    private static void AssertLine(JsonElement line, string kind, string? reference, string place, string what, int row, string text, int[] mark)
    {
        Assert.Equal(kind, line.GetProperty("kind").GetString());
        if (reference is null) Assert.Equal(JsonValueKind.Null, line.GetProperty("ref").ValueKind);
        else Assert.Equal(reference, line.GetProperty("ref").GetString());
        Assert.Equal(place, line.GetProperty("place").GetString());
        Assert.Equal(what, line.GetProperty("what").GetString());
        Assert.Equal(row, line.GetProperty("row").GetInt32());
        Assert.Equal(text, line.GetProperty("text").GetString());
        Assert.Equal(JsonSerializer.Serialize(new[] { mark }), line.GetProperty("marks").GetRawText());
    }

    // Four scenarios of one feature: Pancakes with flour (flour in its name, a call's path and two notes of its diagram,
    // order in its steps and the path), Waffles (flour only in its failure's text), Toast (flour only in its flow) and
    // Juice (no flour).
    private static object[] Breakfast() =>
    [
        Item("breakfast pancakes with flour given an order get: /api/menu", "f0 n10 s30 g45", diagram: Diagram),
        Item("breakfast waffles", "f0 n10", failure: Failure),
        Item("breakfast toast", "f0 n10", flame: Flame),
        Item("breakfast juice", "f0 n10")
    ];

    // A scenario as collectItemMeta posts it: its data-search and data-search-kinds, its diagrams in #puml-data (one
    // `diagram` is puml-0), its activity diagrams held in data-plantuml-z, a Raw PlantUML section's text, its failure's
    // text and its flow's flame data
    private static object Item(string searchText, string kinds, string? diagram = null, string? failure = null, string? flame = null,
        (string Id, string Source)[]? diagrams = null, (string Id, string Source)[]? activities = null, string? rawPlantUml = null)
    {
        var sources = diagrams ?? (diagram is null ? [] : [("puml-0", diagram)]);
        var raw = new List<(string Text, string Kind)>();
        if (rawPlantUml is not null) raw.Add((rawPlantUml, "x"));
        if (failure is not null) raw.Add((failure, "e"));
        return new
        {
            searchText,
            searchKinds = kinds,
            tags = Array.Empty<string>(),
            status = "Passed",
            verdicts = (string[]?)null,
            diagramIds = sources.Select(d => d.Id).ToArray(),
            sources = sources.ToDictionary(d => d.Id, d => B64(d.Source)),
            plantumlZ = (activities ?? []).Select(a => B64(a.Source)).ToArray(),
            plantumlIds = (activities ?? []).Select(a => a.Id).ToArray(),
            rawTexts = raw.Select(r => r.Text).ToArray(),
            rawKinds = raw.Select(r => r.Kind).ToArray(),
            flameZ = flame is null ? [] : new[] { B64(flame) },
            flameIds = flame is null ? [] : new string?[] { null }
        };
    }

    // init (the items, their diagram sources as #puml-data holds them, an index whose one row holds every scenario) and
    // the doc map, one doc a scenario
    private void Start(object[] items)
    {
        var sources = new Dictionary<string, string>();
        foreach (var item in items)
            foreach (var (id, source) in (Dictionary<string, string>)item.GetType().GetProperty("sources")!.GetValue(item)!)
                sources[id] = source;
        _engine.SetValue("__items", JsonSerializer.Serialize(items));
        _engine.SetValue("__puml", JsonSerializer.Serialize(sources));
        _engine.SetValue("__index", Convert.ToBase64String(AllOnesIndex(items.Length)));
        _engine.Execute("__self.onmessage({ data: { type: 'init', items: JSON.parse(__items), pumlDataJson: __puml, indexB64: __index, frames: null } });");
        Assert.Single(Posted("anchors"));
        _engine.Execute($"__self.onmessage({{ data: {{ type: 'docmap', docToItem: [{string.Join(",", Enumerable.Range(0, items.Length))}] }} }});");
    }

    private sealed record Result(int[] Matches, string[] Terms, string[][] Kinds);

    private Result Query(int gen, string input)
    {
        _engine.SetValue("__input", input);
        _engine.Execute($"__self.onmessage({{ data: {{ type: 'query', gen: {gen}, input: __input }} }});");
        var done = Posted("result").Last(r => r.GetProperty("gen").GetInt32() == gen && r.GetProperty("done").GetBoolean());
        var matches = done.GetProperty("matches").EnumerateArray().Select(m => m.GetInt32()).ToArray();
        var terms = done.TryGetProperty("terms", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToArray() : [];
        var kinds = done.TryGetProperty("kinds", out var k)
            ? k.EnumerateArray().Select(x => x.EnumerateArray().Select(y => y.GetString()!).ToArray()).ToArray()
            : [];
        return new Result(matches, terms, kinds);
    }

    private JsonElement Snippets(int gen, int id, string input, int[] items, int max)
    {
        _engine.SetValue("__input", input);
        _engine.Execute($"__self.onmessage({{ data: {{ type: 'snippets', gen: {gen}, id: {id}, input: __input, items: [{string.Join(",", items)}], max: {max} }} }});");
        var reply = Posted("snippets").Last(r => r.GetProperty("id").GetInt32() == id);
        return reply.GetProperty("answers");
    }

    // several messages, one a line, handed over in one turn: none is read until all are in
    private void Send(string messages)
    {
        var calls = messages.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => $"__self.onmessage({{ data: {m} }});");
        _engine.Execute(string.Join("\n", calls));
    }

    private void Clear() => _engine.Execute("__posted.length = 0;");

    private List<JsonElement> Posted(string type) =>
        JsonDocument.Parse(_engine.Evaluate("JSON.stringify(__posted)").AsString()).RootElement.EnumerateArray()
            .Where(m => m.GetProperty("type").GetString() == type).ToList();

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    // KSI1 v1 with one bucket, whose row is a bitset of every doc
    private static byte[] AllOnesIndex(int docs)
    {
        var bytes = new List<byte> { 0x4b, 0x53, 0x49, 0x31, 1 };
        bytes.AddRange(BitConverter.GetBytes(1));
        bytes.AddRange(BitConverter.GetBytes(docs));
        for (var d = 0; d < docs; d++)
        {
            var anchor = Encoding.UTF8.GetBytes("sid-" + d);
            bytes.AddRange(Varint(anchor.Length));
            bytes.AddRange(anchor);
        }
        var bitset = (docs + 7) >> 3;
        bytes.AddRange(Varint(1 + bitset));
        bytes.Add(1);
        for (var i = 0; i < bitset; i++) bytes.Add(0xff);
        return bytes.ToArray();
    }

    private static IEnumerable<byte> Varint(int value)
    {
        var v = (uint)value;
        while (v >= 0x80) { yield return (byte)(v | 0x80); v >>= 7; }
        yield return (byte)v;
    }

    // The worker as buildWorker makes it: each function its roster lists, from the shipped scripts, and the call that
    // starts it, on a self that keeps what it is posted
    private static string WorkerSource()
    {
        var sources = string.Join("\n", new[] { "advanced-search.js", "report-search-function.js", "report-search-index.js" }.Select(LoadEmbedded));
        var index = LoadEmbedded("report-search-index.js");
        var list = index[(index.IndexOf("var fns = [", StringComparison.Ordinal) + "var fns = [".Length)..];
        list = list[..list.IndexOf("];", StringComparison.Ordinal)];
        var roster = Regex.Matches(list, @"\b\w+\b").Select(m => m.Value).ToArray();
        var functions = roster.Select(name =>
        {
            var function = Regex.Match(sources, @"^function " + name + @"\(.*?^\}", RegexOptions.Singleline | RegexOptions.Multiline);
            Assert.True(function.Success, $"{name} is not a function of the shipped scripts");
            return function.Value;
        });
        return string.Join("\n\n", functions)
            + "\n\nvar __posted = [];\nvar __self = { postMessage: function(m) { __posted.push(JSON.parse(JSON.stringify(m))); } };\nkronSearchWorkerMain(__self);\n";
    }

    // What the worker reads its blobs with: base64, a stream of the bytes, and a "gunzip" that passes them through
    private const string StreamStubs = """
        function atob(s) {
            var abc = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/', out = '', bits = 0, n = 0;
            for (var i = 0; i < s.length; i++) {
                var v = abc.indexOf(s.charAt(i));
                if (v < 0) continue;
                bits = (bits << 6) | v; n += 6;
                if (n >= 8) { n -= 8; out += String.fromCharCode((bits >> n) & 255); }
            }
            return out;
        }
        function ReadableStream(source) {
            var self = this;
            this.chunks = [];
            source.start({ enqueue: function(b) { self.chunks.push(b); }, close: function() { } });
        }
        ReadableStream.prototype.pipeThrough = function() { return this; };
        function DecompressionStream() { }
        function Response(stream) { this.bytes = stream.chunks[0]; }
        Response.prototype.arrayBuffer = function() { return Promise.resolve(this.bytes.buffer); };
        Response.prototype.text = function() {
            var b = this.bytes, out = '', i = 0;
            while (i < b.length) {
                var c = b[i++];
                if (c < 128) out += String.fromCharCode(c);
                else if (c < 224) out += String.fromCharCode(((c & 31) << 6) | (b[i++] & 63));
                else if (c < 240) out += String.fromCharCode(((c & 15) << 12) | ((b[i++] & 63) << 6) | (b[i++] & 63));
                else {
                    var cp = (((c & 7) << 18) | ((b[i++] & 63) << 12) | ((b[i++] & 63) << 6) | (b[i++] & 63)) - 0x10000;
                    out += String.fromCharCode(0xD800 + (cp >> 10), 0xDC00 + (cp & 0x3FF));
                }
            }
            return Promise.resolve(out);
        };
        """;

    private static string LoadEmbedded(string name)
    {
        var assembly = typeof(Kronikol.Reports.ReportGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource {name} not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() => _engine.Dispose();
}
