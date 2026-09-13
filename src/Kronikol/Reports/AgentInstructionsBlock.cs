namespace Kronikol.Reports;

/// <summary>
/// The marker protocol for <c>CLAUDE.md</c> and <c>AGENTS.md</c>: Kronikol owns the text between two
/// markers and nothing else in the file.
///
/// <para>It exists here, in the library, because <b>two</b> things write those two file names and only one
/// of them was careful. <c>kronikol init-agents</c> has always spliced — its help promises that re-running
/// it replaces its own block and leaves the rest alone — while report generation wrote the same names with
/// a plain overwrite. Wherever the two land in one directory (a reports folder configured at a repository
/// root, or <c>init-agents</c> pointed at a reports folder) a run destroyed whatever a human had written
/// there. And once generation had replaced the file with an unmarked body, a later <c>init-agents</c>
/// appended its block to that body instead of replacing anything — so the repository's standing
/// instructions ended up carrying one run's report notes as though they were the repository's own.</para>
///
/// <para>One protocol, one implementation, both callers. The tool keeps the byte-level concerns that are
/// its own — BOM preservation, refusing a file that is not valid UTF-8 — and asks this for the text.</para>
/// </summary>
public static class AgentInstructionsBlock
{
    /// <summary>Opens the region Kronikol owns. Only counts on a line of its own.</summary>
    public const string BeginMarker = "<!-- kronikol:begin -->";

    /// <summary>Closes the region Kronikol owns. Only counts on a line of its own.</summary>
    public const string EndMarker = "<!-- kronikol:end -->";

    /// <summary>The block as it is written into a file: the body between the two markers.</summary>
    public static string Wrap(string body) =>
        BeginMarker + "\n" + body.TrimEnd('\r', '\n') + "\n" + EndMarker;

    /// <summary>
    /// <paramref name="existing"/> with <paramref name="block"/> put in place of the region Kronikol owns,
    /// or appended when the file has no such region. <c>null</c> with a <paramref name="problem"/> when the
    /// file cannot be merged into safely — which is always a refusal rather than a guess, because every
    /// repair for a half-marked file deletes something.
    /// </summary>
    public static string? Merge(string existing, string block, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(block);

        problem = null;

        var crlf = existing.Contains("\r\n", StringComparison.Ordinal);
        var newLine = crlf ? "\r\n" : "\n";
        var text = block.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (crlf)
            text = text.Replace("\n", "\r\n", StringComparison.Ordinal);

        var (start, end, count) = FindRegion(existing);

        if (count > 1)
        {
            problem = "contains the Kronikol block twice. Upgrading only the first would leave the "
                      + "second - possibly written by a much older tool - as the last word an agent reads. "
                      + "Delete the one you do not want, then run this again.";
            return null;
        }

        string merged;
        if (start >= 0 && end > start)
        {
            merged = existing[..start] + text.TrimEnd('\r', '\n') + existing[end..];
        }
        else if (start >= 0)
        {
            problem = $"opens a Kronikol block ({BeginMarker}) and never closes it. Every repair for "
                      + "that is a guess, and a wrong guess deletes the rest of the file. Add a "
                      + $"{EndMarker} line where the block ends, or delete the opening line.";
            return null;
        }
        else
        {
            var head = existing.TrimEnd('\r', '\n');
            merged = head.Length == 0 ? text : head + newLine + newLine + text;
        }

        if (!merged.EndsWith(newLine, StringComparison.Ordinal))
            merged += newLine;

        return merged;
    }

    /// <summary>
    /// The half-open character range of the managed region, or <c>(-1, -1)</c> when there is none.
    ///
    /// <para>A marker counts only on a line of its own. The wiki, the README and the command's own help all
    /// quote the marker strings, so a repository whose instructions document this feature will contain them
    /// in prose - and treating a sentence about the block as the block itself would delete whatever came
    /// after it. Only the first region is considered: replacing the least text that can be right is the
    /// safe reading when a file somehow holds two.</para>
    /// </summary>
    public static (int Start, int End, int Count) FindRegion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var start = -1;
        var end = -1;
        var count = 0;
        var offset = 0;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed == BeginMarker)
            {
                count++;
                if (start < 0) start = offset;
            }
            else if (start >= 0 && end < 0 && trimmed == EndMarker)
            {
                // Just past the marker text, not past the line: the CR of a CRLF break belongs to the
                // tail that gets kept, or the replacement would leave a bare LF in a CRLF file.
                end = offset + line.TrimEnd().Length;
            }

            offset += line.Length + 1;
        }

        return (start, end, count);
    }
}
