using System.Globalization;
using System.Text.RegularExpressions;

namespace Kronikol.PlantUml;

/// <summary>
/// A note's text as the report draws it, read back from its PlantUML source (3.30.2, for <c>kronikol query</c>):
/// the header tag a line starts with removed, a key line's label as its word, the emphasis Kronikol wraps a whole line in
/// (FocusFields', a failed answer's reason ink) and a form body's divider tags taken off, the lines the width bound cut
/// joined again, and the escapes the generator writes decoded in one pass from left to right, <c>~X</c> and, from
/// 3.30.1, <c>&lt;U+hhhh&gt;</c>. A zero-width space draws nothing and is dropped; a code point that is no character is
/// kept as written. The report's copy path does the same (<c>noteLinesToText</c>: <c>liftEmphasisOverJoins</c>,
/// <c>rejoinWrappedNoteLines</c>, <c>_stripNoteEmphasis</c>, then <c>decodeNoteEscapes</c>).
/// </summary>
internal static partial class NoteSourceText
{
    internal static string Decode(string noteSource)
    {
        if (string.IsNullOrEmpty(noteSource)) return noteSource;

        // A payload's '<' is escaped, so a tag in note source is Kronikol's. The rejoin lifts emphasis over a cut run's
        // pieces first, so one pair is left around the joined line, which then comes off with the rest.
        var lines = noteSource.ReplaceLineEndings("\n").Split('\n')
            .Select(l => KeyLabel().Replace(HeaderTag().Replace(l, ""), "$1Key "));
        var joined = string.Join("\n", DiagramWidth.RejoinMarkedLines(string.Join("\n", lines)).Split('\n')
            .Select(l => FontTag().Replace(WithoutEmphasis(l), "")));
        return Escape().Replace(joined, m =>
        {
            if (m.Groups[1].Success) return m.Groups[1].Value;
            var codePoint = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (codePoint == 0x200B) return "";
            if (codePoint is >= 0xD800 and <= 0xDFFF || codePoint > 0x10FFFF) return m.Value;
            return char.ConvertFromUtf32(codePoint);
        });
    }

    // A line's content without the tags that open it and close at its end, its indent kept (the scripts' peelEmphasis).
    private static string WithoutEmphasis(string line)
    {
        var indent = 0;
        while (indent < line.Length && char.IsWhiteSpace(line[indent])) indent++;
        var content = line[indent..];
        while (EmphasisOpen().Match(content) is { Success: true } open)
        {
            var close = "</" + open.Groups[1].Value.Split(':')[0] + ">";
            if (content.Length < open.Length + close.Length || !content.EndsWith(close, StringComparison.Ordinal)) break;
            content = content[open.Length..^close.Length];
        }
        return line[..indent] + content;
    }

    /// <summary>The scripts' <c>NOTE_EMPHASIS_OPEN</c>.</summary>
    [GeneratedRegex(@"^<(b|i|u|color:[^>]+|size:\d+)>")]
    private static partial Regex EmphasisOpen();

    /// <summary>The scripts' <c>NOTE_KEY_LINE_INDENTED</c>, a message's key line (<see cref="PlantUmlCreator.NoteKeyLabel"/>).</summary>
    [GeneratedRegex(@"^(\s*)<b>Key</b> ")]
    private static partial Regex KeyLabel();

    /// <summary>The scripts' <c>NOTE_FONT_TAG</c>: a form body's divider.</summary>
    [GeneratedRegex(@"</?font\b[^>]*>")]
    private static partial Regex FontTag();

    /// <summary>The header tag, as the report's scripts read it (<c>NOTE_HEADER_TAG_INDENTED</c>).</summary>
    [GeneratedRegex(@"^\s*<color:(?:gray|#[0-9A-Fa-f]{6})>")]
    private static partial Regex HeaderTag();

    /// <summary>The scripts' <c>NOTE_ESCAPE</c>: a creole escape, or a code point.</summary>
    [GeneratedRegex(@"~([/*_\-""\[<#=])|<U\+([0-9A-Fa-f]{4,6})>")]
    private static partial Regex Escape();
}
