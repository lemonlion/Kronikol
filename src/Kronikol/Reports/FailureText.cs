using System.Text.RegularExpressions;
namespace Kronikol.Reports;

/// <summary>
/// Builds the failure text a scenario carries out of whatever pieces a test framework hands an adapter.
///
/// <para>It exists because "no message" and "an empty message" are different facts and every adapter had
/// to get the distinction right on its own. Every consumer downstream — the failures digest, the HTML
/// cluster panel, `kronikol query failures`, the CTRF export — treats a present <c>errorMessage</c> as
/// evidence that something went wrong. An adapter that joins an empty list with a line separator
/// produces <c>"\r\n"</c>, which is not a harmless default: it is a report claiming every passing
/// scenario has something to say about how it failed.</para>
/// </summary>
public static class FailureText
{
    /// <summary>
    /// Joins the messages a framework reported, one per line, and answers <c>null</c> rather than an
    /// empty or whitespace-only string when there was nothing to report.
    ///
    /// <para>Blank entries are dropped rather than joined. That is not tidiness: the digest and the
    /// cluster panel both key a failure on its <em>first line</em>, so a leading empty message would make
    /// the empty string the thing every such failure is grouped by.</para>
    /// </summary>
    public static string? Join(IEnumerable<string?>? lines)
    {
        if (lines is null)
            return null;

        var kept = lines.Where(line => !string.IsNullOrWhiteSpace(line));
        return OrNull(string.Join(Environment.NewLine, kept));
    }

    /// <summary>
    /// The text, or <c>null</c> when it carries nothing — the single rule every failure field follows, so
    /// that "absent" has one representation instead of three.
    /// </summary>
    public static string? OrNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    /// <summary>
    /// The first line of <paramref name="message"/> — what the failures digest and the HTML cluster panel
    /// group a failure by. One implementation, because there were two and they disagreed three ways: on
    /// bare-CR line endings, on empty versus null messages, and on which characters count as whitespace.
    /// Two surfaces of one report that group the same failures differently are worse than either rule.
    /// </summary>
    public static string FirstLine(string? message)
    {
        if (message is null)
            return "";

        var end = message.AsSpan().IndexOfAny('\r', '\n');
        var line = end < 0 ? message : message[..end];
        return CollapseWhitespace(line);
    }

    /// <summary>
    /// Cuts <paramref name="text"/> to at most <paramref name="limit"/> UTF-16 code units, marking the
    /// cut with an ellipsis, and never between the halves of a surrogate pair.
    ///
    /// <para>Every character outside the Basic Multilingual Plane — emoji, much of CJK — is two code
    /// units, so a cut at an arbitrary index can leave a lone surrogate, which is not a character.
    /// <see cref="File.WriteAllText(string,string)"/> encodes UTF-8 with the throwing fallback, so a
    /// digest containing one does not come out mangled: it does not come out. The write throws, the
    /// caller records a diagnostic, and the PREVIOUS run's file stays on disk — stale, plausible, and
    /// describing a different run. Assertion messages and captured third-party payloads are exactly where
    /// an emoji turns up.</para>
    /// </summary>
    public static string Truncate(string text, int limit)
    {
        if (text is null || text.Length <= limit)
            return text ?? "";

        // text[..limit] keeps indices 0..limit-1, so a high surrogate in the last kept position has had
        // its partner excluded. Drop it rather than orphan it; one character short is not a cost anyone
        // can see in a value that is being truncated anyway.
        var cut = limit;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;

        return text[..cut] + "…";
    }

    /// <summary>
    /// The fully qualified test name a stack frame's method belongs to, in the shape
    /// <c>dotnet test --filter "FullyQualifiedName~..."</c> matches: namespace, class and method, with the
    /// compiler's scaffolding removed - the parameter list, an async or iterator state machine
    /// (<c>&lt;Method&gt;d__12.MoveNext</c>), a lambda (<c>&lt;Method&gt;b__0</c>), a local function
    /// (<c>&lt;Method&gt;g__Local|0_0</c>), a display class (<c>&lt;&gt;c__DisplayClass3_0</c>), a generic
    /// arity (<c>Class`1</c>) and type arguments (<c>Method[T]</c>). Null when the frame names nothing a
    /// test runner could select.
    /// </summary>
    /// <remarks>
    /// <para>Measured against a real run before this existed: the name was extractable for 26 of 26
    /// failing scenarios and 0 of 5 passing ones - a passing scenario has no trace - which is why this is
    /// a reader of what every report already carries rather than a new field every adapter would have to
    /// write.</para>
    /// <para>One shape it cannot recover: the runtime prints a nested class with <c>.</c> where the
    /// runner's own name has <c>+</c>, and the frame does not say which segments were classes. A filter
    /// for a test in a nested class may select nothing; shortening it to the class and method is the
    /// reader's move, and the documentation says so.</para>
    /// </remarks>
    public static string? TestFilter(string? frameMethod)
    {
        if (string.IsNullOrWhiteSpace(frameMethod))
            return null;

        var method = frameMethod.Trim();
        var parameters = method.IndexOf('(');
        if (parameters >= 0)
            method = method[..parameters];

        method = Regex.Replace(method, @"`\d+", "");                                   // Class`1
        method = Regex.Replace(method, @"\[[^\]]*\]", "");                             // Method[T]
        method = Regex.Replace(method, @"\.<>c(__DisplayClass\d+_\d+)?(?=\.|$)", "");   // <>c, <>c__DisplayClass3_0
        method = Regex.Replace(method, @"\.MoveNext$", "");                            // the state machine's step

        // <Method>b__0 (lambda), <Method>d__3 (state machine), <Method>g__Local|0_0 (local function);
        // innermost first, so an async lambda's <<Method>b__0>d unwraps to the method too.
        string before;
        do
        {
            before = method;
            method = Regex.Replace(method, @"<([^<>]+)>[a-z](__[^.<>]*)?", "$1");
        } while (method != before);

        method = method.Trim('.');
        return method.Contains('.') && !method.Contains('<') ? method : null;
    }

    /// <summary>
    /// The command that re-runs the test a frame belongs to, or null when <see cref="TestFilter"/> finds
    /// no test in it.
    /// </summary>
    /// <remarks>
    /// <c>~</c> (contains) rather than <c>=</c>: NUnit's fully qualified name carries the arguments of a
    /// parameterised test and xUnit's theory rows share one method, so an exact match would select nothing
    /// on the former and is no better on the latter. This is the VSTest filter grammar; a runner on
    /// Microsoft.Testing.Platform's own <c>dotnet test</c> takes the same name through its own flag.
    /// </remarks>
    public static string? RerunCommand(string? frameMethod) =>
        TestFilter(frameMethod) is { } name ? $"dotnet test --filter \"FullyQualifiedName~{name}\"" : null;

    /// <summary>
    /// The frame a failure was thrown from: the first one in <paramref name="stackTrace"/> that carries a
    /// file and a line, with the method that owns it. <c>null</c> when there is none.
    ///
    /// <para>The first such frame rather than the topmost, because the topmost frames are the assertion
    /// library's and the runtime's, and those assemblies ship without PDBs — so "has source information"
    /// is a good enough proxy for "is code someone in this repository wrote". Emitting a frame without one
    /// would put <c>Xunit.Assert.Equal</c> where the reader expects their own test, which is worse than
    /// the blank this replaces.</para>
    ///
    /// <para>Only the invariant-culture shape is parsed. The runtime localises <c>at</c> and <c>in</c>, so
    /// a translated trace yields null and the digest simply says nothing — the alternative, guessing at a
    /// shape, is how a reader ends up at a file and line that are not where anything happened.</para>
    /// </summary>
    /// <param name="stackTrace">The captured trace.</param>
    /// <param name="preferFile">
    /// The file the scenario was declared in, when a producer reported one. Measured against a real run:
    /// "the first frame with source information" is NOT the caller's code, because xUnit v3 ships
    /// source-linked PDBs, so three <c>Xunit.Assert.Equal</c> frames carry a file and a line before the
    /// test does. Matching the declaring file first is the signal that needs no list; the namespace skip
    /// below is the fallback for when a producer reported no file.
    /// </param>
    public static (string Method, string File, int Line)? ThrownAt(string? stackTrace, string? preferFile = null)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
            return null;

        var frames = ParseFrames(stackTrace);
        if (frames.Count == 0)
            return null;

        if (preferFile is { Length: > 0 })
        {
            var wanted = System.IO.Path.GetFileName(preferFile);
            foreach (var frame in frames)
                if (string.Equals(System.IO.Path.GetFileName(frame.File), wanted, StringComparison.OrdinalIgnoreCase))
                    return frame;
        }

        foreach (var frame in frames)
            if (!IsFrameworkFrame(frame.Method))
                return frame;

        // Everything looked like a framework frame. One of them is still better than nothing: a reader who
        // sees Xunit.Assert.Equal learns the assertion that threw, which is more than a blank line says.
        return frames[0];
    }

    /// <summary>
    /// Namespaces whose frames are never the code someone here wrote. A blocklist is a heuristic and will
    /// be wrong for anyone whose own test namespace starts with one of these — which is why it is the
    /// fallback and the declaring file is tried first.
    /// </summary>
    private static readonly string[] FrameworkNamespaces =
    [
        "Xunit.", "NUnit.", "TUnit.", "MSTest.", "Microsoft.VisualStudio.TestTools.",
        "Shouldly.", "FluentAssertions.", "AwesomeAssertions.", "LightBDD.", "Reqnroll.",
        "TechTalk.SpecFlow.", "TestStack.BDDfy.", "Kronikol.",
        "System.", "Microsoft.", "Castle.", "Moq.", "NSubstitute.",
    ];

    private static bool IsFrameworkFrame(string method) =>
        FrameworkNamespaces.Any(prefix => method.StartsWith(prefix, StringComparison.Ordinal));

    private static List<(string Method, string File, int Line)> ParseFrames(string stackTrace)
    {
        var frames = new List<(string Method, string File, int Line)>();

        foreach (var raw in stackTrace.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("at ", StringComparison.Ordinal))
                continue;

            // Split on the LAST " in ", because a method signature can contain one: a parameter named
            // `in` is impossible, but a generic argument or a local function name is not, and the path is
            // always last. Same reasoning for `:line ` — a Windows path has a colon of its own.
            var marker = line.LastIndexOf(" in ", StringComparison.Ordinal);
            if (marker < 0)
                continue;

            var method = line[3..marker].Trim();
            var rest = line[(marker + 4)..];

            var lineMarker = rest.LastIndexOf(":line ", StringComparison.Ordinal);
            if (lineMarker < 0)
                continue;

            if (!int.TryParse(rest[(lineMarker + 6)..].Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
                continue;

            var file = rest[..lineMarker].Trim();
            if (method.Length == 0 || file.Length == 0)
                continue;

            frames.Add((method, file, number));
        }

        return frames;
    }

    /// <summary>
    /// Runs of whitespace folded to one space, and the ends trimmed. <see cref="char.IsWhiteSpace(char)"/>
    /// rather than a regex, so that every Unicode separator counts — a key that treats U+00A0 as text on
    /// one surface and as a space on the other splits a cluster in half for no reason a reader can see.
    /// </summary>
    public static string CollapseWhitespace(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');
            pendingSpace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }
}
