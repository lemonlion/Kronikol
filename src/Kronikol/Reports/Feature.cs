namespace Kronikol.Reports;

/// <summary>
/// Represents a test feature (a logical group of scenarios) in the report.
/// </summary>
public record Feature
{
    public required string DisplayName { get; set; }
    public string? Endpoint { get; set; }
    public Scenario[] Scenarios { get; set; } = [];
    public string? Description { get; set; }
    public string[]? Labels { get; set; }

    /// <summary>
    /// Where the feature is written, as the runner reports it: a project-relative path with forward
    /// slashes on the Gherkin lanes (<c>Features/Cake.feature</c>), null everywhere else. Not the same
    /// contract as <see cref="ScenarioStep.SourceFile"/>, which is deliberately a bare file name.
    /// <para>Features are grouped by display name throughout Kronikol, so two feature files sharing a
    /// <c>Feature:</c> title become one feature here and the first path seen wins.</para>
    /// </summary>
    public string? SourceFile { get; set; }
}