namespace Kronikol.Reports;

/// <summary>
/// The fully-resolved (non-nullable) toggle default start states one generated HTML report runs
/// with. <see cref="BuiltIn"/> is the single source of truth for the built-in defaults — the
/// literals the report markup and scripts historically hard-coded live here. Nothing downstream
/// of <c>GenerateHtmlReport</c> ever sees a null.
/// </summary>
public record ResolvedToggleDefaults
{
    /// <summary>The built-in defaults — the states every report started in before configurability.</summary>
    public static ResolvedToggleDefaults BuiltIn { get; } = new();

    public ReportDetailsState Details { get; init; } = ReportDetailsState.Truncated;
    public TruncateLineCount TruncateLines { get; init; } = TruncateLineCount.Lines40;
    public bool HeadersShown { get; init; } = true;
    public bool AssertionsShown { get; init; }
    public bool StepsShown { get; init; } = true;
    public bool DatabasesShown { get; init; } = true;
    public NotePayloadFormat NotePayloadFormat { get; init; } = NotePayloadFormat.Json;
    public bool FeaturesExpanded { get; init; }
    public bool ScenariosExpanded { get; init; }
    public DiagramTabKind DiagramTab { get; init; } = DiagramTabKind.Sequence;
    public bool ScenarioTimelineVisible { get; init; }
    public bool ComponentDiagramVisible { get; init; }
    public FilterCombinationMode DependencyFilterMode { get; init; } = FilterCombinationMode.And;
    public FilterCombinationMode CategoryFilterMode { get; init; } = FilterCombinationMode.Or;
    public bool FeaturesSummaryOpen { get; init; }
    public bool FailureClustersOpen { get; init; } = true;
    public bool StepsSectionOpen { get; init; } = true;
    public bool DiagramsSectionOpen { get; init; } = true;
    public bool DiagnosticsOpen { get; init; }
    public bool RulesOpen { get; init; } = true;
    public bool BackgroundStepsOpen { get; init; }
    public bool RawPlantUmlOpen { get; init; }
    public ParameterTableView ParameterTableView { get; init; } = ParameterTableView.Flat;
    public InternalFlowTab InternalFlowTab { get; init; } = InternalFlowTab.Activity;
}

/// <summary>
/// Resolves the effective toggle defaults for one report: the built-ins, overlaid by
/// <see cref="ReportConfigurationOptions.TestRunReportToggleDefaults"/>, then — for
/// Specifications.html — by <see cref="ReportConfigurationOptions.SpecificationsToggleDefaults"/>
/// (most specific wins, per property). <see cref="ReportToggleDefaults.NotePayloadFormat"/>
/// treats the flat <see cref="ReportConfigurationOptions.NotePayloadFormat"/> option as its
/// built-in, so the flat property keeps working and a group value wins when set.
/// </summary>
public static class ReportToggleDefaultsResolver
{
    public static ResolvedToggleDefaults Resolve(ReportConfigurationOptions options, bool specifications)
    {
        var resolved = ResolvedToggleDefaults.BuiltIn with { NotePayloadFormat = options.NotePayloadFormat };
        resolved = Overlay(resolved, options.TestRunReportToggleDefaults);
        if (specifications)
            resolved = Overlay(resolved, options.SpecificationsToggleDefaults);
        return resolved;
    }

    private static ResolvedToggleDefaults Overlay(ResolvedToggleDefaults baseline, ReportToggleDefaults overrides) => new()
    {
        Details = Defined(overrides.Details) ?? baseline.Details,
        TruncateLines = Defined(overrides.TruncateLines) ?? baseline.TruncateLines,
        HeadersShown = overrides.HeadersShown ?? baseline.HeadersShown,
        AssertionsShown = overrides.AssertionsShown ?? baseline.AssertionsShown,
        StepsShown = overrides.StepsShown ?? baseline.StepsShown,
        DatabasesShown = overrides.DatabasesShown ?? baseline.DatabasesShown,
        NotePayloadFormat = Defined(overrides.NotePayloadFormat) ?? baseline.NotePayloadFormat,
        FeaturesExpanded = overrides.FeaturesExpanded ?? baseline.FeaturesExpanded,
        ScenariosExpanded = overrides.ScenariosExpanded ?? baseline.ScenariosExpanded,
        DiagramTab = Defined(overrides.DiagramTab) ?? baseline.DiagramTab,
        ScenarioTimelineVisible = overrides.ScenarioTimelineVisible ?? baseline.ScenarioTimelineVisible,
        ComponentDiagramVisible = overrides.ComponentDiagramVisible ?? baseline.ComponentDiagramVisible,
        DependencyFilterMode = Defined(overrides.DependencyFilterMode) ?? baseline.DependencyFilterMode,
        CategoryFilterMode = Defined(overrides.CategoryFilterMode) ?? baseline.CategoryFilterMode,
        FeaturesSummaryOpen = overrides.FeaturesSummaryOpen ?? baseline.FeaturesSummaryOpen,
        FailureClustersOpen = overrides.FailureClustersOpen ?? baseline.FailureClustersOpen,
        StepsSectionOpen = overrides.StepsSectionOpen ?? baseline.StepsSectionOpen,
        DiagramsSectionOpen = overrides.DiagramsSectionOpen ?? baseline.DiagramsSectionOpen,
        DiagnosticsOpen = overrides.DiagnosticsOpen ?? baseline.DiagnosticsOpen,
        RulesOpen = overrides.RulesOpen ?? baseline.RulesOpen,
        BackgroundStepsOpen = overrides.BackgroundStepsOpen ?? baseline.BackgroundStepsOpen,
        RawPlantUmlOpen = overrides.RawPlantUmlOpen ?? baseline.RawPlantUmlOpen,
        ParameterTableView = Defined(overrides.ParameterTableView) ?? baseline.ParameterTableView,
        InternalFlowTab = Defined(overrides.InternalFlowTab) ?? baseline.InternalFlowTab
    };

    /// <summary>
    /// C# enums are not closed types — <c>(TruncateLineCount)37</c> compiles — so every configured
    /// enum value is checked and an undefined cast fails report generation with an error naming
    /// the valid members, instead of silently emitting broken markup.
    /// </summary>
    private static TEnum? Defined<TEnum>(TEnum? value) where TEnum : struct, Enum
    {
        if (value is { } v && !Enum.IsDefined(v))
            throw new ArgumentException(
                $"{typeof(TEnum).Name} has no member with value {Convert.ToInt64(v)}. Valid members: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        return value;
    }
}
