using System.Text;

namespace Kronikol.Tool.Query;

/// <summary>
/// Writes a command's answer under a byte budget.
///
/// <para>The budget is the whole point of the tool, so it is enforced here rather than trusted to each
/// command. Two rules follow from it. Truncation always announces itself, with the exact flags that
/// resume — an agent that cannot tell whether it saw everything has to go back to reading the file, which
/// is the failure this exists to prevent. And nothing is ever silently dropped: where a payload would go,
/// a pointer goes instead, saying how big it is and what to ask for.</para>
/// </summary>
internal sealed class QueryWriter(TextWriter output, int maxBytes)
{
    private readonly StringBuilder _buffer = new();
    private int _bytes;
    private bool _overBudget;
    private string? _footer;

    public bool Truncated => _overBudget;

    public void Line(string text = "")
    {
        if (_overBudget)
            return;

        var cost = Encoding.UTF8.GetByteCount(text) + 1;
        if (maxBytes > 0 && _bytes + cost > maxBytes)
        {
            _overBudget = true;
            return;
        }

        _buffer.Append(text).Append('\n');
        _bytes += cost;
    }

    /// <summary>
    /// The line printed after the body of the answer whatever happens — how much was shown, and the exact
    /// re-run that shows the next part.
    /// </summary>
    public void Footer(string text) => _footer = text;

    public void Flush()
    {
        output.Write(_buffer.ToString());

        if (_overBudget)
            output.Write("… output truncated at " + maxBytes + " bytes · raise with --max-bytes, or filter harder\n");

        if (_footer is not null)
            output.Write(_footer + "\n");
    }

    /// <summary>
    /// Renders a paged listing and its footer in one place, so no command can page without saying so.
    /// </summary>
    public void Page<T>(IReadOnlyList<T> all, int offset, int limit, string noun, Action<T> render, string? rerunPrefix = null)
    {
        var shown = 0;
        for (var i = offset; i < all.Count && shown < limit; i++, shown++)
        {
            render(all[i]);
            if (Truncated)
                break;
        }

        var last = offset + shown;
        Footer(last >= all.Count && offset == 0
            ? $"{all.Count} {noun}"
            : $"{noun}: {offset + 1}-{last} of {all.Count} · next: {rerunPrefix}--offset {last}");
    }

    /// <summary>
    /// Formats a size the way the elision markers do. Bytes below a kilobyte, kilobytes above — precision
    /// past that is noise in a decision about whether to fetch something.
    /// </summary>
    public static string Size(int bytes) =>
        bytes < 1024 ? bytes + " B" : (bytes / 1024.0).ToString("0.#") + " KB";

    public static string Duration(double? ms) =>
        ms is null ? "" : ms < 1000 ? $"{ms:0} ms" : $"{ms / 1000:0.##} s";

    /// <summary>One line of text with its newlines flattened, so a listing stays one row per item.</summary>
    public static string OneLine(string? text, int max = 160)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var flat = text.ReplaceLineEndings(" ").Trim();
        while (flat.Contains("  ", StringComparison.Ordinal))
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
