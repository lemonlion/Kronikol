using Kronikol.Ingestion;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Kronikol.Tracking.Tabular;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// The step-delimiter bar's two emission forms, pinned against what the shipped plantuml.js engine
/// actually renders (measured on the stock v1.2026.8beta1-0e4f452 build, teoz pragma):
/// <list type="bullet">
/// <item>a bar with no body content keeps the legacy one-line coloured form byte for byte —
/// <c>hnote across &lt;&lt;stepDelimiter&gt;&gt; #black:&lt;color:white&gt;…</c>;</item>
/// <item>a bar carrying a Gherkin data table, a doc string, or multi-line marker text switches to the
/// styled form — <c>hnote across &lt;&lt;stepDelimiter&gt;&gt;&lt;&lt;stepBody&gt;&gt;: label\n\n|= … |\n</c>
/// (each table/doc-string block padded with a blank display line above and below) —
/// whose colours come from the injected <c>.stepBody</c> style instead of inline tags, because the
/// <c>&lt;color:white&gt;</c> prefix styles only the first display line (everything after a <c>\n</c>
/// painted black-on-black) and inline colour tags put the statement under the ~1400-character
/// coloured-bar crash cap while the tag-free form runs to the 16,000 note ceiling.</item>
/// </list>
/// Escaping is NOT creole-tilde based: measured on the shipped engine, <c>~|</c> inside a table row does
/// not escape the pipe (the pipe still splits the cell and the tilde renders literally) and <c>~~</c>
/// renders as two tildes — while creole pairs (<c>**bold**</c>), tags and HTML entities are all live.
/// The only working neutralisation is PlantUML's <c>&lt;U+00XX&gt;</c> escapes, which these tests pin.
/// A table row is also only recognised when the physical display line starts with <c>|</c> and ends with
/// <c>|</c> — trailing whitespace kills the row — so lines are joined with a bare <c>\n</c> escape.
/// </summary>
public class StepBarPlantUmlTests
{
    private const string LegacyPrefix = "hnote across <<stepDelimiter>> #black:<color:white>";
    private const string RichPrefix = "hnote across <<stepDelimiter>><<stepBody>>: ";

    private static string[][] Menu() =>
    [
        ["name", "price"],
        ["Blueberry", "3.50"],
    ];

    // ── Form selection ──────────────────────────────────────────

    [Fact]
    public void A_bar_without_body_content_is_byte_identical_to_the_legacy_form()
    {
        Assert.Equal(
            LegacyPrefix + "Given the mock is armed",
            StepBarPlantUml.Build("Given the mock is armed"));
    }

    [Fact]
    public void A_table_switches_to_the_styled_body_form_with_breathing_room()
    {
        // One blank display line above the table and one below — butted directly against the step
        // text and the note border the table reads cramped. Measured on the pinned engine: the \n\n
        // renders a blank line, and a bare trailing \n renders an empty bottom line (each +15px).
        var bar = StepBarPlantUml.Build("Given muffins", [new StepBarTable(null, Menu())]);

        Assert.Equal(
            RichPrefix + @"Given muffins\n\n|= name |= price |\n| Blueberry | 3.50 |\n",
            bar);
    }

    [Fact]
    public void A_doc_string_switches_to_the_styled_body_form_with_breathing_room()
    {
        var bar = StepBarPlantUml.Build("Given the request body", docString: "{ \"a\": 1,\r\n  \"b\": 2 }");

        Assert.Equal(
            RichPrefix + "Given the request body\\n\\n{ \"a\": 1,\\n  \"b\": 2 }\\n",
            bar);
    }

    [Fact]
    public void Multi_line_marker_text_switches_to_the_styled_form_instead_of_vanishing()
    {
        // The legacy coloured bar paints every line after the first black-on-black — the ingest format
        // allows multi-line step text, and it was silently invisible. Multi-line text now rides the
        // styled form, where the .stepBody style colours every line.
        // Continuation lines are step text, not a data block — no padding blank lines around them.
        var real = StepBarPlantUml.Build("Given a payload\nwith a second line");
        Assert.Equal(RichPrefix + "Given a payload\\nwith a second line", real);

        // A literal backslash-n already in the text displays as a line break too, so it needs the
        // styled form just the same.
        var literal = StepBarPlantUml.Build(@"Given a payload\nwith an escape");
        Assert.StartsWith(RichPrefix, literal);
    }

    [Fact]
    public void Empty_tables_and_blank_doc_strings_leave_the_legacy_form_alone()
    {
        Assert.Equal(
            LegacyPrefix + "Given nothing",
            StepBarPlantUml.Build("Given nothing", [new StepBarTable(null, [])], docString: "   "));
    }

    [Fact]
    public void A_header_only_table_still_draws_its_header_row()
    {
        var bar = StepBarPlantUml.Build("Given columns", [new StepBarTable(null, [["a", "b"]])]);

        Assert.Equal(RichPrefix + @"Given columns\n\n|= a |= b |\n", bar);
    }

    [Fact]
    public void Continuation_text_before_a_table_is_padded_from_the_table_not_from_the_label()
    {
        var bar = StepBarPlantUml.Build("Given a payload\nwith detail",
            [new StepBarTable(null, [["a"], ["1"]])]);

        Assert.Equal(RichPrefix + @"Given a payload\nwith detail\n\n|= a |\n| 1 |\n", bar);
    }

    // ── Escaping: measured against the real engine ──────────────

    [Fact]
    public void Cell_pipes_angle_brackets_and_ampersands_render_literally_via_unicode_escapes()
    {
        // ~| does NOT escape a pipe inside a table row on the shipped engine — the pipe still splits the
        // cell and the tilde renders literally. <U+007C> is the only working literal pipe. The same
        // engine parses tags (<b>) and HTML entities (&#124;) inside cells, so < and & are escaped too.
        var bar = StepBarPlantUml.Build("Given tricky", [new StepBarTable(null,
        [
            ["field", "value"],
            ["a|b", "x&y"],
            ["a<b", "plain"],
        ])]);

        Assert.Contains("| a<U+007C>b | x<U+0026>y |", bar);
        Assert.Contains("| a<U+003C>b | plain |", bar);
        Assert.DoesNotContain("~|", bar);
    }

    [Fact]
    public void Doubled_creole_pair_markers_in_cells_are_broken_apart_with_a_zero_width_space()
    {
        // Creole pairs style table cells (**bold** renders bold, "" eats the quotes), and the tilde
        // escape is inert in this note form. Replacing the marker with <U+0022>-style escapes is
        // defeated too: those five codes substitute back BEFORE creole parsing (measured), rebuilding
        // the live pair. The one working neutralisation is a zero-width space between the two marker
        // characters — <U+200B> substitutes early, breaking the pair at parse level while rendering
        // invisibly, so the visible text is unchanged.
        var bar = StepBarPlantUml.Build("Given styles", [new StepBarTable(null,
        [
            ["field", "value"],
            ["**bold**", "http://a http://b"],
            ["c--d", "say \"\"hi\"\""],
        ])]);

        Assert.Contains("| *<U+200B>*bold*<U+200B>* |", bar);
        Assert.Contains("http:/<U+200B>/a http:/<U+200B>/b", bar);
        Assert.Contains("| c-<U+200B>-d |", bar);
        Assert.Contains("say \"<U+200B>\"hi\"<U+200B>\"", bar);
    }

    [Fact]
    public void A_tripled_marker_cannot_leave_an_adjacent_pair_behind()
    {
        var bar = StepBarPlantUml.Build("Given runs", [new StepBarTable(null, [["h"], ["***"]])]);

        Assert.Contains("| *<U+200B>*<U+200B>* |", bar);
        Assert.DoesNotContain("**", bar[RichPrefix.Length..]);
    }

    [Fact]
    public void Doc_string_lines_opening_with_creole_block_markers_are_escaped()
    {
        // A display line starting with | becomes a table row, * a bullet, = a heading, # a numbered
        // item. The tilde escape being inert, the opener becomes its <U+00XX> escape. A -- run is
        // already broken by the doubled-pair rule.
        var bar = StepBarPlantUml.Build("Given a doc string",
            docString: "|= not a header |\n* not a bullet\n= not a heading\n-- not struck --\n# not a number");

        Assert.Contains(@"\n<U+007C>= not a header |", bar);
        Assert.Contains(@"\n<U+002A> not a bullet", bar);
        Assert.Contains(@"\n<U+003D> not a heading", bar);
        Assert.Contains(@"\n-<U+200B>- not struck -<U+200B>-", bar);
        Assert.Contains(@"\n<U+0023> not a number", bar);
    }

    [Fact]
    public void Doc_string_indentation_is_preserved()
    {
        // Leading whitespace on a body line is safe (an indented | is not a table row) and keeps
        // pretty-printed payloads readable.
        var bar = StepBarPlantUml.Build("Given the body", docString: "{ \"outer\": {\n    \"inner\": true } }");

        Assert.Contains("\\n    \"inner\": true } }", bar);
    }

    [Fact]
    public void Cell_backslashes_are_defused_with_a_zero_width_space()
    {
        // A literal \n sequence in a cell value ("C:\new\table") would otherwise display as a line
        // break mid-row and tear the table apart. <U+005C> substitutes back before that processing
        // (measured — it rebuilds the live \n), so the working fix is a zero-width space after the
        // backslash.
        var bar = StepBarPlantUml.Build("Given paths", [new StepBarTable(null, [["h"], [@"C:\new\table"]])]);

        Assert.Contains(@"| C:\<U+200B>new\<U+200B>table |", bar);
    }

    [Fact]
    public void Cell_newlines_fold_to_spaces()
    {
        // Gherkin cells cannot contain line breaks, but the NDJSON ingest format could carry them; a raw
        // newline would tear the row apart mid-table.
        var bar = StepBarPlantUml.Build("Given cells", [new StepBarTable(null, [["h"], ["a\r\nb"]])]);

        Assert.Contains("| a b |", bar);
    }

    // ── Multiple tables (weaver-path tabular parameters) ────────

    [Fact]
    public void Multiple_named_tables_get_name_lines_a_single_table_does_not()
    {
        var one = StepBarPlantUml.Build("Given one",
            [new StepBarTable("inputs", [["a"], ["1"]])]);
        Assert.DoesNotContain("inputs:", one);

        var two = StepBarPlantUml.Build("Given two",
        [
            new StepBarTable("inputs", [["a"], ["1"]]),
            new StepBarTable("outputs", [["b"], ["2"]]),
        ]);
        Assert.Contains(@"\n\ninputs:\n|= a |", two);
        // Adjacent blocks share a single blank line — the "after" of one is the "before" of the next.
        Assert.Contains(@"| 1 |\n\noutputs:\n|= b |", two);
        Assert.EndsWith(@"| 2 |\n", two);
    }

    // ── Caps ────────────────────────────────────────────────────

    [Fact]
    public void The_styled_bar_is_capped_at_the_note_ceiling_not_the_coloured_bar_limit()
    {
        // No inline colour tags → the ~1400-char coloured-bar crash cap does not apply; the statement
        // rides the 16,000 note ceiling instead (measured: no crash and no refusal at 15.9k chars).
        var rows = new List<string[]> { new[] { "key", "value" } };
        for (var i = 0; i < 400; i++)
            rows.Add([$"key-{i}", new string('v', 30)]);
        var bar = StepBarPlantUml.Build("Given a big table", [new StepBarTable(null, rows.ToArray())]);

        Assert.True(bar.Length > PlantUmlStatementLimits.MaxColouredNoteBarChars, $"{bar.Length} chars");
        Assert.True(bar.Length <= PlantUmlStatementLimits.MaxNoteLineChars, $"{bar.Length} chars");
        Assert.EndsWith(PlantUmlStatementLimits.TruncationMarker, bar);
    }

    // ── The public emitters ─────────────────────────────────────

    [Fact]
    public void The_ingest_emitter_draws_a_table_and_doc_string_when_the_record_carries_them()
    {
        var bar = InteractionRecord.StepDelimiterPlantUml("Given", "the muffins exist",
            table: Menu(), docString: null);

        Assert.Equal(RichPrefix + @"Given the muffins exist\n\n|= name |= price |\n| Blueberry | 3.50 |\n", bar);

        var unchanged = InteractionRecord.StepDelimiterPlantUml("Given", "the mock is armed");
        Assert.Equal(LegacyPrefix + "Given the mock is armed", unchanged);
    }

    [Fact]
    public void A_step_marker_record_carries_its_table_into_the_diagram_override()
    {
        var record = InteractionRecord.StepMarker("t-1", "the muffins exist", DateTimeOffset.UtcNow,
            keyword: "Given", table: Menu(), docString: null);

        var logs = record.ToLogs().ToList();
        var plantUml = logs[0].PlantUml;

        Assert.NotNull(plantUml);
        Assert.Contains("<<stepBody>>", plantUml);
        Assert.Contains("| Blueberry | 3.50 |", plantUml);
    }

    [Fact]
    public void A_step_marker_record_round_trips_its_table_through_json()
    {
        var record = InteractionRecord.StepMarker("t-1", "the muffins exist", DateTimeOffset.UtcNow,
            keyword: "Given", table: Menu(), docString: "body");

        var parsed = InteractionRecord.FromJson(record.ToJson());

        Assert.Equal(Menu(), parsed.Table);
        Assert.Equal("body", parsed.DocString);
    }

    [Fact]
    public void StartStep_with_an_explicit_table_draws_it_in_the_bar()
    {
        var testId = $"bar-table-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "Given", "the muffins exist", null, null, Menu(), null);
        StepCollector.CompleteStep(testId, passed: true);

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))
            .ToArray();

        Assert.Single(logs);
        Assert.Contains("<<stepBody>>", logs[0].PlantUml!);
        Assert.Contains("| Blueberry | 3.50 |", logs[0].PlantUml!);
        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void StartStep_draws_tabular_parameters_in_the_bar()
    {
        var testId = $"bar-tabular-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "Given", "the people exist",
            ["people"], [new FakeTabularData()]);
        StepCollector.CompleteStep(testId, passed: true);

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))
            .ToArray();

        Assert.Single(logs);
        Assert.Contains("<<stepBody>>", logs[0].PlantUml!);
        Assert.Contains("|= name |= age |", logs[0].PlantUml!);
        Assert.Contains("| Alice | 30 |", logs[0].PlantUml!);
        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void StartStep_without_body_content_emits_the_legacy_bar_bytes()
    {
        var testId = $"bar-legacy-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "Given", "a user exists", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))
            .ToArray();

        Assert.Single(logs);
        Assert.Contains(LegacyPrefix + "Given a user exists", logs[0].PlantUml!);
        Assert.DoesNotContain("<<stepBody>>", logs[0].PlantUml!);
        StepCollector.ClearSteps(testId);
    }

    // ── The injected style ──────────────────────────────────────

    [Fact]
    public void A_diagram_with_a_styled_bar_gains_the_stepBody_style_block()
    {
        var bar = StepBarPlantUml.Build("Given muffins", [new StepBarTable(null, Menu())]);
        var diagram = Diagrams([Override(bar), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        Assert.Contains(".stepBody {", diagram);
        Assert.Contains("BackgroundColor black", diagram);
        Assert.Contains("FontColor white", diagram);
        Assert.Contains("LineColor white", diagram);
    }

    [Fact]
    public void A_diagram_without_a_styled_bar_does_not_gain_the_style_block()
    {
        var bar = StepBarPlantUml.Build("Given a user exists");
        var diagram = Diagrams([Override(bar), OverrideEnd(), Request("http://example.com/api/orders")]).Single();

        Assert.DoesNotContain(".stepBody", diagram);
    }

    [Fact]
    public void The_report_strip_regex_still_removes_the_styled_bar()
    {
        // collapsible-notes-script.js strips hidden step bars with
        // /\n?hnote across <<stepDelimiter>>[^\n]*\n?/g — the styled bar stays a single physical line
        // and keeps the <<stepDelimiter>> prefix precisely so this regex needs no change.
        var strip = new System.Text.RegularExpressions.Regex(@"\n?hnote across <<stepDelimiter>>[^\n]*\n?");

        var bar = StepBarPlantUml.Build("Given muffins", [new StepBarTable(null, Menu())]);
        var source = $"participant \"Api\" as api\n{bar}\napi -> api: x\n";

        var stripped = strip.Replace(source, "");
        Assert.DoesNotContain("hnote", stripped);
        Assert.DoesNotContain("Blueberry", stripped);
        Assert.Contains("api -> api: x", stripped);
    }

    // ── Harness ─────────────────────────────────────────────────

    private sealed class FakeTabularData : ITabularParameterData
    {
        public TabularColumn[] GetColumns() => [new("name", false), new("age", false)];

        public TabularRow[] GetRows() =>
        [
            new(TableRowType.Matching,
            [
                new TabularCell("Alice", null, VerificationStatus.NotApplicable),
                new TabularCell("30", null, VerificationStatus.NotApplicable),
            ]),
        ];
    }

    private static RequestResponseLog Request(string uri) =>
        new(
            TestName: "My Test",
            TestId: "test-1",
            Method: HttpMethod.Get,
            Content: null,
            Uri: new Uri(uri),
            Headers: [],
            ServiceName: "OrderService",
            CallerName: "WebApp",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false);

    private static RequestResponseLog Override(string plantUml) =>
        new(
            TestName: "My Test",
            TestId: "test-1",
            Method: "",
            Content: "",
            Uri: new Uri("http://override.com"),
            Headers: [],
            ServiceName: "",
            CallerName: "",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsOverrideStart = true,
            PlantUml = $"\n{plantUml}\n\n"
        };

    private static RequestResponseLog OverrideEnd() =>
        new(
            TestName: "My Test",
            TestId: "test-1",
            Method: "",
            Content: "",
            Uri: new Uri("http://override.com"),
            Headers: [],
            ServiceName: "",
            CallerName: "",
            Type: RequestResponseType.Request,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false)
        {
            IsOverrideEnd = true
        };

    private static string[] Diagrams(IEnumerable<RequestResponseLog> logs) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs)
            .SelectMany(t => t.PlantUmls.Select(p => p.PlainText))
            .ToArray();
}
