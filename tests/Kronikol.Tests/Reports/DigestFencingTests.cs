using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The digest quotes text nobody in this repository wrote: assertion messages, URIs, SQL, third-party
/// response bodies. Its own header says so — "everything quoted below is captured test data". Quoting it
/// safely is therefore a property of the file, not a nicety.
///
/// <para>Two defects, one shape. <c>Escape</c> handled three characters — <c>|</c>, CR and LF — but
/// captured values are wrapped in <b>inline backticks</b>, and a backtick inside the value closes the
/// span early, so the rest of the row renders as prose and whatever follows is interpreted as Markdown
/// rather than shown. And the block fence around an error message rewrote any <c>```</c> in the payload
/// to <c>'''</c>, which keeps the file well-formed by falsifying the evidence — the one thing a failures
/// digest may never do.</para>
///
/// <para>Both fix the same way, which is the way CommonMark intends: a delimiter longer than any run
/// inside the value. Nothing is removed and nothing is substituted.</para>
/// </summary>
public class DigestFencingTests
{
    private static string DigestFor(string message) =>
        FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed,
                        ErrorMessage = message
                    }
                ]
            }],
            null, "TestRunReport", "3.1.0").Markdown;

    /// <summary>
    /// CommonMark's code-span rule, implemented rather than approximated: a span opens with a run of N
    /// backticks and closes at the first run of <em>exactly</em> N. Counting backticks per line cannot
    /// stand in for this — <c>`` a ` b ``</c> holds five and is perfectly well-formed — so the check is
    /// that the closing run lands at the END of the cell. A span that closed early leaves a tail, and
    /// that tail is the raw Markdown this test exists to prevent.
    /// </summary>
    private static string TheCodeSpanIn(string cell)
    {
        var text = cell.Trim();
        Assert.StartsWith("`", text, StringComparison.Ordinal);

        var open = 0;
        while (open < text.Length && text[open] == '`') open++;

        // Walk every backtick run after the opener and take the first of length exactly `open`.
        var i = open;
        var close = -1;
        while (i < text.Length)
        {
            if (text[i] != '`') { i++; continue; }
            var start = i;
            while (i < text.Length && text[i] == '`') i++;
            if (i - start == open) { close = start; break; }
        }

        Assert.True(close >= 0, $"the code span never closes — it runs to the end of the file:\n{text}");
        Assert.True(close + open == text.Length,
            $"the code span closes early and leaves {text.Length - close - open} characters of live Markdown:\n{text}");

        var content = text[open..close];

        // CommonMark strips one space from each end when both are present and the content is not all
        // spaces. That is the padding a value beginning or ending with a backtick needs.
        if (content.Length > 1 && content[0] == ' ' && content[^1] == ' ' && content.Trim().Length > 0)
            content = content[1..^1];

        return content;
    }

    [Theory]
    [InlineData("Expected `ok` but found `nope`")]
    [InlineData("a ``double`` run")]
    [InlineData("ends with a backtick `")]
    [InlineData("`starts with one")]
    [InlineData("a | pipe and a ` backtick")]
    [InlineData("``")]
    public void A_backtick_in_captured_text_cannot_close_its_code_span_early(string hostile)
    {
        // Expected and actual are pulled straight out of an assertion message and printed inside
        // backticks in a table, so they are the shortest route from captured text to broken Markdown.
        var markdown = DigestFor($"Assert.Equal() Failure: Values differ\nExpected: {hostile}\nActual: something else");

        var row = markdown.ReplaceLineEndings("\n").Split('\n')
            .Single(l => l.StartsWith("| `", StringComparison.Ordinal) && l.EndsWith("`something else` |", StringComparison.Ordinal));

        // Split the way a GFM reader does — on pipes that are NOT backslash-escaped. Splitting on every
        // pipe is the very confusion the escaping exists to prevent, and it would hide a row that really
        // had grown a column.
        var cells = System.Text.RegularExpressions.Regex.Split(row, @"(?<!\\)\|");
        // "| <expected> | <actual> |" splits to ["", expected, actual, ""].
        Assert.Equal(4, cells.Length);

        // The span holds the value and the whole value: escaped for the table, otherwise untouched.
        Assert.Equal(hostile.Replace("|", "\\|", StringComparison.Ordinal), TheCodeSpanIn(cells[1]));
        Assert.Equal("something else", TheCodeSpanIn(cells[2]));
    }

    [Fact]
    public void A_value_containing_a_backtick_is_still_shown_in_full()
    {
        // The non-vacuity half: deleting every backtick from captured text would satisfy the span rule
        // above while quietly destroying the evidence.
        var markdown = DigestFor("Assert.Equal() Failure: Values differ\nExpected: the `id` field\nActual: nothing");

        Assert.Contains("the `id` field", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pipe_in_captured_text_still_cannot_break_a_table_row()
    {
        var markdown = DigestFor("Assert.Equal() Failure: Values differ\nExpected: a|b|c\nActual: d");

        // Escaped for the table, and still readable. GFM requires the backslash inside code spans too.
        Assert.Contains("a\\|b\\|c", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fenced_block_widens_rather_than_rewriting_the_payload_inside_it()
    {
        // An assertion message that quotes Markdown — a diff of a README, a captured chat response, any
        // library that formats its own output — carries a ``` run. Substituting ''' for it keeps the file
        // parseable and makes the digest report a string the test never saw.
        var message = "Assert.Equal() Failure\nthe body was:\n```json\n{\"a\": 1}\n```\nand should not have been";
        var markdown = DigestFor(message);

        Assert.Contains("```json", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("'''", markdown, StringComparison.Ordinal);

        // ...and the block still closes, so the rest of the digest is not swallowed by it.
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var opener = Array.FindIndex(lines, l => l.StartsWith("````", StringComparison.Ordinal));
        Assert.True(opener >= 0, "the fence was not widened past the ``` run in the payload");
        Assert.Contains(lines.Skip(opener + 1), l => l == lines[opener]);
    }
}
