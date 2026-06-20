using Kronikol.ComponentDiagram;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Reports.Merge;

/// <summary>
/// Combines several <see cref="MergeableReport"/>s — typically produced by parallel CI runners each
/// executing a disjoint subset of the test suite — into a single report equivalent to one produced
/// had all tests run together.
/// </summary>
public static class MergeableReportMerger
{
    /// <summary>Merges the supplied reports into one. Throws if the sequence is empty.</summary>
    public static MergeableReport Merge(IReadOnlyList<MergeableReport> reports)
    {
        if (reports is null || reports.Count == 0)
            throw new ArgumentException("At least one report is required to merge.", nameof(reports));

        return new MergeableReport
        {
            KronikolVersion = reports[0].KronikolVersion,
            StartTime = reports.Min(r => r.StartTime),
            EndTime = reports.Max(r => r.EndTime),
            Features = MergeFeatures(reports),
            Diagrams = MergeDiagrams(reports),
            ComponentRelationships = MergeRelationships(reports),
            InternalFlowSegments = MergeMap(reports.Select(r => r.InternalFlowSegments)),
            WholeTestFlow = MergeMap(reports.Select(r => r.WholeTestFlow)),
            WholeTestVisualization = reports
                .Select(r => r.WholeTestVisualization)
                .FirstOrDefault(v => v != WholeTestFlowVisualization.None),
            CiMetadata = ReconcileCiMetadata(reports)
        };
    }

    /// <summary>
    /// Groups features by display name so a feature split across multiple runners is recombined into
    /// one, with its scenarios unioned (deduplicated by scenario id). Feature order is alphabetical to
    /// match the report's own ordering.
    /// </summary>
    private static Feature[] MergeFeatures(IReadOnlyList<MergeableReport> reports)
    {
        var byName = new Dictionary<string, Feature>(StringComparer.Ordinal);
        var order = new List<string>();
        var scenarioIdsByFeature = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var feature in reports.SelectMany(r => r.Features))
        {
            if (!byName.TryGetValue(feature.DisplayName, out var existing))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var scenarios = feature.Scenarios.Where(s => seen.Add(s.Id)).ToList();
                byName[feature.DisplayName] = feature with { Scenarios = scenarios.ToArray() };
                scenarioIdsByFeature[feature.DisplayName] = seen;
                order.Add(feature.DisplayName);
            }
            else
            {
                var seen = scenarioIdsByFeature[feature.DisplayName];
                var merged = existing.Scenarios.Concat(feature.Scenarios.Where(s => seen.Add(s.Id))).ToArray();
                byName[feature.DisplayName] = existing with
                {
                    Scenarios = merged,
                    Endpoint = existing.Endpoint ?? feature.Endpoint,
                    Description = existing.Description ?? feature.Description,
                    Labels = existing.Labels ?? feature.Labels
                };
            }
        }

        return order.OrderBy(n => n, StringComparer.Ordinal).Select(n => byName[n]).ToArray();
    }

    private static DiagramAsCode[] MergeDiagrams(IReadOnlyList<MergeableReport> reports)
    {
        var seen = new HashSet<(string, string)>();
        var result = new List<DiagramAsCode>();
        foreach (var d in reports.SelectMany(r => r.Diagrams))
            if (seen.Add((d.TestRuntimeId, d.CodeBehind)))
                result.Add(d);
        return result.ToArray();
    }

    /// <summary>
    /// Re-aggregates component relationships across reports. Runners execute disjoint test subsets, so
    /// call counts and test counts sum exactly; method sets are unioned.
    /// </summary>
    private static ComponentRelationship[] MergeRelationships(IReadOnlyList<MergeableReport> reports)
    {
        var byKey = new Dictionary<(string, string, string), ComponentRelationship>();

        foreach (var rel in reports.SelectMany(r => r.ComponentRelationships))
        {
            var key = (rel.Caller, rel.Service, rel.Protocol);
            if (!byKey.TryGetValue(key, out var existing))
            {
                byKey[key] = rel with { Methods = new HashSet<string>(rel.Methods) };
            }
            else
            {
                var methods = new HashSet<string>(existing.Methods);
                methods.UnionWith(rel.Methods);
                byKey[key] = existing with
                {
                    Methods = methods,
                    CallCount = existing.CallCount + rel.CallCount,
                    TestCount = existing.TestCount + rel.TestCount,
                    DependencyCategory = existing.DependencyCategory ?? rel.DependencyCategory
                };
            }
        }

        return byKey.Values.ToArray();
    }

    private static Dictionary<string, T> MergeMap<T>(IEnumerable<Dictionary<string, T>> maps)
    {
        var result = new Dictionary<string, T>();
        foreach (var map in maps)
            foreach (var kvp in map)
                result[kvp.Key] = kvp.Value; // scenario/segment ids are unique across disjoint runners
        return result;
    }

    /// <summary>
    /// Picks the first report that captured CI metadata. Runners in the same workflow share repository,
    /// branch and commit, so the first non-null record represents the combined run.
    /// </summary>
    private static CiMetadata? ReconcileCiMetadata(IReadOnlyList<MergeableReport> reports) =>
        reports.Select(r => r.CiMetadata).FirstOrDefault(m => m is not null);
}
