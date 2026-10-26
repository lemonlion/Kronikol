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

    /// <summary>
    /// The feature's own tags: those of its class, its fixture or its <c>Feature:</c>, each once in the first spelling
    /// met, and on the tests NDJSON lane those every one of its scenarios carries. Every scenario of the feature carries
    /// them first among its effective tags. Null when there are none.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Where the feature is written, as the runner reports it: a project-relative path with forward
    /// slashes on the Gherkin lanes (<c>Features/Cake.feature</c>) and, from 4.2.0, on the tests NDJSON
    /// lane, the first <c>sourceFile</c> one of its scenarios' <c>start</c> records gives; null everywhere
    /// else. Not the same
    /// contract as <see cref="ScenarioStep.SourceFile"/>, which is deliberately a bare file name.
    /// <para>Features are grouped by display name throughout Kronikol, so two feature files sharing a
    /// <c>Feature:</c> title become one feature here and the first path seen wins.</para>
    /// </summary>
    public string? SourceFile { get; set; }

    /// <summary>
    /// The name the feature's scenarios' stable ids are computed from, where a lane records one: 5.9.0's titleized name
    /// (<c>StringCasing.TitleizeAsOf590</c>) on a lane that titleizes a feature's name, so a feature 5.9.1 shows
    /// differently (a Gherkin title as written, a word with an apostrophe kept) keeps its scenarios' ids and their
    /// history (plans/REPORT_UI_FIXES_PLAN.md N3). Null where the ids key on <see cref="DisplayName"/>. The data files
    /// write it as <c>stableIdName</c> (only where it differs from the name), so a merge computes the same ids.
    /// </summary>
    internal string? StableIdName { get; set; }
}