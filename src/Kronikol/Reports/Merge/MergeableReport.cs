using System.Text.Json;
using Kronikol.ComponentDiagram;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Reports.Merge;

/// <summary>
/// The in-memory form of an enriched "mergeable" test-run report, reconstructed from a JSON file
/// produced with <see cref="ReportConfigurationOptions.GenerateMergeableData"/> enabled.
/// Holds everything required to render a full HTML report — and to merge several such reports
/// (e.g. from parallel CI runners) into a single combined report.
/// </summary>
public sealed class MergeableReport
{
    /// <summary>The Kronikol version that produced the source report (informational).</summary>
    public string KronikolVersion { get; init; } = "";

    /// <summary>Earliest start time across the contained run.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>Latest end time across the contained run.</summary>
    public DateTime EndTime { get; init; }

    /// <summary>The features and their scenarios/steps.</summary>
    public Feature[] Features { get; init; } = [];

    /// <summary>Per-scenario diagram source (sequence/activity PlantUML), keyed by scenario id via <see cref="DiagramAsCode.TestRuntimeId"/>.</summary>
    public DiagramAsCode[] Diagrams { get; init; } = [];

    /// <summary>Aggregated component-diagram relationships extracted from the run's tracked traffic.</summary>
    public ComponentRelationship[] ComponentRelationships { get; init; } = [];

    /// <summary>Precomputed, self-contained internal-flow segment payloads keyed by segment id (consumed by the popup JS as <c>window.__iflowSegments</c>).</summary>
    public Dictionary<string, JsonElement> InternalFlowSegments { get; init; } = new();

    /// <summary>Precomputed, self-contained whole-test-flow fragments keyed by scenario id.</summary>
    public Dictionary<string, WholeTestFlowFragment> WholeTestFlow { get; init; } = new();

    /// <summary>The whole-test-flow visualization mode the source report was rendered with.</summary>
    public WholeTestFlowVisualization WholeTestVisualization { get; init; }

    /// <summary>CI metadata captured for the run, if any.</summary>
    public CiMetadata? CiMetadata { get; init; }
}
