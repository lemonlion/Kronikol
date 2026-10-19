using System.Text.RegularExpressions;

namespace Kronikol.Ingestion;

/// <summary>
/// A terminal's control sequences, taken out of text a test runner meant for a terminal. Playwright's reporter API gives
/// an error's message with colour codes (<c>ESC[31m</c> … <c>ESC[39m</c>), and a tests file written from it, as the
/// wiki's recipe writes one, carried them into the report, <c>Failures.md</c> (where they were part of the cluster key)
/// and every query (plans/LIST_CAPS_PLAN.md 3.10). A bracket without the escape before it is text and stays.
/// </summary>
internal static class TerminalCodes
{
    /// <summary>A CSI sequence (colours, cursor moves, erasing) or an OSC one (a hyperlink, a title), as ECMA-48 frames them.</summary>
    private static readonly Regex Sequence = new(@"\e\[[0-?]*[ -/]*[@-~]|\e\][^\a\e]*(?:\a|\e\\)", RegexOptions.Compiled);

    /// <summary><paramref name="text"/> without its terminal control sequences; null stays null.</summary>
    public static string? Strip(string? text) =>
        text is null || text.IndexOf((char)27) < 0 ? text : Sequence.Replace(text, "");
}
