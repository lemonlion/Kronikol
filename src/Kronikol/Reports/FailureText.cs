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
