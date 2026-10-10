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

    /// <summary>A fragment opener carrying a label — <c>loop</c>, <c>alt</c>, <c>partition</c>. Capped, lower, at <see cref="PlantUmlStatementLimits.MaxBlockLabelChars"/>, the render worker's stack edge.</summary>
    BlockOpener,

    /// <summary>
    /// A one-line note carrying a colour or font tag — the step-delimiter bar
    /// <c>hnote across &lt;&lt;stepDelimiter&gt;&gt; #black:&lt;color:white&gt;…</c>. Capped at
    /// <see cref="PlantUmlStatementLimits.MaxColouredNoteBarChars"/>, the render worker's stack edge.
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

    /// <summary>
    /// Anything else — participants, <c>skinparam</c>, <c>autonumber</c>, blank lines. Left alone: a participant's name
    /// and alias are capped where they are written (<see cref="PlantUmlStatementLimits.MaxParticipantNameChars"/>),
    /// since cutting a declaration's line would cut its alias.
    /// </summary>
    Other
}

/// <summary>
/// The statement-length limits PlantUML's parser enforces, measured against the engine Kronikol ships
/// (originally <c>lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched</c>,
/// re-verified against the <c>@v1.2026.6-patched</c> build, the stock <c>@v1.2026.8beta1-0e4f452</c>
/// build, and the published <c>@plantuml/core@1.2026.8</c> that replaced it, where every pin below holds and
/// every edge sits within a few characters of the build before: plans/ENGINE_PIN_PLAN.md §1.11). They are per statement
/// kind, not one global line limit, and they fail in two different ways — neither of which says
/// "too long":
/// <list type="bullet">
/// <item><description>an over-long <b>message</b> matches no rule, so the parser abandons the diagram
/// and the engine draws <c>Syntax Error?</c> over the whole fragment (or silently draws the wrong
/// diagram when the class-parse fallback succeeds);</description></item>
/// <item><description>everything else is the engine's stack. Its regex library (TeaVM's port of
/// <c>java.util.regex</c>) walks a bracket class under <c>+</c> or <c>*</c>, and a lazy <c>.*?</c>, once per
/// character on the JavaScript stack, and PlantUML runs such patterns over a <b>coloured note bar</b>, a
/// <b>block opener</b>, a <b>link</b>'s text, a <b>component edge</b>'s label, a <b>participant's name</b> and
/// alias, and an <b>activity action</b> and swimlane. Past its length the engine draws its RangeError picture or
/// text, or <c>Syntax Error?</c> where the overflow is swallowed while a command is chosen, in place of the whole
/// diagram. The JVM does not recurse there, so Java PlantUML draws all of them at 100,000 characters
/// (plans/LONG_COMPONENT_EDGE_PLAN.md §2.3).</description></item>
/// </list>
/// <para>
/// A stack edge is a budget, so it binds where the stack is smallest: the Web Worker <c>BrowserJs</c> renders in,
/// whose stack is half the page's, with V8's optimizing compilers off (as an enterprise policy or a browser's
/// security mode can set), where every frame is the interpreter's size. Until 4.14.5 the block-opener and
/// coloured-bar caps came from node, whose stack is the page's, and both failed in a cold worker with the JIT on,
/// the default. Each constant keeps a quarter under the lowest figure found, so that a runtime with a smaller stack
/// does not reopen the bug.
/// </para>
/// <list type="table">
/// <item><term><c>a -&gt; b: …</c>, <c>a --&gt; b: …</c>, <c>a -[#F39C12]&gt; b: …</c></term><description>2000 — exactly, on every build measured, and on the
/// whole statement: a 27-character prefix leaves a 1973-character label, not a longer statement. A parse limit,
/// so the same in every runtime.</description></item>
/// <item><term><c>loop</c>, <c>partition</c> openers, the whole statement, in the worker: 840 with the optimizing
/// compilers off, 1,010 cold with the JIT on (node 25.9: about 2,000; 1.2026.6's parse limits were 1476 to
/// 1484)</term><description>constant 600 (1471 until 4.14.5)</description></item>
/// <item><term><c>hnote across … #black:&lt;color:white&gt;…</c>, in the worker: 880 with the optimizing compilers
/// off, 1,040 cold with the JIT on (node 25.9: 2005 to 2008)</term><description>constant 600 (1400 until 4.14.5)</description></item>
/// <item><term>note bodies 16371, <c>note over a : …</c> 16392, plain <c>hnote across</c> 16398
/// (16370/16377/16376 on 1.2026.8beta1 — unchanged; a placeholder note's line draws at 15,900 in the worker with
/// the optimizing compilers off)</term><description>constant 16000</description></item>
/// <item><term>the text inside a <c>[[#iflow-…]]</c> link, in the worker: 980 cold, 475 to 495 with the
/// optimizing compilers off</term><description>constant 350</description></item>
/// <item><term>a component diagram edge's label, as written, in the worker: 550 with the optimizing compilers
/// off, 1,910 cold with the JIT on (Chromium 147), 1,620 warm (the issue's platform: 516)</term><description>constant 375</description></item>
/// <item><term>a participant's name, in the worker with the optimizing compilers off: 280 in a sequence diagram,
/// 310 and 340 in a component diagram, 540 as an activity swimlane</term><description>constant 200</description></item>
/// <item><term>an activity diagram's action, in the worker: 820 with the optimizing compilers off, 990 cold with
/// the JIT on</term><description>constant 600</description></item>
/// </list>
/// <para>
/// The worker figures are the first length that failed, measured on 2026-10-10 with Kronikol's own emitters in
/// Chromium 147 and Chrome 154 on Windows and in Chromium 147 on Linux, which agree with the optimizing compilers
/// off (tools/render-bench/results/statement-limits-worker-2026-10-10.txt). Firefox's worker fails later, and
/// WebKit, which renders on the page's main thread where it has no OffscreenCanvas, later still.
/// </para>
/// <para>
/// Leading and trailing whitespace is not counted — a valid short arrow padded to 2500 characters with
/// trailing spaces parses — so every cap applies to the trimmed statement.
/// </para>
/// <para>
/// <b>Which limits are PlantUML's and which are the JS build's.</b> Measured against real Java PlantUML
/// through IKVM (see <c>IkvmStatementLimitTests</c>): the <b>2,000-character message limit is PlantUML's
/// own</b> — Java refuses 2,001 exactly as the JS build does — while every stack edge above is an artifact of
/// the TeaVM build, which Java draws far past. Every cap is applied where the source is written rather than where
/// a renderer is chosen, because the same source may be rendered either way.
/// </para>
/// <para>
/// Measuring this is easy to get wrong: when a message statement is too long the parser falls back to
/// reading the source as a <em>class</em> diagram, and that fallback often succeeds — echoing the label
/// text and emitting no <c>Syntax Error</c> banner. "Is the label in the SVG?" therefore passes for both
/// outcomes. The signal that separates them is that a sequence diagram draws each participant twice, as
/// a head box and a foot box. And the engine's error pictures are SVGs that list the source, so "an SVG holding
/// the names" passes too: a probe has to reject a picture whose first drawn line starts <c>PlantUML </c>, as the
/// worker probe did not until 4.14.5.
/// </para>
/// </summary>
internal static class PlantUmlStatementLimits
{
    /// <summary>Longest message/arrow statement the parser accepts. Measured at exactly 2000; 2001 fails.</summary>
    public const int MaxMessageStatementChars = 2000;

    /// <summary>
    /// Longest <c>loop</c>/<c>alt</c>-style block opener, the whole statement. The engine's regex library walks the
    /// label once per character on its stack, and in the render worker, whose stack is half the page's, a
    /// <c>loop</c> or <c>partition</c> opener drew <c>Syntax Error?</c> from 840 characters with V8's optimizing
    /// compilers off and from 1,010 in a cold worker with the JIT on, the default (plans/LONG_COMPONENT_EDGE_PLAN.md R3:
    /// Chromium 147 and Chrome 154, Windows and Linux; Firefox's worker from 1,070). Until 4.14.5 the constant was
    /// 1471, from the parse limits of 1.2026.6 and node, whose stack is the page's.
    /// </summary>
    public const int MaxBlockLabelChars = 600;

    /// <summary>
    /// Longest one-line note carrying a colour tag: the step bar a step written as one unbroken token takes
    /// (<c>hnote across &lt;&lt;stepDelimiter&gt;&gt; #black:&lt;color:white&gt;…</c>; a step with spaces takes the
    /// styled form, which draws to 13,600). Past it the engine overflows its stack in the colour tag's pattern and
    /// draws its RangeError text in place of the diagram: in the render worker from 880 characters with V8's
    /// optimizing compilers off and from 1,040 in a cold worker with the JIT on, the default
    /// (plans/LONG_COMPONENT_EDGE_PLAN.md R3; Firefox's worker from 1,250). Until 4.14.5 the constant was 1400, from
    /// node, whose stack is the page's. The same bar <em>without</em> a colour tag runs to 16398.
    /// </summary>
    public const int MaxColouredNoteBarChars = 600;

    /// <summary>
    /// Longest participant name a diagram shows: a sequence diagram's participant, a component diagram's node, an
    /// activity diagram's swimlane. The engine walks a declared name, and the alias derived from it, once per
    /// character on its stack, and in the render worker with V8's optimizing compilers off a sequence diagram drew
    /// <c>Syntax Error?</c> from 280 characters of name, a component diagram from 310 (a <c>&lt;&lt;system&gt;&gt;</c>
    /// rectangle) and 340 (a database), and an activity diagram from 540 (a swimlane); cold with the JIT on, from 540,
    /// 690, 730 and 1,000 (plans/LONG_COMPONENT_EDGE_PLAN.md R3). A longer name is cut with <see cref="TruncateLabel"/>,
    /// and its alias takes a hash of the whole name (<see cref="AliasSource"/>), so two names that share their start
    /// stay two participants.
    /// </summary>
    public const int MaxParticipantNameChars = 200;

    /// <summary>
    /// Longest action in the internal-flow activity diagram, <c>:span name (12ms);</c>, measured as the name is
    /// written (escaped and wrapped). Instrumentations name a span after a whole SQL statement, and in the render
    /// worker with V8's optimizing compilers off the engine drew <c>Syntax Error?</c> from 820 characters of name, and
    /// from 990 cold with the JIT on (plans/LONG_COMPONENT_EDGE_PLAN.md R3).
    /// </summary>
    public const int MaxActivityActionChars = 600;

    /// <summary>
    /// Ceiling for note content of any kind. Measured at 16371–16398; a pure backstop, since Kronikol
    /// chunks note values long before this.
    /// </summary>
    public const int MaxNoteLineChars = 16000;

    /// <summary>
    /// Longest text inside a <c>[[…]]</c> link: the request arrow's internal-flow link
    /// <c>[[#iflow-&lt;id&gt; text]]</c> and the component diagram edge's <c>[[#iflow-rel-… text]]</c>, measured
    /// as written (escapes and display breaks count). BrowserJs renders in a Chromium worker, whose stack is
    /// smaller than the page's or node's, and there the engine overflows it parsing a long link:
    /// <c>RangeError: Maximum call stack size exceeded</c> takes the place of the whole diagram. Measured in
    /// Chromium 147 on both engine builds (plans/ENGINE_PIN_PLAN.md §10): from 980 characters in a worker whose
    /// JIT has not warmed up, 1,575 in one that has, and 475 (a component edge) to 495 (a request) with V8's
    /// optimizing compilers off, as a browser runs them when a policy or a security mode turns them off. In a sequence
    /// diagram the same text unlinked draws at every length to the message limit, and the page's main thread, node,
    /// Firefox and WebKit draw every length measured. A component diagram's edge is another matter: the engine walks
    /// its whole label, link or not, so <see cref="MaxComponentEdgeLabelChars"/> binds there and this cap never does
    /// (until 4.14.5 this said the unlinked edge drew to the message limit; it failed from 550). The constant keeps a
    /// quarter under the lowest edge.
    /// </summary>
    public const int MaxLinkedLabelChars = 350;

    /// <summary>
    /// Longest label of a component diagram edge, <c>caller -[#colour]-&gt; service : "label"</c> (and C4's
    /// <c>Rel(…)</c>), measured as written: display breaks and escapes count, the quotes and the aliases do not.
    /// The engine reads every edge label through a pattern its regex library walks once per character on the
    /// stack, link or no link, so a long method list runs a render worker out of stack and the engine draws a
    /// stack-overflow picture in place of the whole diagram (#162). Measured in the worker with V8's optimizing
    /// compilers off, cold and warm, on Windows and Linux, in Chromium 147 and Chrome 154: 540 characters draw and
    /// 550 fail (plans/LONG_COMPONENT_EDGE_PLAN.md R1; the issue's own figures were 506 and 516). With the JIT on a
    /// cold worker draws to 1,900. The constant keeps a quarter under the issue's figure.
    /// </summary>
    public const int MaxComponentEdgeLabelChars = 375;

    /// <summary>Appended where a statement is cut, so a reader can tell truncation from a short value.</summary>
    public const string TruncationMarker = "…";

    /// <summary>A participant's name as a diagram shows it: cut to <see cref="MaxParticipantNameChars"/>, marker included.</summary>
    public static string CapName(string name) => TruncateLabel(name, MaxParticipantNameChars);

    /// <summary>
    /// What a participant's alias is derived from: the name itself when it fits under
    /// <see cref="MaxParticipantNameChars"/>, else its start followed by a hash of the whole name, so that two names
    /// sharing their first <see cref="MaxParticipantNameChars"/> characters, which <see cref="CapName"/> shows alike,
    /// stay two participants.
    /// </summary>
    public static string AliasSource(string name) =>
        name.Length <= MaxParticipantNameChars ? name : string.Concat(name.AsSpan(0, MaxParticipantNameChars - 9), "_", StableHash(name));

    /// <summary>
    /// Eight hex digits of FNV-1a over the name's UTF-16 code units: the same on every runtime and in every process,
    /// as an alias that lands in a golden file or a merged report has to be.
    /// </summary>
    private static string StableHash(string text)
    {
        var hash = 0x811C9DC5u;
        foreach (var c in text)
            hash = (hash ^ c) * 0x01000193u;
        return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

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

        // Never inside a code point escape, `<U+hhhh>` (at most ten characters): half of one paints as text.
        for (var p = cut - 1; p >= 0 && p >= cut - 10; p--)
        {
            if (label[p] == '>') break;
            if (label[p] != '<') continue;
            if (label.AsSpan(p).StartsWith("<U+", StringComparison.Ordinal)) cut = p;
            break;
        }

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
