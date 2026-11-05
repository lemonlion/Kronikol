using Reqnroll;

namespace Kronikol.ReqNRoll;

/// <summary>
/// Captures information about an individual Reqnroll step execution, including its keyword, text, status, duration, and associated data.
/// </summary>
public record ReqNRollStepInfo(
    string Keyword,
    string Text,
    ScenarioExecutionStatus Status = ScenarioExecutionStatus.OK,
    TimeSpan? Duration = null,
    string? TableText = null,
    string? DocString = null,
    InlineParamCapture[]? InlineParams = null)
{
    /// <summary>The step the hooks opened for this Gherkin step (<c>StepCollector.StartFrame</c>); null on a hand-built one, and on one that did not run.</summary>
    internal int? FrameId { get; init; }

    /// <summary>For a step that did not run (skipped, undefined): the id of the step opened last before it, 0 when none was.</summary>
    internal int? NotRunAfter { get; init; }

    /// <summary>For a step that did not run with a Gherkin step open (one that runs another): the id of that step.</summary>
    internal int? NotRunUnder { get; init; }

    /// <summary>Whether a failure had stopped the scenario by the time this step did not run.</summary>
    internal bool AfterFailure { get; init; }
}

/// <summary>
/// Captures a single inline parameter's position and value within the step text.
/// </summary>
public record InlineParamCapture(int StartOffset, int Length, string Value, string? Name);