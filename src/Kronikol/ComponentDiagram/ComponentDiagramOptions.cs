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

    /// <summary>
    /// Has no effect: nothing reads it. It was meant to show relationship flow popups for component connections.
    /// Kept so that code which sets it still compiles; its removal waits for a major version.
    /// </summary>
    public bool ShowRelationshipFlows { get; set; } = true;

    /// <summary>
    /// Has no effect: nothing reads it. It was meant to choose the diagram style of relationship flow popups.
    /// Kept so that code which sets it still compiles; its removal waits for a major version.
    /// </summary>
    public InternalFlowDiagramStyle RelationshipFlowStyle { get; set; } = InternalFlowDiagramStyle.ActivityDiagram;

    /// <summary>
    /// Has no effect: nothing reads it. It was meant to add a system-level flame chart to the component diagram.
    /// Kept so that code which sets it still compiles; its removal waits for a major version.
    /// </summary>
    public bool ShowSystemFlameChart { get; set; } = true;

    /// <summary>
    /// Has no effect: nothing reads it. Low-coverage arrows are drawn dashed only from relationship stats a caller
    /// computes with <see cref="ComponentFlowSegmentBuilder.ComputeRelationshipStats"/>, which takes its own
    /// threshold, and passes to <see cref="ComponentDiagramGenerator.GeneratePlantUml"/>; a generated report
    /// passes none. Kept so that code which sets it still compiles; its removal waits for a major version.
    /// </summary>
    public int LowCoverageThreshold { get; set; } = 3;

    /// <summary>Controls whether component diagram arrows are colored by dependency type or performance. Default: <see cref="ArrowColorMode.DependencyType"/>.</summary>
    public ArrowColorMode ArrowColorMode { get; set; } = ArrowColorMode.DependencyType;

    /// <summary>Optional user overrides for dependency type colors (key = category string, value = hex color).</summary>
    public Dictionary<string, string>? DependencyColors { get; set; }

    /// <summary>
    /// Has no effect: nothing reads it. It was meant to cap the tests a system-level flame chart shows. Kept so
    /// that code which sets it still compiles; its removal waits for a major version.
    /// </summary>
    public int MaxFlameChartTests { get; set; } = 50;
}
