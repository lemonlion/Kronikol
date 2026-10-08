using Kronikol.Reports;

namespace Kronikol.xUnit2;

/// <summary>
/// Holds xUnit v2 test scenario metadata including feature name, scenario name, endpoint, execution result, and error details.
/// </summary>
public class ScenarioInfo
{
    public required string Id { get; init; }
    public required string FeatureName { get; init; }
    public required string ScenarioName { get; set; }
    public required string MethodMatchKey { get; init; }
    public string? Endpoint { get; init; }
    public bool IsHappyPath { get; init; }
    public ExecutionResult Result { get; set; } = ExecutionResult.Passed;
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Whether xUnit sent a result for the test: set by Kronikol's results sink.</summary>
    internal bool HasResult { get; set; }

    /// <summary>
    /// Reported as <see cref="ExecutionResult.Passed"/> because no result was seen, which the report marks as a
    /// default rather than a verdict (<see cref="Scenario.ResultDefaulted"/>).
    /// </summary>
    internal bool ResultDefaulted { get; set; }

    /// <summary>Made by Kronikol's results sink when xUnit sent the test's <c>ITestStarting</c>, not by <see cref="TestTrackingAttribute"/>.</summary>
    internal bool MadeAtTestStarting { get; init; }
}