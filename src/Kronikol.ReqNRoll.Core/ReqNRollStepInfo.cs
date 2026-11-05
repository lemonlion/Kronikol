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
    /// <summary>The steps this step's definition ran, in the order they finished. Each step that opened a frame has its own list,
    /// and StepCollector was told that list as what opened the step, which is how the two are paired.</summary>
    internal IReadOnlyList<ReqNRollStepInfo>? Nested { get; init; }

    /// <summary>A step definition ran this step, which makes it that step's sub-step and not one of the scenario's own rows.</summary>
    internal bool RanByAStep { get; init; }
}

/// <summary>
/// Captures a single inline parameter's position and value within the step text.
/// </summary>
public record InlineParamCapture(int StartOffset, int Length, string Value, string? Name);