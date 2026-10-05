namespace Kronikol.ComponentDiagram;

/// <summary>
/// Controls whether component diagram arrows are colored by dependency type or performance (P95 latency).
/// </summary>
public enum ArrowColorMode
{
    /// <summary>Arrow color indicates the target service's dependency type (default).</summary>
    DependencyType,

    /// <summary>
    /// Arrow color indicates P95 latency (green/orange/red), in a diagram drawn with relationship stats: a caller
    /// computes them with <see cref="ComponentFlowSegmentBuilder.ComputeRelationshipStats"/> and passes them to
    /// <see cref="ComponentDiagramGenerator.GeneratePlantUml"/>. A generated report passes none (none has since
    /// 2.0.92-beta), so in this mode its component diagram draws its arrows uncolored.
    /// </summary>
    Performance
}
