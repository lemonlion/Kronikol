namespace Kronikol.Reports;

/// <summary>
/// The execution outcome of a test scenario.
/// </summary>
public enum ExecutionResult
{
    /// <summary>The scenario passed all assertions.</summary>
    Passed,

    /// <summary>The scenario failed due to an assertion or unhandled exception.</summary>
    Failed,

    /// <summary>The scenario was explicitly skipped (e.g. via <c>[Skip]</c> or <c>[Ignore]</c>).</summary>
    Skipped,

    /// <summary>
    /// Some of the logic in a step was intentionally skipped over at runtime while the steps after it ran: a LightBDD
    /// bypass, a tracked step's <c>SkipIf</c>, a tests file's <c>bypassed</c> step, or a Cucumber step reported
    /// <c>SKIPPED</c> that a later step ran after. On a scenario: a step of it was bypassed and none failed or skipped
    /// the rest (the LightBDD adapter and <c>kronikol ingest</c>; the other adapters keep the framework's verdict).
    /// </summary>
    Bypassed,

    /// <summary>The scenario was skipped because a prior scenario in the same group failed.</summary>
    SkippedAfterFailure
}