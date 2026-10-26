using System.Text;

namespace Kronikol.PlantUml;

/// <summary>
/// A data table drawn inside a step-delimiter bar. <see cref="Rows"/>[0] is the header row;
/// <see cref="Name"/> labels the table when a bar carries more than one (weaver-path tabular parameters).
/// </summary>
internal readonly record struct StepBarTable(string? Name, string[][] Rows);

/// <summary>
/// Builds the step-delimiter bar both emitters (<see cref="Tracking.StepCollector"/> live,
/// <see cref="Ingestion.InteractionRecord"/> ingest) draw, in one of two forms:
/// <list type="bullet">
/// <item>no body content — the legacy one-line coloured bar,
/// <c>hnote across &lt;&lt;stepDelimiter&gt;&gt; #black:&lt;color:white&gt;label</c>, byte-identical to
/// what shipped before tables joined the bar, capped at
/// <see cref="PlantUmlStatementLimits.MaxColouredNoteBarChars"/> (the colour tag's pattern overflows the
/// engine's stack in the render worker past 880 characters with V8's optimizing compilers off, 1,040 with the JIT on,
/// and the engine draws its RangeError text in place of the diagram);</item>
/// <item>a Gherkin data table, a doc string, or multi-line marker text — the styled form,
/// <c>hnote across &lt;&lt;stepDelimiter&gt;&gt;&lt;&lt;stepBody&gt;&gt;: label\n\n|= … |</c>, still one
/// physical line (the report's hide-steps strip regex and every line-oriented consumer keep working)
/// with <c>\n</c> escapes between display lines. Every table/doc-string block has one blank display
/// line above it (multi-line marker text is step text and gets none) and none below the last, which
/// the owner asked to lose in 5.0.0 (plans/OPTION_C_PLAN.md R17.6): measured on the pinned engine, a
/// bare trailing <c>\n</c> renders as an empty bottom line. Colours come from the <c>.stepBody</c> style
/// <see cref="PlantUmlCreator"/> injects, not inline tags: <c>&lt;color:white&gt;</c> styles only the
/// first display line (everything after a <c>\n</c> painted black-on-black), and a tag-free statement
/// rides the <see cref="PlantUmlStatementLimits.MaxNoteLineChars"/> ceiling instead of the coloured
/// crash cap (measured: no crash and no size refusal at 15.9k characters).</item>
/// </list>
/// <para>
/// Escaping is measured against the shipped plantuml.js build, not taken from the creole docs. The
/// <c>~</c> escape is inert in this note form (<c>~|</c> leaves the pipe live and renders the tilde),
/// so body content is neutralised with the two mechanisms that verifiably work:
/// <c>&lt;U+00XX&gt;</c> escapes for <c>|</c> <c>&lt;</c> <c>&amp;</c> and line-opening block markers
/// (these codes substitute after creole parsing, so the marker never fires), and a zero-width space
/// <c>&lt;U+200B&gt;</c> inserted between doubled pair markers (<c>**</c>, <c>""</c>, <c>__</c>, …). The quote/
/// bracket/backslash/underscore codes can NOT be used as replacements on their own — they substitute back
/// BEFORE creole parsing and rebuild the live marker — which is why pairs are broken with the invisible
/// character instead. A backslash is written <c>&lt;U+005C&gt;&lt;U+200B&gt;</c>, both as code points
/// (<c>\n</c> in a cell would break the display line mid-row, the Java engine reads the <c>\&lt;</c> of a
/// <c>\&lt;U+200B&gt;</c> as an escape, and search, which reads the diagram's source, keeps a zero-width space
/// written as the character; DIAGRAM_COLOURS_PLAN §12.5). A table
/// row is only recognised when its display line starts and ends with <c>|</c> (trailing whitespace kills
/// the row), so lines are joined with a bare <c>\n</c>.
/// </para>
/// </summary>
internal static class StepBarPlantUml
{
    /// <summary>The stereotype class of the styled body form; <see cref="PlantUmlCreator"/> injects a matching style.</summary>
    internal const string BodyNoteClass = "stepBody";

    /// <summary>
    /// The style <see cref="PlantUmlCreator"/> injects into a source holding a styled bar: the bar black, its text and its
    /// table rules white, and every line and table of it centred as a block (5.9.1, plans/REPORT_UI_FIXES_PLAN.md B1: the
    /// engine's default is left, so a table sat under the left end of its label). The tests read this one copy.
    /// </summary>
    internal const string BodyStyle = $$"""
        <style>
         .{{BodyNoteClass}} {
             BackgroundColor black
             FontColor white
             LineColor white
             HorizontalAlignment center
         }
        </style>
        """;

    /// <summary>
    /// What opens and closes every table cell's text (5.9.1, plans/REPORT_UI_FIXES_PLAN.md B3): the HTML step-param-table's
    /// <c>padding: 3px 8px</c>, as 8 px of invisible space each side and a zero-width strut raised before the text and one
    /// lowered after it, which the engine draws 3 px above and 3 px below the text (measured on the pinned engine, the
    /// plan's M-I6 variant 3). The engine cannot centre a cell's text; the page centres the header row's (B4).
    /// </summary>
    internal const string CellOpen = "<space:8><sup><U+200B></sup>";

    /// <inheritdoc cref="CellOpen"/>
    internal const string CellClose = "<sub><U+200B></sub><space:8>";

    /// <summary>
    /// The side room every line of a cell gets: the engine starts each line after a cell's line break at the cell's left,
    /// so a doc string's later lines lost the room the first had, and with it their indentation against the first.
    /// </summary>
    private const string CellRoom = "<space:8>";

    internal const string LegacyPrefix = "hnote across <<stepDelimiter>> #black:<color:white>";
    internal const string RichPrefix = $"hnote across <<stepDelimiter>><<{BodyNoteClass}>>: ";

    /// <summary>The zero-width space escape that breaks a creole pair at parse level while rendering invisibly.</summary>
    private const string Zwsp = "<U+200B>";

    /// <summary>
    /// Characters whose doubling creole reads as a span or link marker, and <c>{</c>: a <c>{{</c> opens an embedded
    /// diagram, which swallowed a doc-string line reading <c>{{</c> (3.30.2).
    /// </summary>
    private const string PairChars = "*/_-\"[]{";

    public static string Build(string label, IReadOnlyList<StepBarTable>? tables = null, string? docString = null)
    {
        var labelLines = (label ?? "").Replace("\r", "").Trim().Split('\n');
        // A step quoting a JWT, a base64 blob, a URL or a GUID list has no whitespace for wrapWidth to
        // break at, and the bar grows with it — measured, a 950-character token drew 9 450 px. Wrapping
        // here also routes such a step to the styled form automatically: the legacy coloured bar paints
        // everything after the first display line black-on-black, and the branch below already switches
        // form as soon as the label carries a break.
        // The label reaches PlantUML as written (its styling has always been live), except the three
        // prefixes that make the engine load a bundle or drop the text: LightBDD writes a table
        // parameter as <$name>, which the bar used to paint as "". The wrapper never cuts a word that
        // holds a `<`, so a `~` stays with the `<` it escapes.
        var labelLine = string.Join(@"\n", DiagramWidth.WrapLines(PlantUmlCreator.EscapeOneLineMarkup(labelLines[0]), DiagramWidth.MaxNoteTextLineChars));

        var body = new List<string>();
        // Multi-line marker text (the ingest format allows it) used to fold into the coloured bar as
        // \n escapes and paint black-on-black; as body lines of the styled form it stays readable.
        foreach (var extra in labelLines.Skip(1))
            body.AddRange(EscapeWrappedBodyLine(extra));

        // Each table/doc-string block gets one blank display line above it: butted directly against
        // the step text it reads cramped. None below the last block from 5.0.0 (R17.6): measured on the
        // pinned engine, a bare trailing \n at the end of the statement renders as an empty bottom line.
        if (tables is not null)
        {
            var named = tables.Count(t => t.Rows.Length > 0) > 1;
            foreach (var table in tables)
            {
                if (table.Rows.Length == 0)
                    continue;
                body.Add("");
                if (named && !string.IsNullOrWhiteSpace(table.Name))
                    body.Add(EscapeBodyLine(table.Name!.Trim() + ":"));
                body.Add(TableRow(table.Rows[0], header: true));
                foreach (var row in table.Rows.Skip(1))
                    body.Add(TableRow(row, header: false));
            }
        }

        // A doc string is one framed cell, so the centred bar keeps its lines' indentation, as the HTML step-docstring
        // block does (5.9.1, plans/REPORT_UI_FIXES_PLAN.md B2: centred line by line, a JSON body lost its shape). Each line
        // is wrapped and escaped as a body line, a pipe is written as a cell's is, and the pieces are joined with the
        // cell's own line break after escaping, since EscapeInline rewrites every backslash. Not cut by TableCellBudget,
        // which shares a row's width between cells side by side: the cell is alone in its row.
        if (!string.IsNullOrWhiteSpace(docString))
        {
            body.Add("");
            var lines = docString!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .SelectMany(EscapeWrappedBodyLine)
                .Select(line => line.Replace("|", "<U+007C>"))
                .ToArray();
            body.Add("|" + string.Join(CellBreak, lines.Select((line, i) =>
                (i == 0 ? CellOpen : CellRoom) + line + (i == lines.Length - 1 ? CellClose : CellRoom))) + "|");
        }

        // A literal \n already in the text displays as a line break too — anything multi-line on
        // screen needs the styled form to be visible at all.
        if (body.Count == 0 && !labelLine.Contains(@"\n", StringComparison.Ordinal))
            return LegacyPrefix + PlantUmlStatementLimits.TruncateLabel(
                labelLine, PlantUmlStatementLimits.MaxColouredNoteBarChars - LegacyPrefix.Length);

        var content = string.Join(@"\n", body.Prepend(labelLine));
        return RichPrefix + PlantUmlStatementLimits.TruncateLabel(
            content, PlantUmlStatementLimits.MaxNoteLineChars - RichPrefix.Length);
    }

    /// <summary>A line break inside a table cell: the engine reads a doubled backslash and an n there as one.</summary>
    internal const string CellBreak = @"\\n";

    /// <summary>
    /// One creole table row: <c>|=</c> before each header cell, <c>|</c> before each data cell, every cell's text between
    /// <see cref="CellOpen"/> and <see cref="CellClose"/>.
    /// </summary>
    private static string TableRow(string[] cells, bool header)
    {
        var sb = new StringBuilder();
        // Creole table cells are the one construct that never wraps — not even at spaces — so a cell
        // can only be shortened, and the budget has to be shared across the row because a row's width
        // is the sum of its cells.
        var budget = DiagramWidth.TableCellBudget(cells.Length);
        foreach (var cell in cells)
            sb.Append(header ? "|=" : "|").Append(CellOpen).Append(EscapeCell(cell, budget)).Append(CellClose);
        return sb.Append('|').ToString();
    }

    /// <summary>
    /// Neutralises everything in a cell that could style, tear or restructure the row: pipes become
    /// literal-pipe escapes, newlines fold to spaces (a break mid-row tears the table), and the
    /// inline rules of <see cref="EscapeInline"/> apply.
    /// </summary>
    private static string EscapeCell(string? cell, int budget)
    {
        var flat = (cell ?? "").Replace("\r\n", "\n").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return EscapeInline(DiagramWidth.ElideCell(flat, budget)).Replace("|", "<U+007C>");
    }

    /// <summary>
    /// One source line of body content, broken to <see cref="DiagramWidth.MaxNoteTextLineChars"/> and
    /// escaped <b>piece by piece</b>. The order matters: <see cref="EscapeInline"/> rewrites every
    /// backslash, so escaping a line the wrapper had already woven line breaks into
    /// would turn every one of those breaks back into literal text.
    /// </summary>
    private static IEnumerable<string> EscapeWrappedBodyLine(string line) =>
        DiagramWidth.WrapLines(line, DiagramWidth.MaxNoteTextLineChars).Select(EscapeBodyLine);

    /// <summary>A display line creole reads as a separator (<c>..x..</c>, <c>..</c>) or a dotted rule (<c>....</c>).</summary>
    private static bool IsSeparatorOrRule(ReadOnlySpan<char> line) =>
        line.StartsWith("..") && (line.Length == 2 || (line.Length >= 4 && line.EndsWith("..")));

    /// <summary>
    /// One body display line (a doc-string line, a table label, a continuation of multi-line marker
    /// text): inline rules plus the line-opening block markers — a leading <c>|</c> starts a table
    /// row, <c>*</c> a bullet, <c>=</c> a heading, <c>#</c> a numbered item, <c>..x..</c> a separator
    /// and <c>....</c> a rule. Leading whitespace is preserved (indented lines are never table rows,
    /// and pretty-printed payloads stay readable).
    /// </summary>
    private static string EscapeBodyLine(string line)
    {
        var escaped = EscapeInline(line.Replace("\r", ""));

        var start = 0;
        while (start < escaped.Length && escaped[start] is ' ' or '\t')
            start++;
        if (start < escaped.Length && escaped[start] is '|' or '*' or '=' or '#')
            escaped = escaped[..start] + $"<U+{(int)escaped[start]:X4}>" + escaped[(start + 1)..];
        else if (IsSeparatorOrRule(escaped.AsSpan(start).TrimEnd()))
            // `..x..` is a separator drawn as a line with x in it, and `....` a rule that draws nothing (3.30.2).
            escaped = escaped[..start] + "<U+002E>" + escaped[(start + 1)..];

        return escaped;
    }

    /// <summary>
    /// The context-free rules: <c>&lt;</c> and <c>&amp;</c> become their late-substituted escapes (tags
    /// and HTML entities are live in bar text, and a zero-width space follows an <c>&amp;</c> that opens a
    /// decimal reference, which the engine would still decode), a <c>~</c> and a builtin call's <c>%</c>
    /// become theirs too, a zero-width space breaks every doubled pair marker, a backslash becomes its code
    /// point and a zero-width space's (so a literal <c>\n</c> in the data cannot become a line break), and a
    /// character that ends a one-line statement becomes its code point.
    /// </summary>
    private static string EscapeInline(string text)
    {
        var sb = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '<':
                    // A captured `<U+hhhh>` would be decoded again after `<U+003C>`: a zero-width space keeps it text (§12.5).
                    sb.Append(PlantUmlCreator.OpensCodePoint(text, i) ? "<U+003C>" + Zwsp : "<U+003C>");
                    continue;
                case '&':
                    sb.Append("<U+0026>");
                    // Under the note wrap width the engine decodes code points before references, so `<U+0026>#39;`
                    // would still paint `'`: a zero-width space after it breaks the reference (3.30.2).
                    if (PlantUmlCreator.IsDecimalCharacterReference(text, i)) sb.Append(Zwsp);
                    continue;
                case '~':
                    // Creole's own escape: before / < . " ] # * _ - [ it paints only what follows, and a `~~x~~`
                    // pair is a wave. As its code point a tilde paints and escapes nothing (3.30.2).
                    sb.Append("<U+007E>");
                    continue;
                case '%' when PlantUmlCreator.IsBuiltinCall(text, i):
                    // The preprocessor evaluates a builtin call wherever it sits: `%date()` painted the date (3.30.2).
                    sb.Append("<U+0025>");
                    continue;
                case '\\':
                    // Its code point and a zero-width space's: a bare backslash meets the next character (`\n` breaks the
                    // line, `\t` is a tab), the Java engine read the `\<` of the `\<U+200B>` written here before as an
                    // escape and painted "U+200B>", and search keeps a zero-width space written as the character, so it
                    // could not find the text (§12.5). This form draws a backslash on both engines and searches as one.
                    sb.Append("<U+005C>").Append(Zwsp);
                    continue;
                case var _ when PlantUmlCreator.IsStatementBreak(c):
                    // NEL, LINE SEPARATOR and PARAGRAPH SEPARATOR end the bar's one statement on both engines (§12.5).
                    sb.Append(PlantUmlCreator.CodePoint(c));
                    continue;
            }

            sb.Append(c);
            if (PairChars.Contains(c) && i + 1 < text.Length && text[i + 1] == c)
                sb.Append(Zwsp);
        }

        return sb.ToString();
    }
}
