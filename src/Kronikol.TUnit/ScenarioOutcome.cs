using Kronikol.Reports;

namespace Kronikol.TUnit;

/// <summary>
/// What a TUnit result gives its scenario besides the verdict. Only a failed scenario carries a message and a stack
/// trace, the rule Kronikol.MSTest's <c>DiagrammedComponentTest</c> states: <c>Skip.Test</c> reports its reason on an
/// exception, and a skipped scenario carrying it read as a failure to every reader of the report. A duration below zero,
/// an end TUnit recorded before its start (a <c>[Before(Test)]</c> hook that threw), is no measurement, and nor is the
/// duration of a test that never started.
/// </summary>
internal static class ScenarioOutcome
{
    public static string? Message(ExecutionResult result, Exception? exception) =>
        result == ExecutionResult.Failed ? FailureText.OrNull(exception?.Message) : null;

    public static string? StackTrace(ExecutionResult result, Exception? exception) =>
        result == ExecutionResult.Failed ? FailureText.OrNull(exception?.StackTrace) : null;

    /// <summary>
    /// The measured duration, unless it is below zero or the test never started: TUnit measures a test whose class's
    /// constructor threw from no start at all (739,896 days on the probe).
    /// </summary>
    public static TimeSpan? Duration(TimeSpan? measured, DateTimeOffset? start) =>
        start is not null && measured is { Ticks: >= 0 } duration ? duration : null;
}
