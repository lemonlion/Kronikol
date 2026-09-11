using System.Text;

namespace Kronikol.PlantUml;

/// <summary>
/// The one width budget every generated diagram holds to, and the wrapper that enforces it.
/// <para>
/// PlantUML's default <c>PLANTUML_LIMIT_SIZE</c> is 4096 pixels. A diagram wider than that is not
/// rejected — it is <b>cropped</b>, silently, keeping the top-left corner and discarding the rest,
/// with no error in the output and nothing in the log. Measured 2026-09-11, the limit is
/// <b>raster-only</b>: the same source drew SVG at 24 185 px uncropped and PNG at exactly 4 096. So
/// the crop bites <see cref="PlantUmlImageFormat.Png"/> rendering and a source a reader copies out of
/// the report and pastes into plantuml.com — never the in-report browser render, whose engine runs at
/// <c>maxSvgSize: 98304</c>.
/// </para>
/// <para>
/// The budget is nonetheless enforced for every renderer, because a crop is not the only cost of an
/// over-wide diagram: <c>.plantuml-browser svg { max-width: 100% }</c> scales a diagram that overflows
/// its container <em>down</em> rather than scrolling it, so past the container width more pixels mean
/// smaller text. A diagram that wants five thousand pixels is already unreadable in the report.
/// </para>
/// <para>
/// <c>skinparam wrapWidth</c> is no substitute. It does not reach message labels, group labels,
/// participant boxes, titles or activity diagrams at all in real Java PlantUML, and where it does
/// apply it only breaks at whitespace — and the text that gets long (locator chains, connection
/// strings, tokens, fully-qualified type names, minified payloads) frequently has none. The line
/// breaks therefore have to be in the source.
/// </para>
/// </summary>
internal static class DiagramWidth
{
    /// <summary>
    /// PlantUML's default <c>PLANTUML_LIMIT_SIZE</c> — the width every generated diagram stays under.
    /// </summary>
    internal const int PlantUmlLimitSize = 4096;

    /// <summary>
    /// The default <c>skinparam wrapWidth</c>, in pixels — how wide a note body or (on the JS build)
    /// an arrow label is drawn before the engine breaks it. It only ever breaks at whitespace, so it
    /// is a complement to the character budgets above, never a replacement for them.
    /// </summary>
    internal const int DefaultWrapWidthPx = 800;

    /// <summary>
    /// Narrowest <c>DiagramNoteWrapWidth</c> the generator accepts.
    /// <para>
    /// <c>PlantUmlCreator.MaxNoteChunkChars</c> is 80 because eighty characters is about 720 pixels at
    /// the note font size, which keeps a form-url-encoded chunk on one drawn line — if the engine has
    /// to break one it breaks it mid-line, and can cut one of Kronikol's own inline colour tags in
    /// half. Flooring the option at the width that arithmetic already assumes keeps that relationship
    /// true without making the chunk size (and therefore every form body's bytes) depend on it.
    /// </para>
    /// </summary>
    internal const int MinNoteWrapWidthPx = 720;

    /// <summary>
    /// Widest <c>DiagramNoteWrapWidth</c> the generator accepts: past
    /// <see cref="PlantUmlLimitSize"/> a note alone can crop a raster render.
    /// </summary>
    internal const int MaxNoteWrapWidthPx = PlantUmlLimitSize;

    /// <summary>Validates a configured note wrap width, naming the supported range when it is out of it.</summary>
    internal static int ValidateNoteWrapWidth(int wrapWidth) =>
        wrapWidth >= MinNoteWrapWidthPx && wrapWidth <= MaxNoteWrapWidthPx
            ? wrapWidth
            : throw new ArgumentOutOfRangeException(nameof(wrapWidth), wrapWidth,
                $"DiagramNoteWrapWidth must be between {MinNoteWrapWidthPx} and {MaxNoteWrapWidthPx} pixels. "
                + $"{MinNoteWrapWidthPx} is the width a form-url-encoded note chunk needs to stay on one drawn line; "
                + $"{MaxNoteWrapWidthPx} is PlantUML's own PLANTUML_LIMIT_SIZE, past which a rasterised diagram is cropped.");

    /// <summary>
    /// Longest display line a message/arrow label, an activity node label or a diagram title is drawn
    /// on. Roughly 600 pixels at the label font size — wide enough that ordinary labels are untouched
    /// and keep their exact bytes, and narrow enough that text at the 2 000-character statement cap
    /// draws as a block of about twenty short lines rather than one very long one.
    /// </summary>
    internal const int MaxLabelLineChars = 100;

    /// <summary>
    /// Longest display line a participant's name is drawn on. Sequence participant boxes never wrap —
    /// not at whitespace, not at <c>wrapWidth</c> — so this is the only bound they have. 80 is well
    /// past any real service name, so no existing diagram changes.
    /// </summary>
    internal const int MaxNameLineChars = 80;

    /// <summary>
    /// Longest display line the text of an injected marker note (a step-delimiter bar, an assertion
    /// note) is drawn on. Notes <em>do</em> honour <c>wrapWidth</c>, but only at whitespace, so this
    /// bounds the case that has none.
    /// </summary>
    internal const int MaxNoteTextLineChars = 110;

    /// <summary>
    /// Longest a single creole table cell is drawn before it is elided. Creole table cells are the one
    /// construct that never wraps <b>even at spaces</b>, so a cell cannot be broken — only shortened.
    /// </summary>
    internal const int MaxTableCellChars = 60;

    /// <summary>
    /// Characters that mean a word is creole markup — a link, a tag, an escape — and must never be cut
    /// part-way: half of <c>[[#anchor</c> is literal text, half of <c>&lt;size:10&gt;</c> is a broken
    /// tag, and a stranded <c>\</c> would eat the <c>n</c> of the break that follows it.
    /// </summary>
    private static readonly char[] UnsplittableMarkup = ['[', ']', '<', '>', '\\'];

    /// <summary>The escape PlantUML reads as a display-line break inside a label or a one-line note.</summary>
    internal const string EscapedBreak = @"\n";

    /// <summary>
    /// Breaks <paramref name="text"/> onto display lines of at most <paramref name="budget"/>
    /// characters, and returns it <b>unchanged</b> when every line already fits — which is nearly
    /// always, and is what keeps ordinary diagrams byte-identical.
    /// <para>
    /// The text's existing breaks are structure, so each display line is wrapped independently and the
    /// breaks between them are preserved exactly. <paramref name="lineBreak"/> is <c>\n</c>-the-escape
    /// for labels and one-line notes, and a real newline for note bodies.
    /// </para>
    /// </summary>
    internal static string Wrap(string text, int budget, string lineBreak = EscapedBreak)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= budget)
            return text;

        var lines = text.Split(lineBreak);
        var wrapped = new List<string>(lines.Length);
        var changed = false;

        foreach (var line in lines)
        {
            var before = wrapped.Count;
            WrapOneLine(line, budget, wrapped);
            changed |= wrapped.Count - before != 1 || wrapped[before] != line;
        }

        return changed ? string.Join(lineBreak, wrapped) : text;
    }

    /// <summary>
    /// The body of a <c>note … end note</c> block, bounded to <see cref="MaxNoteTextLineChars"/>.
    /// <para>
    /// A block note's display lines are its <b>source</b> lines: measured on real PlantUML, a
    /// <c>\n</c> escape inside one draws as the two literal characters and breaks nothing (660 z's on
    /// one line drew 4 341 px; the same text carrying five <c>\n</c> escapes drew 4 395 px — wider, not
    /// narrower — and only real newlines brought it to 766). The single-line
    /// <c>hnote across &lt;&lt;stepBody&gt;&gt;: …</c> form is the opposite way round, which is why the
    /// two callers wrap with different break characters.
    /// </para>
    /// <para>
    /// A body line reading exactly <c>end note</c> would close the note early — truncating the diagram
    /// and desynchronising the report's assertion-strip regex — so one is neutralised with a
    /// zero-width space, which the parser does not read as the terminator and which draws as nothing.
    /// </para>
    /// </summary>
    internal static string WrapBlockNoteBody(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var wrapped = new List<string>(lines.Length);

        foreach (var line in lines)
        foreach (var piece in WrapLines(line, MaxNoteTextLineChars))
            wrapped.Add(piece.Trim() is "end note" or "endnote" ? "​" + piece : piece);

        return string.Join("\n", wrapped);
    }

    /// <summary>
    /// <paramref name="line"/> broken into display lines of at most <paramref name="budget"/>
    /// characters, as a list. Use this rather than <see cref="Wrap"/> wherever each resulting line has
    /// to be escaped separately afterwards — the step bar's escaping puts a zero-width space after
    /// every backslash, which would neutralise a <c>\n</c> the wrapper had already woven in.
    /// </summary>
    internal static List<string> WrapLines(string line, int budget)
    {
        var into = new List<string>(1);
        WrapOneLine(line, budget, into);
        return into;
    }

    /// <summary>
    /// The per-cell character budget for a creole table row of <paramref name="cellCount"/> cells. A
    /// row never wraps and is the sum of its cells, so bounding one cell is not enough: the row's
    /// budget is shared out, with a floor so that a very wide table still shows something in every
    /// column.
    /// </summary>
    internal static int TableCellBudget(int cellCount) =>
        cellCount <= 1 ? MaxTableCellChars : Math.Max(MinTableCellChars, MaxTableRowChars / cellCount);

    /// <summary>Longest a whole creole table row is drawn before its cells start being elided.</summary>
    internal const int MaxTableRowChars = 380;

    /// <summary>The floor <see cref="TableCellBudget"/> never elides below, however many columns there are.</summary>
    internal const int MinTableCellChars = 24;

    /// <summary>
    /// One creole table cell, shortened to <see cref="MaxTableCellChars"/> with a trailing ellipsis
    /// when it is longer. A cell is the one place a break is impossible — a newline inside a row tears
    /// the table apart — so eliding is the only way to bound it, and a row is the sum of its cells.
    /// </summary>
    internal static string ElideCell(string cell, int budget = MaxTableCellChars) =>
        cell.Length <= budget ? cell : cell[..(budget - 1)] + "…";

    /// <summary>Appends <paramref name="line"/> to <paramref name="into"/>, broken between its atoms.</summary>
    private static void WrapOneLine(string line, int budget, List<string> into)
    {
        if (line.Length <= budget)
        {
            into.Add(line);
            return;
        }

        var current = new StringBuilder();

        foreach (var atom in Atoms(line, budget))
        {
            if (current.Length > 0 && current.Length + 1 + atom.Length > budget)
            {
                into.Add(current.ToString());
                current.Clear();
            }

            if (atom.Length <= budget || !CanSplit(atom))
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(atom);
                continue;
            }

            // A word no line could hold and no space in it to break at. Cutting it is the only way to
            // bound the width, and it is safe precisely because CanSplit ruled out markup. The pieces
            // are whole lines rather than words, so no space is introduced into the middle of a word.
            if (current.Length > 0)
            {
                into.Add(current.ToString());
                current.Clear();
            }

            for (var at = 0; at < atom.Length; at += budget)
            {
                var piece = atom.Substring(at, Math.Min(budget, atom.Length - at));
                if (at + budget < atom.Length)
                    into.Add(piece);
                else
                    current.Append(piece); // the tail carries on, so the next atom can join it
            }
        }

        if (current.Length > 0)
            into.Add(current.ToString());
    }

    /// <summary>
    /// The units a line may be broken between, largest first: one per comma-separated item — a list of
    /// whole operations, columns or arguments is what makes a wrapped label readable — falling back to
    /// words inside an item too long to stand alone.
    /// </summary>
    private static IEnumerable<string> Atoms(string line, int budget)
    {
        foreach (var item in CommaSeparatedItems(line))
        {
            if (item.Length <= budget)
            {
                yield return item;
                continue;
            }

            foreach (var word in item.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                yield return word;
        }
    }

    /// <summary>Splits on <c>", "</c>, keeping each comma with the item it terminates.</summary>
    private static IEnumerable<string> CommaSeparatedItems(string line)
    {
        var start = 0;
        for (var i = 0; i + 1 < line.Length; i++)
        {
            if (line[i] != ',' || line[i + 1] != ' ') continue;
            yield return line[start..(i + 1)];
            start = i + 2;
        }

        if (start < line.Length)
            yield return line[start..];
    }

    /// <summary>Whether a word can be cut mid-way — see <see cref="UnsplittableMarkup"/>.</summary>
    private static bool CanSplit(string word) => word.IndexOfAny(UnsplittableMarkup) < 0;
}
