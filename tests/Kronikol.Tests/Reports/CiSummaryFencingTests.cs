using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>CiSummary.md</c> reproduces captured text instead of being rewritten by it.
///
/// <para>The digest learned this in 3.2.0: a fence is sized from the longest backtick run in the body it
/// wraps, because a fixed <c>```</c> around text that contains <c>```</c> ends early and everything after
/// it renders as the page's own markup. That fix was never propagated to <c>CiSummaryGenerator</c>, which
/// has three fixed-width fences of its own — one around a stack trace, two around PlantUML source that
/// embeds captured bodies as notes — and which targets a surface the digest does not: this file is pasted
/// into a GitHub Actions job summary and rendered as HTML in the UI.</para>
///
/// <para>The scenario and feature names on those lines are HTML-escaped. The captured
/// <c>errorMessage</c> beside them was not: it went through an escape that replaces <c>|</c> and nothing
/// else, outside any table, outside any fence, in a generator that deliberately emits raw HTML around it.
/// So an assertion message containing markup was markup.</para>
/// </summary>
public class CiSummaryFencingTests
{
    /// <summary>
    /// The breakout, in the shape a real one takes: a test that asserts on a fenced code sample, so the
    /// message it prints on failure contains a fence of its own.
    /// </summary>
    [Fact]
    public void A_stack_trace_containing_a_fence_does_not_end_its_own_block()
    {
        var markdown = Generate("   at Docs.Render() — expected ```json\\n{}\\n``` in the output\n   at Docs.Test()");

        var fenceLines = markdown.Split('\n').Count(l => l.TrimEnd() is "```");
        Assert.True(fenceLines % 2 == 0, $"unbalanced fences:\n{markdown}");
        Assert.Contains("````", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file emits raw HTML by design — <c>&lt;details&gt;</c> around every failure — so a browser
    /// renders what is in it. The names on the same line have always been escaped; the captured message
    /// was the one field that was not.
    /// </summary>
    [Fact]
    public void A_captured_error_message_is_not_markup()
    {
        var markdown = Generate(stackTrace: "   at Orders.Place()",
            errorMessage: "Expected <b>4173</b> but found <img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<img src=x", markdown, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x", markdown, StringComparison.Ordinal);
        Assert.Contains("4173", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file is made of the tool's own headings wrapped around run-supplied text, and had no line
    /// saying which was which — the rule its sibling `Failures.md` carries and it did not.
    /// </summary>
    [Fact]
    public void The_summary_says_that_its_quoted_text_is_test_data()
    {
        var markdown = Generate("   at Orders.Place()");

        Assert.Contains("not instructions", markdown, StringComparison.OrdinalIgnoreCase);
    }

    private static string Generate(string stackTrace, string errorMessage = "Assert.Equal() Failure") =>
        CiSummaryGenerator.GenerateMarkdown(
            [
                new Feature
                {
                    DisplayName = "Orders",
                    Scenarios =
                    [
                        new Scenario
                        {
                            Id = "t0", DisplayName = "Checkout", Result = ExecutionResult.Failed,
                            Duration = TimeSpan.FromSeconds(1),
                            ErrorMessage = errorMessage,
                            ErrorStackTrace = stackTrace,
                            Steps = [new ScenarioStep { Keyword = "When", Text = "placing", Status = ExecutionResult.Failed }]
                        }
                    ]
                }
            ],
            [], [],
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc));
}
