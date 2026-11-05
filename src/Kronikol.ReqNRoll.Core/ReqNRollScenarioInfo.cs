using Reqnroll;

namespace Kronikol.ReqNRoll;

/// <summary>
/// Contains complete metadata for a Reqnroll scenario execution, including feature context, tags, steps, status, and example data.
/// </summary>
public record ReqNRollScenarioInfo
{
    public required string ScenarioId { get; init; }
    /// <summary>
    /// The title as written, <c>ScenarioInfo.Title</c>: for an outline, with its <c>&lt;header&gt;</c> placeholders unfilled,
    /// the same on every row. The rows are ordered by it and the outline is named by it.
    /// </summary>
    public required string ScenarioTitle { get; init; }
    public string? ScenarioDescription { get; init; }
    public required string FeatureTitle { get; init; }
    public string? FeatureDescription { get; init; }
    public required string[] ScenarioTags { get; init; }
    public required string[] CombinedTags { get; init; }
    /// <summary>The feature's own tags, <c>FeatureInfo.Tags</c>; a scenario's combined tags hold them too.</summary>
    public string[] FeatureTags { get; init; } = [];
    public Exception? TestError { get; init; }
    public ScenarioExecutionStatus ExecutionStatus { get; init; }
    public TimeSpan? Duration { get; init; }

    /// <summary>When the scenario finished, stamped in the after-scenario hook that collects it.</summary>
    public DateTimeOffset? EndedAt { get; init; }
    public List<ReqNRollStepInfo> Steps { get; init; } = [];
    public string? Rule { get; init; }
    public string? OutlineId { get; init; }
    public Dictionary<string, string>? ExampleValues { get; init; }
    public Dictionary<string, object?>? ExampleRawValues { get; init; }
    public Dictionary<string, string>? ExampleFlatValues { get; init; }
    public string? ExamplesBlockName { get; init; }
    public string? ExamplesBlockDescription { get; init; }
    public int? ExamplesBlockIndex { get; init; }

    /// <summary>The feature file, project-relative with forward slashes, when Reqnroll's messages carry it.</summary>
    public string? SourceFile { get; init; }

    /// <summary>The line the <c>Scenario:</c> / <c>Scenario Outline:</c> keyword is on.</summary>
    public int? SourceLine { get; init; }

    /// <summary>
    /// The scenario's name as Gherkin names its pickle (#101): for an outline row, the title with its <c>&lt;header&gt;</c>
    /// placeholders filled from the row's values; for anything else, the title. The report shows it; null where the
    /// hooks did not resolve one, and the title is shown then.
    /// </summary>
    internal string? RowName { get; init; }

    /// <summary>
    /// The texts of the steps a <c>Background:</c> written in the feature or the scenario's rule gives this scenario, in the
    /// order they run (#222). Empty when none is written and when the Gherkin document is out of reach; null for a scenario
    /// built by hand, which never had a document to read.
    /// </summary>
    internal IReadOnlyList<string>? WrittenBackground { get; init; }
}