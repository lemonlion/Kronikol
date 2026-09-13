using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Reports;

/// <summary>
/// Extension methods for writing strings into YAML output.
/// </summary>
public static partial class YamlExtensions
{
    /// <summary>
    /// Rewrites a string so it cannot disturb YAML structure, by deleting the characters that could.
    /// </summary>
    /// <remarks>
    /// <para>Superseded by <see cref="ToYamlScalar"/>, which quotes rather than rewrites. This method
    /// does not escape anything: it substitutes. <c>[</c> becomes <c>&lt;</c>, <c>: </c> becomes
    /// <c> = </c>, <c>{</c> becomes <c>(</c>, and a captured JSON body of
    /// <c>{"item":"Widget"}</c> lands in the report as <c>("item":"Widget")</c> — not the body that went
    /// over the wire. It also does nothing at all about newlines, which are what actually break the
    /// document: a value containing one is written raw, the second line starts at column 0, and the file
    /// stops being YAML.</para>
    ///
    /// <para>Kept because it is public and someone may be calling it; nothing inside Kronikol does.</para>
    /// </remarks>
    [Obsolete("Mutilates the value rather than escaping it, and does not handle newlines. Use ToYamlScalar.")]
    public static string SanitiseForYml(this string value)
    {
        return value
            .Replace("[", "<")
            .Replace("]", ">")
            .Replace(": ", " = ")
            .Replace("#", "(hash)")
            .Replace("&", "(and)")
            .Replace("*", "(star)")
            .Replace("{", "(")
            .Replace("}", ")")
            .Replace("!", "(bang)")
            .Replace("%", "(pct)")
            .Replace("@", "(at)")
            .Replace("`", "'")
            .Replace("|", "(pipe)");
    }

    /// <summary>
    /// Renders <paramref name="value"/> as a YAML scalar that a parser reads back as exactly that string.
    /// </summary>
    /// <param name="value">The string to render. Never null — a null belongs to the caller, which writes an empty scalar.</param>
    /// <param name="blockIndent">
    /// The column a block scalar's lines are written at, as a run of spaces. Pass the full prefix the
    /// scalar is being appended after (<c>"        ErrorMessage: "</c>), so the block is indented deeper
    /// than the mapping that holds it, which is what makes it a block rather than the next key.
    /// </param>
    /// <remarks>
    /// <para>Three forms, chosen by what the value contains, because the choice is what keeps the file
    /// both correct and readable:</para>
    /// <list type="bullet">
    /// <item>plain, when nothing in the value can be misread — the common case, and the reason a YAML
    /// report still looks like one;</item>
    /// <item>a literal block (<c>|</c>) when the value spans lines, so a stack trace or a request body
    /// stays legible instead of becoming one long run of <c>\n</c>;</item>
    /// <item>double quotes otherwise, which can represent anything.</item>
    /// </list>
    ///
    /// <para>"Can be misread" is deliberately generous. It rejects the YAML 1.1 booleans (<c>yes</c>,
    /// <c>no</c>, <c>on</c>, <c>off</c>) and timestamps as well as the YAML 1.2 core set, because the
    /// version a consumer's parser implements is not ours to choose, and a scenario named <c>No</c>
    /// coming back as <c>false</c> is the kind of defect that is found years later in somebody else's
    /// pipeline. Quoting a string that did not strictly need it costs two characters.</para>
    /// </remarks>
    internal static string ToYamlScalar(this string value, string blockIndent)
    {
        if (value.Length == 0)
            return "\"\"";

        if (value.Contains('\n') && IsBlockSafe(value))
            return LiteralBlock(value, blockIndent);

        return IsPlainSafe(value) ? value : DoubleQuoted(value);
    }

    /// <summary>
    /// A literal block preserves the value's lines as lines. It can only do that when every line survives
    /// being re-indented, which rules out a first line that is already indented (the reader cannot tell
    /// that indentation from the block's own without an explicit indicator), a carriage return (block
    /// scalars normalise line breaks, so a <c>\r\n</c> would come back as <c>\n</c>), and any other
    /// control character, which has no representation here at all.
    /// </summary>
    private static bool IsBlockSafe(string value)
    {
        if (value[0] is ' ' or '\t' || value.Contains('\r'))
            return false;

        foreach (var c in value)
            if (char.IsControl(c) && c != '\n' && c != '\t')
                return false;

        return true;
    }

    private static string LiteralBlock(string value, string blockIndent)
    {
        // Chomping says what happens to the line breaks at the end: `-` strips them, no indicator keeps
        // one, `+` keeps all of them. Getting this wrong silently adds or drops a trailing newline.
        var trailingNewlines = 0;
        while (trailingNewlines < value.Length && value[^(trailingNewlines + 1)] == '\n')
            trailingNewlines++;

        var header = trailingNewlines switch { 0 => "|-", 1 => "|", _ => "|+" };
        var body = value[..(value.Length - trailingNewlines)];

        var builder = new StringBuilder(header);
        foreach (var line in body.Split('\n'))
        {
            builder.Append('\n');
            // A blank line gets no indent: trailing whitespace on an otherwise empty line is invisible
            // in the file and means nothing to the parser.
            if (line.Length > 0)
                builder.Append(blockIndent).Append(line);
        }

        for (var i = 1; i < trailingNewlines; i++)
            builder.Append('\n');

        return builder.ToString();
    }

    private static string DoubleQuoted(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                        builder.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    else
                        builder.Append(c);
                    break;
            }
        }
        return builder.Append('"').ToString();
    }

    /// <summary>
    /// Whether the value can be written with no quotes at all and still read back as itself.
    /// </summary>
    private static bool IsPlainSafe(string value)
    {
        if (value != value.Trim())
            return false;

        // Leading indicators. Some of these ( - ? : ) only start something when a space follows, but a
        // value that begins with one is rare enough that the distinction is not worth the risk.
        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0]))
            return false;

        // `: ` opens a mapping and ` #` opens a comment, wherever they appear; a trailing colon does too.
        if (value.Contains(": ") || value.Contains(" #") || value.EndsWith(':'))
            return false;

        foreach (var c in value)
            if (char.IsControl(c))
                return false;

        return !ResolvesToSomethingOtherThanAString(value);
    }

    /// <summary>
    /// Whether a parser would hand this plain scalar back as a null, a boolean, a number or a timestamp
    /// rather than as the string it was written from.
    /// </summary>
    private static bool ResolvesToSomethingOtherThanAString(string value)
    {
        switch (value)
        {
            case "~" or "null" or "Null" or "NULL":
            case "true" or "True" or "TRUE" or "false" or "False" or "FALSE":
            // YAML 1.1 only, but plenty of parsers still implement it.
            case "yes" or "Yes" or "YES" or "no" or "No" or "NO":
            case "on" or "On" or "ON" or "off" or "Off" or "OFF":
            case "y" or "Y" or "n" or "N":
            case ".inf" or "-.inf" or "+.inf" or ".Inf" or ".INF" or ".nan" or ".NaN" or ".NAN":
                return true;
        }

        return NumberLike().IsMatch(value) || TimestampLike().IsMatch(value);
    }

    /// <summary>Decimal, hexadecimal, octal and YAML 1.1 sexagesimal integers, and floats.</summary>
    [GeneratedRegex(@"^[-+]?(\d[\d_]*(:[0-5]?\d)*|0x[0-9a-fA-F_]+|0o?[0-7_]+)$|^[-+]?(\.\d+|\d[\d_]*(\.[\d_]*)?)([eE][-+]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberLike();

    /// <summary>A date, or a date and time — both of which YAML 1.1 resolves to a timestamp.</summary>
    [GeneratedRegex(@"^\d{4}-\d{1,2}-\d{1,2}([Tt ].*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampLike();
}
