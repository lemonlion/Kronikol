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
/// <see cref="PlantUmlStatementLimits.MaxColouredNoteBarChars"/> (the coloured form crashes the JS
/// engine outright past ~1400 characters);</item>
/// <item>a Gherkin data table, a doc string, or multi-line marker text — the styled form,
/// <c>hnote across &lt;&lt;stepDelimiter&gt;&gt;&lt;&lt;stepBody&gt;&gt;: label\n\n|= … |\n</c>, still one
/// physical line (the report's hide-steps strip regex and every line-oriented consumer keep working)
/// with <c>\n</c> escapes between display lines. Every table/doc-string block is padded with one
/// blank display line above and below (adjacent blocks share the one between them; multi-line
/// marker text is step text and gets none) — measured on the pinned engine, an empty display line
/// renders as a blank note line even as a bare trailing <c>\n</c>. Colours come from the <c>.stepBody</c> style
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
/// <c>&lt;U+200B&gt;</c> inserted between doubled pair markers (<c>**</c>, <c>""</c>, <c>__</c>, …) and
/// after backslashes (<c>\n</c> in a cell would break the display line mid-row). The quote/bracket/
/// backslash/underscore codes can NOT be used as replacements — they substitute back BEFORE creole
/// parsing and rebuild the live marker — which is why pairs are broken with the invisible character
/// instead. A table row is only recognised when its display line starts and ends with <c>|</c>
/// (trailing whitespace kills the row), so lines are joined with a bare <c>\n</c>.
/// </para>
/// </summary>
internal static class StepBarPlantUml
{
    /// <summary>The stereotype class of the styled body form; <see cref="PlantUmlCreator"/> injects a matching style.</summary>
    internal const string BodyNoteClass = "stepBody";

    internal const string LegacyPrefix = "hnote across <<stepDelimiter>> #black:<color:white>";
    internal const string RichPrefix = $"hnote across <<stepDelimiter>><<{BodyNoteClass}>>: ";

    /// <summary>The zero-width space escape that breaks a creole pair at parse level while rendering invisibly.</summary>
    private const string Zwsp = "<U+200B>";

    /// <summary>Characters whose doubling creole reads as a span/link marker.</summary>
    private const string PairChars = "*/_-\"[]";

    public static string Build(string label, IReadOnlyList<StepBarTable>? tables = null, string? docString = null)
    {
        var labelLines = (label ?? "").Replace("\r", "").Trim().Split('\n');
        var labelLine = labelLines[0];

        var body = new List<string>();
        // Multi-line marker text (the ingest format allows it) used to fold into the coloured bar as
        // \n escapes and paint black-on-black; as body lines of the styled form it stays readable.
        foreach (var extra in labelLines.Skip(1))
            body.Add(EscapeBodyLine(extra));

        // Each table/doc-string block gets one blank display line above and one below — butted
        // directly against the step text and the note border they read cramped. Adjacent blocks
        // share the blank line between them. Measured on the pinned engine: an empty body entry
        // renders as a blank note line, including as a bare trailing \n at the end of the statement.
        var padded = false;

        if (tables is not null)
        {
            var named = tables.Count(t => t.Rows.Length > 0) > 1;
            foreach (var table in tables)
            {
                if (table.Rows.Length == 0)
                    continue;
                body.Add("");
                padded = true;
                if (named && !string.IsNullOrWhiteSpace(table.Name))
                    body.Add(EscapeBodyLine(table.Name!.Trim() + ":"));
                body.Add(TableRow(table.Rows[0], header: true));
                foreach (var row in table.Rows.Skip(1))
                    body.Add(TableRow(row, header: false));
            }
        }

        if (!string.IsNullOrWhiteSpace(docString))
        {
            body.Add("");
            padded = true;
            foreach (var line in docString!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                body.Add(EscapeBodyLine(line));
        }

        if (padded)
            body.Add("");

        // A literal \n already in the text displays as a line break too — anything multi-line on
        // screen needs the styled form to be visible at all.
        if (body.Count == 0 && !labelLine.Contains(@"\n", StringComparison.Ordinal))
            return LegacyPrefix + PlantUmlStatementLimits.TruncateLabel(
                labelLine, PlantUmlStatementLimits.MaxColouredNoteBarChars - LegacyPrefix.Length);

        var content = string.Join(@"\n", body.Prepend(labelLine));
        return RichPrefix + PlantUmlStatementLimits.TruncateLabel(
            content, PlantUmlStatementLimits.MaxNoteLineChars - RichPrefix.Length);
    }

    /// <summary>One creole table row: <c>|= h1 |= h2 |</c> for the header, <c>| c1 | c2 |</c> for data.</summary>
    private static string TableRow(string[] cells, bool header)
    {
        var sb = new StringBuilder();
        foreach (var cell in cells)
            sb.Append(header ? "|= " : "| ").Append(EscapeCell(cell)).Append(' ');
        return sb.Append('|').ToString();
    }

    /// <summary>
    /// Neutralises everything in a cell that could style, tear or restructure the row: pipes become
    /// literal-pipe escapes, newlines fold to spaces (a break mid-row tears the table), and the
    /// inline rules of <see cref="EscapeInline"/> apply.
    /// </summary>
    private static string EscapeCell(string? cell)
    {
        var flat = (cell ?? "").Replace("\r\n", "\n").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return EscapeInline(flat).Replace("|", "<U+007C>");
    }

    /// <summary>
    /// One body display line (a doc-string line, a table label, a continuation of multi-line marker
    /// text): inline rules plus the line-opening block markers — a leading <c>|</c> starts a table
    /// row, <c>*</c> a bullet, <c>=</c> a heading, <c>#</c> a numbered item. Leading whitespace is
    /// preserved (indented lines are never table rows, and pretty-printed payloads stay readable).
    /// </summary>
    private static string EscapeBodyLine(string line)
    {
        var escaped = EscapeInline(line.Replace("\r", ""));

        var start = 0;
        while (start < escaped.Length && escaped[start] is ' ' or '\t')
            start++;
        if (start < escaped.Length && escaped[start] is '|' or '*' or '=' or '#')
            escaped = escaped[..start] + $"<U+{(int)escaped[start]:X4}>" + escaped[(start + 1)..];

        return escaped;
    }

    /// <summary>
    /// The context-free rules: <c>&lt;</c> and <c>&amp;</c> become their late-substituted escapes (tags
    /// and HTML entities are live in bar text), a zero-width space breaks every doubled pair marker,
    /// and one follows every backslash (so a literal <c>\n</c> in the data cannot become a line break).
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
                    sb.Append("<U+003C>");
                    continue;
                case '&':
                    sb.Append("<U+0026>");
                    continue;
                case '\\':
                    sb.Append(c).Append(Zwsp);
                    continue;
            }

            sb.Append(c);
            if (PairChars.Contains(c) && i + 1 < text.Length && text[i + 1] == c)
                sb.Append(Zwsp);
        }

        return sb.ToString();
    }
}
