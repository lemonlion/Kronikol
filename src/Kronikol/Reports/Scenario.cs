namespace Kronikol.Reports;

/// <summary>
/// Represents a single test scenario in the report, including execution result,
/// steps, labels, categories, and parameterized example values.
/// </summary>
public record Scenario
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>
    /// The scenario's own free-text description — the prose a Gherkin author writes under
    /// <c>Scenario:</c>, before the first step. Rendered above the step list in the living documentation.
    /// </summary>
    public string? Description { get; set; }

    public bool IsHappyPath { get; set; }
    public ExecutionResult Result { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public TimeSpan? Duration { get; set; }
    public ScenarioStep[]? Steps { get; set; }
    public ScenarioStep[]? BackgroundSteps { get; set; }
    public FileAttachment[]? Attachments { get; set; }
    public string[]? Labels { get; set; }
    public string[]? Categories { get; set; }
    public string? Rule { get; set; }
    public string? OutlineId { get; set; }
    public Dictionary<string, string>? ExampleValues { get; set; }
    public Dictionary<string, object?>? ExampleRawValues { get; set; }
    public Dictionary<string, string>? ExampleFlatValues { get; set; }
    public string? ExampleDisplayName { get; set; }

    /// <summary>Name of the <c>Examples:</c> block this outline row came from (e.g. "the merchant gained share").</summary>
    public string? ExamplesBlockName { get; set; }

    /// <summary>Free-text description under the <c>Examples:</c> header, when the author wrote one.</summary>
    public string? ExamplesBlockDescription { get; set; }

    /// <summary>0-based position of the <c>Examples:</c> block within the outline; orders and separates blocks (needed when blocks are unnamed).</summary>
    public int? ExamplesBlockIndex { get; set; }

    /// <summary>
    /// Which run of this scenario produced the result, when the runner retries: 1 for the first, 2 for
    /// the first retry. Null where the runner reports nothing about attempts, which is every lane but
    /// Cucumber Messages today. <b>1-based deliberately</b>, matching the <c>retry N</c> label rendered
    /// beside it - Cucumber's own wire value is 0-based, and a field that disagreed with the label on
    /// the same scenario would be worse than no field.
    /// </summary>
    public int? Attempt { get; set; }

    /// <summary>
    /// Where the scenario is written - a project-relative path with forward slashes, matching
    /// <see cref="Feature.SourceFile"/>. Null on the lanes that cannot supply one. Deliberately NOT the
    /// bare-file-name contract of <see cref="ScenarioStep.SourceFile"/>: a step's path comes from
    /// <c>[CallerFilePath]</c> on the build machine and is reduced to a name, a scenario's comes from the
    /// Gherkin document and is already relative.
    /// </summary>
    public string? SourceFile { get; set; }

    /// <summary>
    /// The line the scenario is declared on - the <c>Scenario:</c> or <c>Scenario Outline:</c> keyword,
    /// not the <c>Examples:</c> row, so every row of an outline points at the same declaration.
    /// <see cref="ExampleValues"/> is what says which row.
    /// </summary>
    public int? SourceLine { get; set; }
}