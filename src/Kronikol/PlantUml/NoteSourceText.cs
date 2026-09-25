using System.Globalization;
using System.Text.RegularExpressions;

namespace Kronikol.PlantUml;

/// <summary>
/// A note's text as the report draws it, read back from its PlantUML source (3.30.2, for <c>kronikol query</c>):
/// the header tag a line starts with removed, the lines the width bound cut joined again, and the escapes the
/// generator writes decoded in one pass from left to right, <c>~X</c> and, from 3.30.1, <c>&lt;U+hhhh&gt;</c>. A
/// zero-width space draws nothing and is dropped; a code point that is no character is kept as written. The
/// report's copy path does the same (<c>rejoinWrappedNoteLines</c>, then <c>decodeNoteEscapes</c>).
/// </summary>
internal static partial class NoteSourceText
{
    internal static string Decode(string noteSource)
    {
        if (string.IsNullOrEmpty(noteSource)) return noteSource;

        var lines = noteSource.ReplaceLineEndings("\n").Split('\n').Select(l => HeaderTag().Replace(l, ""));
        var joined = DiagramWidth.RejoinMarkedLines(string.Join("\n", lines));
        return Escape().Replace(joined, m =>
        {
            if (m.Groups[1].Success) return m.Groups[1].Value;
            var codePoint = int.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (codePoint == 0x200B) return "";
            if (codePoint is >= 0xD800 and <= 0xDFFF || codePoint > 0x10FFFF) return m.Value;
            return char.ConvertFromUtf32(codePoint);
        });
    }

    /// <summary>The header tag, as the report's scripts read it (<c>NOTE_HEADER_TAG_INDENTED</c>).</summary>
    [GeneratedRegex(@"^\s*<color:(?:gray|#[0-9A-Fa-f]{6})>")]
    private static partial Regex HeaderTag();

    /// <summary>The scripts' <c>NOTE_ESCAPE</c>: a creole escape, or a code point.</summary>
    [GeneratedRegex(@"~([/*_\-""\[<#=])|<U\+([0-9A-Fa-f]{4,6})>")]
    private static partial Regex Escape();
}
