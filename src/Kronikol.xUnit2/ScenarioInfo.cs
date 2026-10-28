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

    /// <summary>The <c>Category</c> traits of the test's class and its base classes (<see cref="CategoryTraits"/>).</summary>
    public string[]? FeatureTags { get; init; }

    /// <summary>The <c>Category</c> traits of the test method.</summary>
    public string[]? Tags { get; init; }
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

    /// <summary>The order the process made its scenarios in, the sink's and the attribute's alike.</summary>
    internal long Sequence { get; init; } = XUnit2TestTrackingContext.NextSequence();

    /// <summary>A sink's scenario that <see cref="TestTrackingAttribute"/>'s <c>Before</c> took from the hand-over.</summary>
    internal bool TakenByBefore { get; set; }

    /// <summary>An attribute's own scenario that a run's sink has reported, so no later run reports it again.</summary>
    internal bool Claimed { get; set; }

    /// <summary>
    /// The name 5.16.4 gave the test, where <see cref="ScenarioName"/> is another: the scenario's id keys on it, and
    /// on the outline and values read from it (plans/TITLES_AS_WRITTEN_PLAN.md 4.4). Null for a scenario a consumer
    /// made, whose id keys on its name as it is.
    /// </summary>
    internal string? StableIdName { get; set; }

    /// <summary>A name the test wrote (a DisplayName), shown as written (<see cref="Scenario.NameAsWritten"/>).</summary>
    internal bool NameAsWritten { get; set; }

    /// <summary>A name the test wrote whole, with no arguments xUnit appended: its brackets are not a row's values.</summary>
    internal bool NameWrittenWhole { get; set; }

    /// <summary>The theory's DisplayName, else its method's title by the rule (<see cref="Scenario.OutlineTitle"/>).</summary>
    internal string? OutlineTitle { get; set; }
}