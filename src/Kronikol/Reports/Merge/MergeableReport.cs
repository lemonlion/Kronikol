using System.Text.Json;
using Kronikol.ComponentDiagram;
using Kronikol.Tracking;
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

    /// <summary>
    /// Every captured interaction in the contained run, keyed to its scenario by
    /// <see cref="RequestResponseLog.TestId"/>. Empty for a file written before 3.1.0, which carried
    /// no traffic at all - the merged report could then be read but not debugged, and every
    /// interaction-shaped <c>kronikol query</c> verb came back empty.
    /// </summary>
    /// <remarks>
    /// These are the real interactions only: the diagram markers (step delimiters, assertion notes,
    /// the Setup/Action boundary) are dropped at write time and never round-trip, which is why
    /// <see cref="StepPaths"/> and <see cref="Annotations"/> are carried explicitly rather than
    /// re-derived - the derivation reads the markers.
    /// </remarks>
    public RequestResponseLog[] Interactions { get; init; } = [];

    /// <summary>
    /// Which step each interaction happened under, per scenario id, positionally aligned with that
    /// scenario's entries in <see cref="Interactions"/>. A null entry means the interaction could not
    /// be attributed.
    /// </summary>
    public IReadOnlyDictionary<string, List<string?>> StepPaths { get; init; } =
        new Dictionary<string, List<string?>>(StringComparer.Ordinal);

    /// <summary>Diagnostics the run recorded - notably <see cref="DiagnosticKind.ResultDefaulted"/>,
    /// without which a scenario defaulted to Passed is indistinguishable from a real pass.</summary>
    public IReadOnlyList<DiagnosticEntry> Diagnostics { get; init; } = [];

    /// <summary>Exported diagram annotations per scenario id.</summary>
    internal IReadOnlyDictionary<string, List<ReportGenerator.ScenarioAnnotation>> Annotations { get; init; } =
        new Dictionary<string, List<ReportGenerator.ScenarioAnnotation>>(StringComparer.Ordinal);
}
