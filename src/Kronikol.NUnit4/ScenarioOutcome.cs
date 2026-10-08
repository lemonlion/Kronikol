using Kronikol.Reports;
using NUnit.Framework.Interfaces;

namespace Kronikol.NUnit4;

/// <summary>
/// What an NUnit result gives its scenario besides the verdict. Only a failed scenario carries a message and a stack
/// trace, the rule Kronikol.MSTest's <c>DiagrammedComponentTest</c> states: <c>Assert.Pass</c>, <c>Assert.Ignore</c> and
/// <c>Assert.Inconclusive</c> put their reason in the result's message, every passing result has an empty one, and a
/// passed or skipped scenario carrying either read as a failure to every reader of the report. A warning
/// (<c>Assert.Warn</c>, reported as passed) keeps its text, the only trace of it the report has, until
/// plans/ADAPTER_CAPTURE_GAPS_PLAN.md Q5 settles how a warning should show.
/// </summary>
internal static class ScenarioOutcome
{
    public static string? Message(TestStatus status, string? message) =>
        CarriesText(status) ? FailureText.OrNull(message) : null;

    public static string? StackTrace(TestStatus status, string? stackTrace) =>
        CarriesText(status) ? FailureText.OrNull(stackTrace) : null;

    private static bool CarriesText(TestStatus status) => status is TestStatus.Failed or TestStatus.Warning;
}
