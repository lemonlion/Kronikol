namespace Kronikol.Reports;

/// <summary>
/// Represents a single step within a test scenario (e.g. Given, When, Then).
/// </summary>
public record ScenarioStep
{
    public string? Keyword { get; set; }

    /// <summary>
    /// True for a tracked assertion: one <c>Track.That</c> or the assertion weave recorded, or a tests NDJSON
    /// <c>assertion</c> record. The data file writes it only when true (<c>"assertion": true</c>), so a report
    /// without assertions is written as before; a step without a keyword is not an assertion for that alone.
    /// </summary>
    public bool IsAssertion { get; set; }

    public required string Text { get; set; }
    public ExecutionResult? Status { get; set; }
    public string? BypassReason { get; set; }
    public TimeSpan? Duration { get; set; }
    public ScenarioStep[]? SubSteps { get; set; }
    public StepParameter[]? Parameters { get; set; }
    public string[]? Comments { get; set; }
    public FileAttachment[]? Attachments { get; set; }
    public string? DocString { get; set; }
    public string? DocStringMediaType { get; set; }

    /// <summary>
    /// Why this step or assertion failed — the assertion's message, or the exception message that ended
    /// the step. The single most-wanted piece of information about a failing test, and until it lived here
    /// it reached the sequence diagram and nothing else.
    /// </summary>
    public string? FailureMessage { get; set; }

    /// <summary>
    /// The file the step or assertion was written in, when the caller supplied it: an assertion's call site in-process,
    /// a tests NDJSON <c>step</c> or <c>assertion</c> record's <c>sourceFile</c>, a Gherkin step's feature file. File name
    /// only, not a full path.
    /// </summary>
    public string? SourceFile { get; set; }

    /// <summary>The line <see cref="SourceFile"/> holds the step or assertion on; null when unknown.</summary>
    public int? SourceLine { get; set; }

    /// <summary>
    /// Structured representation of the step text with inline parameter values embedded.
    /// When set, the renderer uses this instead of <see cref="Text"/> to produce highlighted
    /// parameter values within the prose (matching LightBDD's native report rendering).
    /// </summary>
    public StepTextSegment[]? TextSegments { get; set; }
}

/// <summary>
/// A segment of step text — either literal prose or an inline parameter value.
/// </summary>
public record StepTextSegment
{
    /// <summary>Literal text content (mutually exclusive with <see cref="Parameter"/> and <see cref="TableReference"/>).</summary>
    public string? Text { get; init; }

    /// <summary>An inline parameter value to render highlighted (mutually exclusive with <see cref="Text"/> and <see cref="TableReference"/>).</summary>
    public InlineParameterValue? Parameter { get; init; }

    /// <summary>The parameter name (for tooltip), only set when <see cref="Parameter"/> is set.</summary>
    public string? ParameterName { get; init; }

    /// <summary>A reference to a tabular/tree parameter rendered below the step. Renders as a clickable toggle. Mutually exclusive with <see cref="Text"/> and <see cref="Parameter"/>.</summary>
    public string? TableReference { get; init; }

    /// <summary>The formatted value for a table reference parameter, used when no backing IParameterResult exists (e.g. bracket-appended params in CompositeStep methods).</summary>
    public string? TableReferenceFormattedValue { get; init; }

    /// <summary>Creates a literal text segment.</summary>
    public static StepTextSegment Literal(string text) => new() { Text = text };

    /// <summary>Creates a parameter segment with value, verification status, and name.</summary>
    public static StepTextSegment Param(string? name, InlineParameterValue value) => new() { Parameter = value, ParameterName = name };

    /// <summary>Creates a table/tree parameter reference segment that toggles visibility of the associated table.</summary>
    public static StepTextSegment TableRef(string paramName) => new() { TableReference = paramName };

    /// <summary>Creates a table/tree parameter reference segment with a formatted value fallback for display when no backing parameter data exists.</summary>
    public static StepTextSegment TableRef(string paramName, string? formattedValue) => new() { TableReference = paramName, TableReferenceFormattedValue = formattedValue };
}
