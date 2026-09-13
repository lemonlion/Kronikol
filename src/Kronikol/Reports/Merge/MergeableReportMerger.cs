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

            // Only when every shard agrees. Shards of one run share a suite, and that is the case this
            // carries; shards of DIFFERENT suites have no single answer, and inventing one would re-key
            // every scenario in the merged file to a suite half of them never belonged to. Null there is
            // the honest answer and reproduces the pre-3.1.0 ids, which is also the only id scheme two
            // different suites ever shared.
            Suite = reports.Select(r => r.Suite).Distinct(StringComparer.Ordinal).Count() == 1
                ? reports[0].Suite
                : null,

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
            CiMetadata = ReconcileCiMetadata(reports),

            // Only when every shard agrees, for the same reason `Suite` above is: there is no single
            // answer otherwise, and the merging machine's own environment is not one of the candidates.
            // A shard that recorded none contributes a null, which fails the test - that is deliberate,
            // since one shard's environment does not describe a shard that never said.
            Environment = reports.Select(r => r.Environment).Distinct().Count() == 1
                ? reports[0].Environment
                : null,
            // Shards run disjoint subsets, so their traffic simply concatenates - no dedup, because a
            // request and its response deliberately share one RequestResponseId.
            Interactions = reports.SelectMany(r => r.Interactions).ToArray(),
            StepPaths = MergeByScenario(reports.Select(r => r.StepPaths)),
            Annotations = MergeByScenario(reports.Select(r => r.Annotations)),
            Diagnostics = [.. reports.SelectMany(r => r.Diagnostics), .. DisagreementDiagnostics(reports)]
        };
    }

    /// <summary>
    /// Unions per-scenario side tables. A scenario id belongs to exactly one shard, so a collision means
    /// the same runner's output was supplied twice; first wins, matching how scenarios themselves merge.
    /// </summary>
    private static IReadOnlyDictionary<string, T> MergeByScenario<T>(IEnumerable<IReadOnlyDictionary<string, T>> sources)
    {
        var merged = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var source in sources)
            foreach (var entry in source)
                merged.TryAdd(entry.Key, entry.Value);
        return merged;
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
                    SourceFile = existing.SourceFile ?? feature.SourceFile,
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
    /// <summary>
    /// What the merge itself has to report about the merge: today, that the shards did not agree on the
    /// environment, so the merged file records none.
    /// </summary>
    /// <remarks>
    /// Dropping the key is the honest answer, but silence is only honest if a reader can find out why
    /// nothing was said. The distinct environments go in the message rather than into a widened
    /// <c>environment</c> value, which keeps that key's declared shape - an object of exactly os and
    /// runtime - and so needs no format version bump. A shard that recorded none is listed as such,
    /// because "one shard did not say" is the reason as often as "they ran on different machines".
    /// </remarks>
    private static IEnumerable<DiagnosticEntry> DisagreementDiagnostics(IReadOnlyList<MergeableReport> reports)
    {
        var environments = reports.Select(r => r.Environment).Distinct().ToArray();
        if (environments.Length <= 1)
            yield break;

        var described = environments.Select(e => e is null ? "(not recorded)" : $"{e.Os} / {e.Runtime}");

        yield return new DiagnosticEntry(
            DiagnosticKind.Other,
            $"Merged {reports.Count} reports that do not agree on the environment, so the merged report records none. "
            + "Seen: " + string.Join("; ", described) + ".");
    }

    private static CiMetadata? ReconcileCiMetadata(IReadOnlyList<MergeableReport> reports) =>
        reports.Select(r => r.CiMetadata).FirstOrDefault(m => m is not null);
}
