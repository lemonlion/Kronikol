using System.Globalization;
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
        var (kept, duplicateDiagnostics) = DropWhatWasAlreadySupplied(reports);
        reports = kept;

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
            // The traffic simply concatenates - no dedup here, because a request and its response
            // deliberately share one RequestResponseId. A scenario the merge was given twice has already
            // lost its second copy's traffic above, which is the only duplication there ever was.
            Interactions = reports.SelectMany(r => r.Interactions).ToArray(),
            StepPaths = MergeByScenario(reports.Select(r => r.StepPaths)),
            Annotations = MergeByScenario(reports.Select(r => r.Annotations)),
            Diagnostics = [.. reports.SelectMany(r => r.Diagnostics), .. duplicateDiagnostics, .. DisagreementDiagnostics(reports)]
        };
    }

    /// <summary>
    /// Unions per-scenario side tables. Runtime ids are unique across the merge once
    /// <see cref="DisambiguateRuntimeIds"/> has run, so a collision is two entries for one scenario and
    /// the first wins, matching how the scenarios themselves merge.
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
    /// one, with its scenarios concatenated. Feature order is alphabetical to match the report's own
    /// ordering. Nothing is deduplicated here: <see cref="DropWhatWasAlreadySupplied"/> has already
    /// removed every scenario the merge was given twice, together with everything hung off it.
    /// </summary>
    private static Feature[] MergeFeatures(IReadOnlyList<MergeableReport> reports)
    {
        var byName = new Dictionary<string, Feature>(StringComparer.Ordinal);

        foreach (var feature in reports.SelectMany(r => r.Features))
        {
            if (!byName.TryGetValue(feature.DisplayName, out var existing))
            {
                byName[feature.DisplayName] = feature;
                continue;
            }

            byName[feature.DisplayName] = existing with
            {
                Scenarios = [.. existing.Scenarios, .. feature.Scenarios],
                Endpoint = existing.Endpoint ?? feature.Endpoint,
                SourceFile = existing.SourceFile ?? feature.SourceFile,
                Description = existing.Description ?? feature.Description,
                Labels = existing.Labels ?? feature.Labels
            };
        }

        return byName.Keys.OrderBy(n => n, StringComparer.Ordinal).Select(n => byName[n]).ToArray();
    }

    /// <summary>
    /// Drops every scenario the merge has already been given, and every shard that had nothing else.
    /// </summary>
    /// <remarks>
    /// <para>Scenarios used to be deduplicated where the features were unioned, and only there. The
    /// second copy's traffic, diagrams and component-diagram counts were not: the same shard supplied
    /// twice - a CI artifact downloaded into two folders, yesterday's merged file left in the directory
    /// being merged, the merge's own output fed back in - kept one scenario and gave it every interaction
    /// twice, two identical diagrams, and a component diagram reading "16 calls across 6 tests" where the
    /// run made 8 across 3. Nothing said so. Dropping the copy in one place, with everything keyed to it,
    /// is what makes the rest of the merge a plain concatenation.</para>
    ///
    /// <para>What tells a copy from a second run is the scenario's content, not its runtime id. The id
    /// looked like the obvious key and the invariant written beside it said "a scenario id belongs to
    /// exactly one shard, so a collision means the same runner's output was supplied twice". That is
    /// false for the frameworks most likely to be sharded: NUnit's id is a per-process sequential counter
    /// (this repository's own NUnit report contains <c>"id": "0-1002"</c>), so every shard process mints
    /// the same ids for entirely different tests, and a shard whose test failed merged green.</para>
    ///
    /// <para>And the content includes how the run ENDED. Two shards that both ran one scenario - an
    /// overlapping partition, a failed shard re-run beside its first attempt - are two runs, with
    /// different results or (almost always) different durations, and both are kept: first-wins would
    /// hide whichever one failed. A copy has the same result, timing and message, because it is the same
    /// bytes. Retries inside one shard differ by attempt, and stay, as they do in the single-run report.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<MergeableReport> Kept, IReadOnlyList<DiagnosticEntry> Diagnostics) DropWhatWasAlreadySupplied(
        IReadOnlyList<MergeableReport> reports)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var shardsThatRan = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var kept = new List<MergeableReport>(reports.Count);
        var dropped = 0;
        var skippedShards = 0;

        for (var index = 0; index < reports.Count; index++)
        {
            var report = reports[index];
            var survivors = 0;
            var survivorIds = new HashSet<string>(StringComparer.Ordinal);
            var duplicateIds = new HashSet<string>(StringComparer.Ordinal);
            var features = new List<Feature>(report.Features.Length);

            foreach (var feature in report.Features)
            {
                var scenarios = new List<Scenario>(feature.Scenarios.Length);
                foreach (var scenario in feature.Scenarios)
                {
                    if (!seen.Add(Identity(report.Suite, feature, scenario)))
                    {
                        duplicateIds.Add(scenario.Id);
                        continue;
                    }

                    scenarios.Add(scenario);
                    survivors++;
                    survivorIds.Add(scenario.Id);

                    var what = WhatItIs(report.Suite, feature, scenario);
                    if (!shardsThatRan.TryGetValue(what, out var shards))
                        shardsThatRan[what] = shards = [];
                    shards.Add(index);
                }

                if (scenarios.Count == feature.Scenarios.Length)
                    features.Add(feature);
                else if (scenarios.Count > 0)
                    features.Add(feature with { Scenarios = scenarios.ToArray() });
            }

            if (duplicateIds.Count == 0)
            {
                kept.Add(report);
                continue;
            }

            dropped += report.Features.Sum(f => f.Scenarios.Length) - survivors;
            if (survivors == 0)
            {
                // The whole shard had been seen: its relationships, its diagnostics, everything. It is
                // the same runner's output twice, and the second time contributes nothing.
                skippedShards++;
                continue;
            }

            // Everything on the side travels by runtime id. Within one shard two scenarios can share an
            // id (a retry under NUnit), so an id a survivor still uses is left alone - stripping it would
            // take the survivor's own calls with it.
            duplicateIds.ExceptWith(survivorIds);
            kept.Add(report with
            {
                Features = features.ToArray(),
                Interactions = report.Interactions.Where(i => !duplicateIds.Contains(i.TestId)).ToArray(),
                Diagrams = report.Diagrams.Where(d => !duplicateIds.Contains(d.TestRuntimeId)).ToArray(),
                StepPaths = report.StepPaths.Where(e => !duplicateIds.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal),
                Annotations = report.Annotations.Where(e => !duplicateIds.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal),
                WholeTestFlow = report.WholeTestFlow.Where(e => !duplicateIds.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal)
            });
        }

        var diagnostics = new List<DiagnosticEntry>();
        if (dropped > 0)
            diagnostics.Add(new DiagnosticEntry(
                DiagnosticKind.Other,
                $"{dropped} scenario(s) were supplied twice with the same result and timing - the same runner's output given to the merge more than once - and were counted once"
                + (skippedShards > 0 ? $"; {skippedShards} shard(s) contained nothing else and were skipped." : ".")));

        var overlapping = shardsThatRan.Count(entry => entry.Value.Count > 1);
        if (overlapping > 0)
            diagnostics.Add(new DiagnosticEntry(
                DiagnosticKind.Other,
                $"{overlapping} scenario(s) ran in more than one shard with different results or timings; every run of them is kept, "
                + "so the merged report holds more scenarios than the suite. Shards of one run should partition it."));

        return (kept, diagnostics);
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
    /// tests. Once the merge stopped deleting the second of those, both survived under one id and each
    /// of them acquired ALL of both scenarios' calls.</para>
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
                var identity = Identity(report.Suite, feature, scenario);
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
    /// What a scenario IS: the value <see cref="ScenarioStableId"/> hashes, under the suite the shard
    /// recorded. The suite is part of it because two suites can each hold an "Orders / Place order" and
    /// those are two scenarios, not one that ran twice - which is also why the merged report drops the
    /// suite when the shards disagree rather than picking one.
    /// </summary>
    private static string WhatItIs(string? suite, Feature feature, Scenario scenario) =>
        ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName ?? "", scenario.OutlineId, scenario.ExampleValues);

    /// <summary>
    /// What a scenario is AND how its run went - the key that tells "the same run twice" from "two runs of
    /// the same scenario". Two shards that both ran a scenario differ in result, attempt, duration or
    /// message, almost always in the duration alone; a copy of one shard differs in none of them.
    /// </summary>
    private static string Identity(string? suite, Feature feature, Scenario scenario) =>
        string.Join((char)31,
            WhatItIs(suite, feature, scenario),
            scenario.Attempt?.ToString(CultureInfo.InvariantCulture) ?? "",
            scenario.Result.ToString(),
            scenario.Duration?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "",
            scenario.ErrorMessage ?? "");

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
    /// <remarks>
    /// A shard's relationships are the counts its own run computed, under its own participant filter,
    /// and cannot be recomputed here from the traffic without that filter - which is why they are summed
    /// rather than rebuilt. The sum is exact once <see cref="DropWhatWasAlreadySupplied"/> has removed
    /// every shard that was the same runner's output twice: that was the case in which one label read
    /// "16 calls across 6 tests" for a run that made 8 across 3.
    /// </remarks>
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
        // First wins, matching MergeByScenario and matching how the scenarios themselves merge. This was
        // last-wins, so one surviving scenario could take its verdict, steps and annotations from the
        // first shard and its internal-flow segment and whole-test flow from the second - a chimera of
        // two runs under one id, with no rule saying which half came from where. Ids are unique across
        // disjoint runners and DisambiguateRuntimeIds now makes that true even when the frameworks
        // disagree, so in practice nothing collides; where something does, one shard is better than two.
        var result = new Dictionary<string, T>();
        foreach (var map in maps)
            foreach (var kvp in map)
                result.TryAdd(kvp.Key, kvp.Value);
        return result;
    }

    /// <summary>
    /// What the merge itself has to report about the merge: that the shards did not agree on the
    /// environment, the suite or the CI run, so the merged file records none, or the first.
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
        if (environments.Length > 1)
        {
            var described = environments.Select(e => e is null ? "(not recorded)" : $"{e.Os} / {e.Runtime}");

            yield return new DiagnosticEntry(
                DiagnosticKind.Other,
                $"Merged {reports.Count} reports that do not agree on the environment, so the merged report records none. "
                + "Seen: " + string.Join("; ", described) + ".");
        }

        // Dropping the suite is not a cosmetic loss. ScenarioStableId folds it into the hash, so a
        // merged report with no suite re-keys EVERY scenario in it - the ids match neither the shards
        // it came from nor a baseline promoted from an earlier run of the same suite, and `diff` then
        // reports every scenario as both new and gone. That happened in silence.
        var suites = reports.Select(r => r.Suite).Distinct(StringComparer.Ordinal).ToArray();
        if (suites.Length > 1)
            yield return new DiagnosticEntry(
                DiagnosticKind.Other,
                $"Merged {reports.Count} reports from {suites.Length} different suites, so the merged report records none "
                + "and every stableId in it is computed without one — they will not match either the shards or a baseline. "
                + "Seen: " + string.Join("; ", suites.Select(x => x is null ? "(not recorded)" : x)) + ".");

        // Run identity, same argument. The first shard's commit and branch were taken for the whole
        // merge with nothing said, so a merge of artifacts from two builds looked like one build.
        var runs = reports.Select(r => r.CiMetadata)
            .Where(m => m is not null)
            .Select(m => $"{m!.CommitSha ?? "(no commit)"}@{m.Branch ?? "(no branch)"}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (runs.Length > 1)
            yield return new DiagnosticEntry(
                DiagnosticKind.Other,
                $"Merged reports from {runs.Length} different CI runs; the merged report records the first. "
                + "Seen: " + string.Join("; ", runs) + ".");
    }

    /// <summary>
    /// Picks the first report that captured CI metadata. Runners in the same workflow share repository,
    /// branch and commit, so the first non-null record represents the combined run; when they do not,
    /// <see cref="DisagreementDiagnostics"/> says so.
    /// </summary>
    private static CiMetadata? ReconcileCiMetadata(IReadOnlyList<MergeableReport> reports) =>
        reports.Select(r => r.CiMetadata).FirstOrDefault(m => m is not null);
}
