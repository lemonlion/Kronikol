using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.PlantUml;

/// <summary>
/// What kind of statement a physical line of PlantUML source is, as far as the parser's length limits
/// are concerned.
/// </summary>
internal enum PlantUmlStatementKind
{
    /// <summary>A message/arrow statement — <c>a -&gt; b: label</c>. Capped at <see cref="PlantUmlStatementLimits.MaxMessageStatementChars"/>.</summary>
    Message,

    /// <summary>A fragment opener carrying a label — <c>loop</c>, <c>alt</c>, <c>partition</c>. Capped, lower, at <see cref="PlantUmlStatementLimits.MaxBlockLabelChars"/>.</summary>
    BlockOpener,

    /// <summary>
    /// A one-line note carrying a colour or font tag — the step-delimiter bar
    /// <c>hnote across &lt;&lt;stepDelimiter&gt;&gt; #black:&lt;color:white&gt;…</c>. Capped at
    /// <see cref="PlantUmlStatementLimits.MaxColouredNoteBarChars"/>, and the cheapest of all the limits.
    /// </summary>
    ColouredNoteBar,

    /// <summary>A note statement without an inline colour tag — <c>note over a : text</c>, <c>note left</c>. Capped only at the note ceiling.</summary>
    Note,

    /// <summary>A line inside an open note block — captured payload. Capped only at the note ceiling.</summary>
    NoteBody,

    /// <summary>A <c>'</c> comment. No cap.</summary>
    Comment,

    /// <summary>A preprocessor or diagram directive — <c>!theme</c>, <c>@startuml</c>. No cap.</summary>
    Directive,

    /// <summary>Anything else — participants, <c>skinparam</c>, <c>autonumber</c>, blank lines. Left alone.</summary>
    Other
}

/// <summary>
/// The statement-length limits PlantUML's parser enforces, measured against the engine Kronikol ships
/// (originally <c>lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched</c>,
/// re-verified against the <c>@v1.2026.6-patched</c> build and again against the stock
/// <c>@v1.2026.8beta1-0e4f452</c> build that replaced it). They are per statement
/// kind, not one global line limit, and they fail in two different ways — neither of which says
/// "too long":
/// <list type="bullet">
/// <item><description>an over-long <b>message</b> matches no rule, so the parser abandons the diagram
/// and the engine draws <c>Syntax Error?</c> over the whole fragment (or silently draws the wrong
/// diagram when the class-parse fallback succeeds);</description></item>
/// <item><description>an over-long <b>coloured note bar</b> or — since the 1.2026.8beta1 build — an
/// over-long <b>block opener</b> takes the engine's own renderer down with
/// <c>RangeError: Maximum call stack size exceeded</c> and produces no SVG at all.</description></item>
/// </list>
/// <para>Measured caps on the trimmed statement (the value below each is the constant, kept under the
/// measurement so a small engine drift does not reopen the bug; the 1.2026.8beta1 re-measurement moved
/// only the TeaVM-build artifacts — upward, so every constant stays safely under its cap):</para>
/// <list type="table">
/// <item><term><c>a -&gt; b: …</c>, <c>a --&gt; b: …</c>, <c>a -[#F39C12]&gt; b: …</c></term><description>2000 — exactly, on every build measured, and on the
/// whole statement: a 27-character prefix leaves a 1973-character label, not a longer statement.</description></item>
/// <item><term><c>loop</c> 1476, <c>alt</c> 1477, <c>group</c> 1482, <c>opt</c> 1484 (1.2026.6 parse limits;
/// on 1.2026.8beta1 the parse accepts more but the engine stack-overflows around <c>loop</c> 3660 / <c>group</c> 4975 / <c>alt</c>,<c>opt</c> 5641)</term><description>constant 1471</description></item>
/// <item><term><c>hnote across … #black:&lt;color:white&gt;…</c> 1458–1534 (≈4124 on 1.2026.8beta1; a stack-overflow
/// edge, so it wobbles between processes)</term><description>constant 1400</description></item>
/// <item><term>note bodies 16371, <c>note over a : …</c> 16392, plain <c>hnote across</c> 16398
/// (16370/16377/16376 on 1.2026.8beta1 — unchanged)</term><description>constant 16000</description></item>
/// </list>
/// <para>
/// Leading and trailing whitespace is not counted — a valid short arrow padded to 2500 characters with
/// trailing spaces parses — so every cap applies to the trimmed statement.
/// </para>
/// <para>
/// <b>Which limits are PlantUML's and which are the JS build's.</b> Measured against real Java PlantUML
/// through IKVM (see <c>IkvmStatementLimitTests</c>): the <b>2,000-character message limit is PlantUML's
/// own</b> — Java refuses 2,001 exactly as the JS build does — while the block-opener limit and the
/// coloured-note-bar crash are artifacts of the TeaVM build, which Java draws far past. Every cap is
/// applied where the source is written rather than where a renderer is chosen, because the same source
/// may be rendered either way.
/// </para>
/// <para>
/// Measuring this is easy to get wrong: when a message statement is too long the parser falls back to
/// reading the source as a <em>class</em> diagram, and that fallback often succeeds — echoing the label
/// text and emitting no <c>Syntax Error</c> banner. "Is the label in the SVG?" therefore passes for both
/// outcomes. The signal that separates them is that a sequence diagram draws each participant twice, as
/// a head box and a foot box.
/// </para>
/// </summary>
internal static class PlantUmlStatementLimits
{
    /// <summary>Longest message/arrow statement the parser accepts. Measured at exactly 2000; 2001 fails.</summary>
    public const int MaxMessageStatementChars = 2000;

    /// <summary>
    /// Longest <c>loop</c>/<c>alt</c>-style block opener. Measured between 1476 (<c>loop</c>) and 1484
    /// (<c>opt</c>) — an oddly specific range, which is why the boundary is pinned by an integration test
    /// rather than trusted as a documented constant.
    /// </summary>
    public const int MaxBlockLabelChars = 1471;

    /// <summary>
    /// Longest one-line note carrying a colour tag. Measured at 1458 for the real step-delimiter bar
    /// (<c>hnote across &lt;&lt;stepDelimiter&gt;&gt; #black:&lt;color:white&gt;…</c>) — past it the engine
    /// overflows its own JS stack and the diagram is lost entirely, so this one is worth truncating a long
    /// Gherkin step for. The same bar <em>without</em> a colour tag runs to 16398.
    /// </summary>
    public const int MaxColouredNoteBarChars = 1400;

    /// <summary>
    /// Ceiling for note content of any kind. Measured at 16371–16398; a pure backstop, since Kronikol
    /// chunks note values long before this.
    /// </summary>
    public const int MaxNoteLineChars = 16000;

    /// <summary>Appended where a statement is cut, so a reader can tell truncation from a short value.</summary>
    public const string TruncationMarker = "…";

    /// <summary>The cap for a statement of this kind, or <c>null</c> when the engine imposes none.</summary>
    public static int? CapFor(PlantUmlStatementKind kind) => kind switch
    {
        PlantUmlStatementKind.Message => MaxMessageStatementChars,
        PlantUmlStatementKind.BlockOpener => MaxBlockLabelChars,
        PlantUmlStatementKind.ColouredNoteBar => MaxColouredNoteBarChars,
        PlantUmlStatementKind.Note or PlantUmlStatementKind.NoteBody => MaxNoteLineChars,
        _ => null
    };

    /// <summary>
    /// Cuts <paramref name="label"/> down to <paramref name="budget"/> characters, marker included,
    /// without stranding a backslash from the character it escapes (<c>\n</c> inside a label is a
    /// two-character escape). Returns the label unchanged when it already fits.
    /// </summary>
    public static string TruncateLabel(string label, int budget)
    {
        if (budget <= 0)
            return string.Empty;
        if (label.Length <= budget)
            return label;

        var cut = Math.Max(0, budget - TruncationMarker.Length);

        // A `\` immediately before the cut belongs to a two-character escape whose partner is gone.
        var trailingSlashes = 0;
        while (cut - trailingSlashes > 0 && label[cut - trailingSlashes - 1] == '\\')
            trailingSlashes++;
        if (trailingSlashes % 2 == 1)
            cut--;

        return string.Concat(label.AsSpan(0, cut), TruncationMarker);
    }

    /// <summary>
    /// Caps a whole physical statement, preserving whatever whitespace surrounded it — indentation, and
    /// the <c>\r</c> of a CRLF line the caller split on <c>\n</c>. Whitespace does not count toward the
    /// engine's limit, so only the trimmed statement is measured.
    /// </summary>
    public static string TruncateStatement(string line, int max)
    {
        var trimmed = line.Trim();
        if (trimmed.Length <= max)
            return line;

        var leading = line[..(line.Length - line.TrimStart().Length)];
        var trailing = line[(line.Length - (line.Length - line.TrimEnd().Length))..];
        return leading + TruncateLabel(trimmed, max) + trailing;
    }
}

/// <summary>
/// The one funnel every generated diagram line passes through, capping only what the engine actually
/// caps. Making "no emitted message statement exceeds 2000 characters" an enforced invariant here beats
/// asking each call site to remember it — and leaves comments, directives and participant declarations,
/// which the engine does not limit, exactly as they were.
/// <para>
/// Stateful: note blocks span lines, and a note body is captured payload that may contain anything that
/// looks like an arrow statement. A call that ends mid-line is passed through untouched and joined to the
/// next call for the purpose of tracking note state.
/// </para>
/// </summary>
internal sealed partial class PlantUmlStatementGuard
{
    private int _noteDepth;
    private string _partialLine = "";

    /// <summary>Forgets any note state — used when a new diagram fragment starts from a clean prefix.</summary>
    public void Reset()
    {
        _noteDepth = 0;
        _partialLine = "";
    }

    /// <summary>
    /// Returns <paramref name="text"/> with every over-long statement in it capped.
    /// <paramref name="terminated"/> says whether the caller will follow it with a newline.
    /// </summary>
    public string Apply(string text, bool terminated)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (terminated) _partialLine = "";
            return text;
        }

        var result = new StringBuilder(text.Length);
        var start = 0;

        while (start <= text.Length)
        {
            var newline = text.IndexOf('\n', start);
            var isLast = newline < 0;
            var end = isLast ? text.Length : newline;
            var segment = text[start..end];

            if (isLast && !terminated)
            {
                // An unterminated tail: the caller will finish this line on a later call, so it cannot be
                // classified yet. Pass it through and remember it for the note-state pass.
                _partialLine += segment;
                result.Append(segment);
                break;
            }

            var whole = _partialLine.Length > 0 ? _partialLine + segment : segment;
            var capped = Advance(whole);

            // Only the part this call contributed can be rewritten — the rest is already in the builder.
            result.Append(_partialLine.Length > 0 ? segment : capped);
            _partialLine = "";

            if (isLast)
                break;

            result.Append('\n');
            start = newline + 1;
        }

        return result.ToString();
    }

    /// <summary>Classifies one complete physical line, updates note state, and returns it capped.</summary>
    private string Advance(string line)
    {
        var kind = Step(line);
        var cap = PlantUmlStatementLimits.CapFor(kind);
        return cap is null ? line : PlantUmlStatementLimits.TruncateStatement(line, cap.Value);
    }

    /// <summary>Classifies one complete physical line against the current note state, and advances it.</summary>
    private PlantUmlStatementKind Step(string line)
    {
        var trimmed = line.Trim();

        if (_noteDepth > 0)
        {
            if (NoteEnd().IsMatch(trimmed))
                _noteDepth--;
            return PlantUmlStatementKind.NoteBody;
        }

        if (trimmed.Length == 0)
            return PlantUmlStatementKind.Other;

        if (trimmed[0] == '\'')
            return PlantUmlStatementKind.Comment;

        if (trimmed[0] is '!' or '@')
            return PlantUmlStatementKind.Directive;

        if (NoteStart().IsMatch(trimmed))
        {
            if (!IsSingleLineNote(trimmed))
            {
                _noteDepth++;
                return PlantUmlStatementKind.Note;
            }
            return HasColourTag(trimmed) ? PlantUmlStatementKind.ColouredNoteBar : PlantUmlStatementKind.Note;
        }

        if (BlockOpener().IsMatch(trimmed))
            return PlantUmlStatementKind.BlockOpener;

        var arrow = Arrow().Match(trimmed);
        if (arrow.Success && trimmed.IndexOf(':', arrow.Index + arrow.Length) >= 0)
            return PlantUmlStatementKind.Message;

        return PlantUmlStatementKind.Other;
    }

    /// <summary>
    /// A note statement written on one line — <c>note over a : text</c>, or the step-delimiter bar's
    /// <c>#black:&lt;color:white&gt;label</c>. Stereotypes are stripped first so the <c>&lt;&lt;…&gt;&gt;</c>
    /// in <c>hnote across &lt;&lt;stepDelimiter&gt;&gt;</c> does not hide the separator.
    /// </summary>
    private static bool IsSingleLineNote(string line)
    {
        var stripped = Stereotype().Replace(line, "");
        var colon = stripped.IndexOf(':');
        if (colon < 0) return false;
        var angle = stripped.IndexOf('<');
        return angle < 0 || colon < angle;
    }

    /// <summary>The inline markup whose parser overflows its stack on a long coloured bar.</summary>
    private static bool HasColourTag(string line) =>
        line.Contains("<color:", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("<font", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every physical line of <paramref name="source"/> with the kind the parser's limits give it.</summary>
    public static IEnumerable<(PlantUmlStatementKind Kind, string Line)> ClassifyLines(string source)
    {
        var guard = new PlantUmlStatementGuard();
        foreach (var line in source.Split('\n'))
            yield return (guard.Step(line.TrimEnd('\r')), line);
    }

    [GeneratedRegex(@"^[hrn]?note\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoteStart();

    [GeneratedRegex(@"^end\s*[hrn]?note$", RegexOptions.IgnoreCase)]
    private static partial Regex NoteEnd();

    [GeneratedRegex(@"^(loop|alt|else|opt|group|par|critical|break|partition|also)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BlockOpener();

    /// <summary>
    /// A PlantUML arrow: a run of <c>-</c>/<c>=</c>/<c>.</c> optionally carrying a <c>[#colour]</c> or
    /// style token, with a head at one end. Deliberately loose — this is a backstop, and every emitter
    /// caps its own label first.
    /// </summary>
    [GeneratedRegex(@"<{1,2}[-=.]{1,2}(?:\[[^\]]*\])?[-=.]{0,2}|[-=.]{1,2}(?:\[[^\]]*\])?[-=.]{0,2}>{1,2}")]
    private static partial Regex Arrow();

    [GeneratedRegex(@"<<[^>]*>>")]
    private static partial Regex Stereotype();
}
