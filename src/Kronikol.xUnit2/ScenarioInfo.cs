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
}