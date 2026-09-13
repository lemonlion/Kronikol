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

        reports = DisambiguateRuntimeIds(reports);

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
    /// one, with its scenarios unioned. Feature order is alphabetical to match the report's own ordering.
    /// </summary>
    /// <remarks>
    /// <para>Scenarios deduplicate on what they ARE - feature, name, outline row and example values -
    /// and not on their runtime id. The runtime id looked like the obvious key and the invariant written
    /// beside it said "a scenario id belongs to exactly one shard, so a collision means the same
    /// runner's output was supplied twice". That is false for the frameworks most likely to be sharded:
    /// NUnit's id is a per-process sequential counter (this repository's own NUnit report contains
    /// <c>"id": "0-1002"</c>), so every shard process mints the same ids for entirely different tests.
    /// Two shards each running a different scenario of one feature produced ONE scenario in the merged
    /// file, whichever sorted second was deleted, and a shard whose test failed merged green.</para>
    ///
    /// <para>Content is what tells the two cases apart, and it is what <see cref="ScenarioStableId"/>
    /// already computes for every cross-run comparison in the tool. The same shard supplied twice still
    /// deduplicates, because the two copies have the same content; two different tests do not.</para>
    /// </remarks>
    private static Feature[] MergeFeatures(IReadOnlyList<MergeableReport> reports)
    {
        var byName = new Dictionary<string, Feature>(StringComparer.Ordinal);
        var order = new List<string>();
        var seenByFeature = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var feature in reports.SelectMany(r => r.Features))
        {
            if (!byName.TryGetValue(feature.DisplayName, out var existing))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var scenarios = feature.Scenarios.Where(s => seen.Add(Identity(feature, s))).ToArray();
                byName[feature.DisplayName] = feature with { Scenarios = scenarios };
                seenByFeature[feature.DisplayName] = seen;
                order.Add(feature.DisplayName);
            }
            else
            {
                var seen = seenByFeature[feature.DisplayName];
                var merged = existing.Scenarios.Concat(feature.Scenarios.Where(s => seen.Add(Identity(feature, s)))).ToArray();
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

    /// <summary>
    /// Gives every distinct scenario a runtime id that is unique across the whole merge, rewriting the
    /// side tables that are keyed by it.
    /// </summary>
    /// <remarks>
    /// <para>Everything hung off a scenario travels by its runtime id: its captured traffic, its step
    /// paths, its annotations and its diagrams. A runtime id is a per-PROCESS value - NUnit's is a
    /// sequential counter, and <see cref="ScenarioStableId"/>'s own documentation says the id "varies by
    /// test framework and can be randomised" - so two shards routinely mint the same one for different
    /// tests. Once <see cref="MergeFeatures"/> stopped deleting the second of those, both survived under
    /// one id and each of them acquired ALL of both scenarios' calls.</para>
    ///
    /// <para>Renumbering loses nothing. The runtime id has no meaning outside the process that minted
    /// it and nothing user-facing keys on it: <c>stableId</c>, which is what the report prints and what
    /// every cross-run comparison uses, is computed from the scenario's content and is untouched. Only
    /// genuine collisions are renumbered - a scenario whose id is not already taken keeps it - so the
    /// ordinary merge, and every report written by a framework with globally unique ids, is unchanged.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<MergeableReport> DisambiguateRuntimeIds(IReadOnlyList<MergeableReport> reports)
    {
        // Which identity claimed each id first. A second claim by the SAME identity is the same scenario
        // arriving twice and keeps the id; a second claim by a different one is the collision.
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        var rewritten = new List<MergeableReport>(reports.Count);
        var collisions = 0;

        foreach (var report in reports)
        {
            var renames = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var feature in report.Features)
            foreach (var scenario in feature.Scenarios)
            {
                var identity = Identity(feature, scenario);
                if (!owner.TryGetValue(scenario.Id, out var taken))
                {
                    owner[scenario.Id] = identity;
                    continue;
                }

                if (string.Equals(taken, identity, StringComparison.Ordinal))
                    continue;

                var fresh = $"{scenario.Id}#{++collisions}";
                while (owner.ContainsKey(fresh))
                    fresh = $"{scenario.Id}#{++collisions}";

                owner[fresh] = identity;
                renames[scenario.Id] = fresh;
            }

            rewritten.Add(renames.Count == 0 ? report : Rename(report, renames));
        }

        return rewritten;
    }

    /// <summary>One shard with some of its scenario ids rewritten, in the scenario and in every side table keyed by it.</summary>
    private static MergeableReport Rename(MergeableReport report, Dictionary<string, string> renames)
    {
        string Id(string id) => renames.TryGetValue(id, out var fresh) ? fresh : id;

        return report with
        {
            Features = report.Features
                .Select(f => f with { Scenarios = f.Scenarios.Select(s => renames.TryGetValue(s.Id, out var fresh) ? s with { Id = fresh } : s).ToArray() })
                .ToArray(),
            Interactions = report.Interactions.Select(i => i with { TestId = Id(i.TestId) }).ToArray(),
            Diagrams = report.Diagrams.Select(d => d with { TestRuntimeId = Id(d.TestRuntimeId) }).ToArray(),
            StepPaths = report.StepPaths.ToDictionary(e => Id(e.Key), e => e.Value, StringComparer.Ordinal),
            Annotations = report.Annotations.ToDictionary(e => Id(e.Key), e => e.Value, StringComparer.Ordinal),
            InternalFlowSegments = report.InternalFlowSegments.ToDictionary(e => Id(e.Key), e => e.Value, StringComparer.Ordinal),
            WholeTestFlow = report.WholeTestFlow.ToDictionary(e => Id(e.Key), e => e.Value, StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// What a scenario IS, for the purpose of telling "the same run twice" from "two different tests".
    /// The same value <see cref="ScenarioStableId"/> hashes, without the suite - two shards of one run
    /// share a suite, and a merge that spans suites has already lost it by the time this is asked.
    /// </summary>
    private static string Identity(Feature feature, Scenario scenario) =>
        ScenarioStableId.Compute(null, feature.DisplayName, scenario.DisplayName ?? "", scenario.OutlineId, scenario.ExampleValues);

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
