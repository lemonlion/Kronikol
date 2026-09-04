namespace Kronikol.Reports;

/// <summary>
/// Configurable default start states for the interactive controls in the generated HTML reports.
/// Every property is nullable; <c>null</c> means "inherit" — the built-in default for
/// <see cref="ReportConfigurationOptions.TestRunReportToggleDefaults"/>, and the effective
/// TestRunReport value for <see cref="ReportConfigurationOptions.SpecificationsToggleDefaults"/>
/// (Specifications.html inherits the TestRunReport defaults unless a setting is specifically
/// overridden). Defaults govern the INITIAL state of each control only: after any interaction the
/// page state is whatever the interaction leaves, exactly as with the built-in defaults. The
/// diagram-toolbar settings (details, truncate lines, headers, assertions, steps, databases,
/// note payload format) apply to <see cref="PlantUmlRendering.BrowserJs"/> reports only; a
/// setting whose control a given report does not emit resolves normally and is simply inert.
/// </summary>
public record ReportToggleDefaults
{
    /// <summary>The Details radio group (Expand / Collapse / Truncate). Default: <see cref="ReportDetailsState.Truncated"/>.</summary>
    public ReportDetailsState? Details { get; set; }

    /// <summary>The truncate-lines dropdown — one of its preset states. Default: <see cref="TruncateLineCount.Lines40"/>.</summary>
    public TruncateLineCount? TruncateLines { get; set; }

    /// <summary>Note header lines (gray metadata) shown. Default: <c>true</c>.</summary>
    public bool? HeadersShown { get; set; }

    /// <summary>Assertion notes shown in diagrams. Default: <c>false</c>.</summary>
    public bool? AssertionsShown { get; set; }

    /// <summary>Step-delimiter bars shown in diagrams. Default: <c>true</c>.</summary>
    public bool? StepsShown { get; set; }

    /// <summary>Database participants and calls shown in diagrams. Default: <c>true</c>.</summary>
    public bool? DatabasesShown { get; set; }

    /// <summary>
    /// Initial JSON ⇄ YAML display format for note payloads. Default: <c>null</c> — falls back to
    /// the flat <see cref="ReportConfigurationOptions.NotePayloadFormat"/> option (which remains
    /// the simple both-reports knob); a value set here wins over the flat option.
    /// </summary>
    public NotePayloadFormat? NotePayloadFormat { get; set; }

    /// <summary>Feature sections start expanded (with the Expand All Features button seeded to match). Default: <c>false</c>.</summary>
    public bool? FeaturesExpanded { get; set; }

    /// <summary>Scenario sections start expanded (with the Expand All Scenarios button seeded to match). Default: <c>false</c>.</summary>
    public bool? ScenariosExpanded { get; set; }

    /// <summary>
    /// The diagram-type tab a scenario's diagram section starts on, where that view exists for the
    /// scenario (built-in fallback order otherwise). Default: <see cref="DiagramTabKind.Sequence"/>.
    /// </summary>
    public DiagramTabKind? DiagramTab { get; set; }

    /// <summary>The Scenario Timeline panel starts visible. Default: <c>false</c>. When both this and
    /// <see cref="ComponentDiagramVisible"/> are set, the timeline wins (the panels are mutually exclusive).</summary>
    public bool? ScenarioTimelineVisible { get; set; }

    /// <summary>The embedded Component Diagram panel starts visible (test run report only). Default: <c>false</c>.</summary>
    public bool? ComponentDiagramVisible { get; set; }

    /// <summary>The dependency filter's AND/OR combination mode. Default: <see cref="FilterCombinationMode.And"/>.</summary>
    public FilterCombinationMode? DependencyFilterMode { get; set; }

    /// <summary>The category filter's AND/OR combination mode. Default: <see cref="FilterCombinationMode.Or"/>.</summary>
    public FilterCombinationMode? CategoryFilterMode { get; set; }

    /// <summary>The Features Summary table disclosure starts open (test run report only). Default: <c>false</c>.</summary>
    public bool? FeaturesSummaryOpen { get; set; }

    /// <summary>The Failure Clusters disclosure starts open. Default: <c>true</c>.</summary>
    public bool? FailureClustersOpen { get; set; }

    /// <summary>Each scenario's Steps disclosure starts open. Default: <c>true</c>.</summary>
    public bool? StepsSectionOpen { get; set; }

    /// <summary>Each scenario's diagrams disclosure starts open. Default: <c>true</c>.</summary>
    public bool? DiagramsSectionOpen { get; set; }

    /// <summary>The report diagnostics disclosure starts open (test run report only). Default: <c>false</c>.</summary>
    public bool? DiagnosticsOpen { get; set; }

    /// <summary>Rule sections start open. Default: <c>true</c>.</summary>
    public bool? RulesOpen { get; set; }

    /// <summary>The separate Background Steps disclosure starts open (only emitted with
    /// <see cref="ReportConfigurationOptions.SeparateBackgroundSteps"/>). Default: <c>false</c>.</summary>
    public bool? BackgroundStepsOpen { get; set; }

    /// <summary>The Raw Plant UML disclosure under each diagram image starts open (non-BrowserJs
    /// rendering modes only). Default: <c>false</c>.</summary>
    public bool? RawPlantUmlOpen { get; set; }

    /// <summary>Which view a parameterized group's example table starts on, where both exist.
    /// Default: <see cref="Reports.ParameterTableView.Flat"/>.</summary>
    public ParameterTableView? ParameterTableView { get; set; }

    /// <summary>Which tab the internal-flow Activity / Flame Chart toggles start on.
    /// Default: <see cref="Reports.InternalFlowTab.Activity"/>.</summary>
    public InternalFlowTab? InternalFlowTab { get; set; }
}
