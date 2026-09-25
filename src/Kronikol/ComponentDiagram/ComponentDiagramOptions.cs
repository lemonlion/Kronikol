using Kronikol.InternalFlow;

namespace Kronikol.ComponentDiagram;

/// <summary>
/// Options for configuring C4-style component diagram generation.
/// </summary>
public record ComponentDiagramOptions
{
    /// <summary>File name (without extension) for the component diagram output. Default: <c>"ComponentDiagram"</c>.</summary>
    public string FileName { get; set; } = "ComponentDiagram";

    /// <summary>When <c>true</c>, the component diagram is embedded in the test run report. Default: <c>true</c>.</summary>
    public bool EmbedInTestRunReport { get; set; } = true;

    /// <summary>Title displayed above the component diagram. Default: <c>"Component Diagram"</c>.</summary>
    public string Title { get; set; } = "Component Diagram";

    /// <summary>
    /// PlantUML theme name written as <c>!theme &lt;name&gt;</c> into the component diagram's source. Applied under
    /// <see cref="PlantUmlRendering.Server"/> and <see cref="PlantUmlRendering.Local"/>, where the diagram is drawn with
    /// the C4 stdlib and the directive follows its <c>!include</c>, unvalidated against it. <b>No effect under
    /// <see cref="PlantUmlRendering.BrowserJs"/> (the default) and <see cref="PlantUmlRendering.NodeJs"/></b>: the
    /// diagram is drawn unthemed, and the run records a <see cref="Reports.DiagnosticKind.OptionNotApplied"/> entry.
    /// </summary>
    public string? PlantUmlTheme { get; set; }

    /// <summary>Filter that controls which participants appear in the diagram. Return <c>true</c> to include.</summary>
    public Func<string, bool>? ParticipantFilter { get; set; }

    /// <summary>Custom formatter for relationship labels between components.</summary>
    public Func<ComponentRelationship, string>? RelationshipLabelFormatter { get; set; }

    /// <summary>When <c>true</c>, relationship flow popups are shown for component connections. Default: <c>true</c>.</summary>
    public bool ShowRelationshipFlows { get; set; } = true;

    /// <summary>Diagram style for relationship flow visualisations. Default: <see cref="InternalFlowDiagramStyle.ActivityDiagram"/>.</summary>
    public InternalFlowDiagramStyle RelationshipFlowStyle { get; set; } = InternalFlowDiagramStyle.ActivityDiagram;

    /// <summary>When <c>true</c>, a system-level flame chart is included. Default: <c>true</c>.</summary>
    public bool ShowSystemFlameChart { get; set; } = true;

    /// <summary>Components with fewer than this many test interactions are flagged as low coverage. Default: <c>3</c>.</summary>
    public int LowCoverageThreshold { get; set; } = 3;

    /// <summary>Controls whether component diagram arrows are colored by dependency type or performance. Default: <see cref="ArrowColorMode.DependencyType"/>.</summary>
    public ArrowColorMode ArrowColorMode { get; set; } = ArrowColorMode.DependencyType;

    /// <summary>Optional user overrides for dependency type colors (key = category string, value = hex color).</summary>
    public Dictionary<string, string>? DependencyColors { get; set; }

    /// <summary>Maximum number of tests to display in the flame chart. Default: <c>50</c>.</summary>
    public int MaxFlameChartTests { get; set; } = 50;
}
