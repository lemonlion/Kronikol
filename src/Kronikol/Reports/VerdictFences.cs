using System.Text;

namespace Kronikol.Reports;

/// <summary>
/// The fenced <c>$verdict</c> search lines of the report scripts (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md section
/// 3.5). A report that draws verdict attributes ships the scripts with the fenced lines; any other report ships them
/// as they were before cross-run history, which for <c>advanced-search.js</c> and
/// <c>report-scenario-feature-map-helper.js</c> are the bytes Kronikol4J ships. One source per script, no copies.
/// </summary>
/// <remarks>
/// Each marker is a whole line at column 0, so the raw file stays valid JavaScript (the verdict code, and comments),
/// which is what the Jint tests load:
/// <code>
/// // kron:verdicts     the lines up to the else or the close ship only with verdicts
/// // kron:else         optional: each line after it is written //| and the line, and ships only without verdicts
/// // kron:/verdicts    closes the block
/// </code>
/// </remarks>
internal static class VerdictFences
{
    internal const string Open = "// kron:verdicts";
    internal const string Else = "// kron:else";
    internal const string Close = "// kron:/verdicts";
    internal const string ElsePrefix = "//|";

    /// <summary>
    /// The script a report ships: with the verdict lines and without the marker lines when
    /// <paramref name="verdicts"/>, else with the lines that stand in for them. Throws on a fence that is not balanced,
    /// so a careless edit cannot ship half a block.
    /// </summary>
    public static string Variant(string source, bool verdicts)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = new StringBuilder(source.Length);
        var state = State.Outside;
        var first = true;
        var lineNumber = 0;
        foreach (var line in source.Split('\n'))
        {
            lineNumber++;
            var bare = line.TrimEnd('\r');
            if (bare == Open)
            {
                if (state != State.Outside)
                    throw Unbalanced(lineNumber, "a fence opened inside another");
                state = State.Verdicts;
                continue;
            }

            if (bare == Else)
            {
                if (state != State.Verdicts)
                    throw Unbalanced(lineNumber, "an else outside a fence");
                state = State.Without;
                continue;
            }

            if (bare == Close)
            {
                if (state == State.Outside)
                    throw Unbalanced(lineNumber, "a close without an open");
                state = State.Outside;
                continue;
            }

            string? kept = state switch
            {
                State.Outside => line,
                State.Verdicts => verdicts ? line : null,
                _ => bare.StartsWith(ElsePrefix, StringComparison.Ordinal)
                    ? verdicts ? null : line[ElsePrefix.Length..]
                    : throw Unbalanced(lineNumber, "an else line that does not start //|")
            };
            if (kept is null)
                continue;
            if (!first)
                output.Append('\n');
            output.Append(kept);
            first = false;
        }

        if (state != State.Outside)
            throw Unbalanced(lineNumber, "a fence that is never closed");
        return output.ToString();
    }

    private static InvalidOperationException Unbalanced(int line, string what) =>
        new($"Unbalanced $verdict fence at line {line}: {what}.");

    private enum State { Outside, Verdicts, Without }
}
