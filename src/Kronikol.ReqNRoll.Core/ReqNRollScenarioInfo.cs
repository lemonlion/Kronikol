using Reqnroll;

namespace Kronikol.ReqNRoll;

/// <summary>
/// Contains complete metadata for a Reqnroll scenario execution, including feature context, tags, steps, status, and example data.
/// </summary>
public record ReqNRollScenarioInfo
{
    public required string ScenarioId { get; init; }
    public required string ScenarioTitle { get; init; }
    public string? ScenarioDescription { get; init; }
    public required string FeatureTitle { get; init; }
    public string? FeatureDescription { get; init; }
    public required string[] ScenarioTags { get; init; }
    public required string[] CombinedTags { get; init; }
    public Exception? TestError { get; init; }
    public ScenarioExecutionStatus ExecutionStatus { get; init; }
    public TimeSpan? Duration { get; init; }
    public List<ReqNRollStepInfo> Steps { get; init; } = [];
    public string? Rule { get; init; }
    public string? OutlineId { get; init; }
    public Dictionary<string, string>? ExampleValues { get; init; }
    public Dictionary<string, object?>? ExampleRawValues { get; init; }
    public Dictionary<string, string>? ExampleFlatValues { get; init; }
    public string? ExamplesBlockName { get; init; }
    public string? ExamplesBlockDescription { get; init; }
    public int? ExamplesBlockIndex { get; init; }
}