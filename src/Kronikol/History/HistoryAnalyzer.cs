using System.Globalization;

namespace Kronikol.History;

/// <summary>
/// The verdicts: pure arithmetic over the last N runs of one suite in one branch stream
/// (plans/CROSS_RUN_HISTORY_PLAN.md §2, §7). No I/O, no clock — the ledger, the current run and the
/// options go in, and the same inputs always give the same verdicts, which is what makes them testable
/// and what lets the tool and the report agree.
///
/// <para><b>Flip rate, not fail rate.</b> A scenario that failed five of ten in one contiguous block
/// flipped once: it broke and was fixed. One that failed five of ten alternating flipped nine times: it
/// is flaky. Same fail rate, opposite verdicts, and the fail rate cannot tell them apart (§7.2).</para>
///
/// <para><b>Only a pass or a fail is a verdict.</b> Skipping a test for a week neither creates nor hides
/// flakiness; a defaulted result is not a verdict at all.</para>
/// </summary>
public static class HistoryAnalyzer
{
    private static readonly HistoryVerdictKind[] PrecedenceOrder =
    [
        HistoryVerdictKind.Flaky,
        HistoryVerdictKind.Broke,
        HistoryVerdictKind.Failing,
        HistoryVerdictKind.AlwaysFailing,
        HistoryVerdictKind.Fixed,
        HistoryVerdictKind.BehaviourChanged,
        HistoryVerdictKind.Slower,
        HistoryVerdictKind.Reordered,
        HistoryVerdictKind.UnstableShape,
        HistoryVerdictKind.New,
        HistoryVerdictKind.Unknown,
        HistoryVerdictKind.Quarantined,
        HistoryVerdictKind.Stable
    ];

    /// <summary>
    /// The order a surface leads with. Flaky first even over broke: the reader's question about a
    /// failing scenario is whether to chase it, and "this one flips" answers it. Quarantined and unknown
    /// are additive and never lead unless nothing else applies.
    /// </summary>
    public static int Precedence(HistoryVerdictKind kind) => Array.IndexOf(PrecedenceOrder, kind);

    /// <summary>Analyses the current run against its stream in the ledger.</summary>
    /// <param name="ledger">The ledger, read with at least the window.</param>
    /// <param name="roster">The current run's roster.</param>
    /// <param name="current">The current run.</param>
    /// <param name="options">Thresholds and streams.</param>
    /// <param name="quarantine">The quarantine list, when there is one.</param>
    /// <param name="aliases">The rename aliases, when there are any.</param>
    /// <param name="today">The date quarantine expiry is judged on; the current run's date when null.</param>
    public static HistoryVerdicts Analyse(HistoryLedger ledger, HistoryRoster roster, HistoryRun current, HistoryAnalysisOptions options,
        HistoryQuarantineList? quarantine = null, HistoryAliases? aliases = null, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(options);

        var stream = options.Branch ?? current.Stream;
        var result = AnalyseStream(ledger, roster, current, options, stream, quarantine, aliases, today ?? DateOnly.FromDateTime(current.At.UtcDateTime));
        if (options.CompareBranch is { Length: > 0 } compare && !string.Equals(compare, stream, StringComparison.Ordinal))
            result = result with { Compare = AnalyseStream(ledger, roster, current, options, compare, quarantine, aliases, today ?? DateOnly.FromDateTime(current.At.UtcDateTime)) };
        return result;
    }

    private static HistoryVerdicts AnalyseStream(HistoryLedger ledger, HistoryRoster roster, HistoryRun current, HistoryAnalysisOptions options,
        string stream, HistoryQuarantineList? quarantine, HistoryAliases? aliases, DateOnly today)
    {
        // The prior runs: the stream's, the current run excluded (it may already be appended), the last
        // `window` of them, oldest first.
        var prior = ledger.Runs(current.Suite)
            .Where(r => string.Equals(r.Stream, stream, StringComparison.Ordinal) && !string.Equals(r.Id, current.Id, StringComparison.Ordinal))
            .ToList();
        if (options.Window > 0 && prior.Count > options.Window)
            prior = prior.Skip(prior.Count - options.Window).ToList();

        var priorRosters = prior.Select(r => ledger.Roster(r.RosterHash)).ToArray();
        var previousFull = LastFull(prior, priorRosters);
        var partial = current.Partial ?? IsPartial(roster, previousFull.Roster, options.PartialThreshold);

        var scenarios = new List<ScenarioHistory>(roster.Count);
        var currentIds = new HashSet<(string, int)>();
        for (var i = 0; i < roster.Count; i++)
        {
            currentIds.Add((roster.Ids[i], roster.Slots[i]));
            scenarios.Add(AnalyseScenario(i, roster, current, prior, priorRosters, options, quarantine, aliases, today));
        }

        // Absent: in the previous full run of the stream, not in this one, and this one not partial. The
        // previous run only, deliberately — a scenario deleted a month ago is not news fifty times.
        var absent = new List<AbsentScenario>();
        if (!partial && previousFull.Roster is { } previousRoster && previousFull.Run is { } previousRun)
        {
            for (var i = 0; i < previousRoster.Count; i++)
            {
                var id = aliases?.Current(previousRoster.Ids[i]) ?? previousRoster.Ids[i];
                if (!currentIds.Contains((id, previousRoster.Slots[i])) && !currentIds.Contains((previousRoster.Ids[i], previousRoster.Slots[i])))
                    absent.Add(new AbsentScenario(previousRoster.Ids[i], previousRoster.Names[i], previousRoster.Features[i], previousRun.Id));
            }
        }

        var counts = new Dictionary<HistoryVerdictKind, int>();
        foreach (var scenario in scenarios)
            foreach (var kind in scenario.Verdicts)
                counts[kind] = counts.GetValueOrDefault(kind) + 1;

        var runs = prior.Select(r => RunPointOf(r)).ToList();
        runs.Add(RunPointOf(current) with { Partial = partial });

        var priorDeps = new HashSet<string>(prior.SelectMany(r => r.Deps ?? []), StringComparer.Ordinal);
        var newDeps = prior.Count == 0 ? [] : (current.Deps ?? []).Where(d => !priorDeps.Contains(d)).OrderBy(d => d, StringComparer.Ordinal).ToArray();

        var coldStart = prior.Count < options.MinRuns;
        return new HistoryVerdicts
        {
            Suite = current.Suite,
            Stream = stream,
            RunId = current.Id,
            RunsRecorded = prior.Count,
            MinRuns = options.MinRuns,
            ColdStart = coldStart,
            ColdStartMessage = coldStart
                ? $"{prior.Count.ToString(CultureInfo.InvariantCulture)} run{(prior.Count == 1 ? "" : "s")} recorded in the {stream} stream; flakiness and duration verdicts need {options.MinRuns.ToString(CultureInfo.InvariantCulture)}"
                : null,
            Partial = partial,
            Scenarios = scenarios,
            Absent = absent,
            Counts = counts,
            Runs = runs,
            NewDependencies = newDeps,
            FirstSeen = counts.GetValueOrDefault(HistoryVerdictKind.New),
            Compare = null
        };
    }

    /// <summary>The last prior run that was not partial, with its roster; the pair is null when there is none.</summary>
    private static (HistoryRun? Run, HistoryRoster? Roster) LastFull(List<HistoryRun> prior, HistoryRoster?[] rosters)
    {
        for (var i = prior.Count - 1; i >= 0; i--)
            if (prior[i].Partial != true && rosters[i] is { } roster)
                return (prior[i], roster);
        return (null, null);
    }

    /// <summary>
    /// The partial heuristic (§5.12): the run lacks more than the threshold's share of the previous
    /// full run's roster. A filtered run — one test in the debugger — must not declare the other 299
    /// absent, and must not be the run "fixed" is measured against.
    /// </summary>
    public static bool IsPartial(HistoryRoster roster, HistoryRoster? previous, double threshold)
    {
        if (previous is null || previous.Count == 0)
            return false;
        var currentIds = new HashSet<string>(roster.Ids, StringComparer.Ordinal);
        var missing = previous.Ids.Distinct(StringComparer.Ordinal).Count(id => !currentIds.Contains(id));
        var distinct = previous.Ids.Distinct(StringComparer.Ordinal).Count();
        return distinct > 0 && (double)missing / distinct > threshold;
    }

    private static RunPoint RunPointOf(HistoryRun run)
    {
        var passed = run.Results.Count(c => c == HistoryFormat.Passed);
        var failed = run.Results.Count(c => c == HistoryFormat.Failed);
        long? duration = run.Durations is { } durations && durations.Any(d => d.HasValue) ? durations.Sum(d => (long)(d ?? 0)) : null;
        return new RunPoint(run.Id, run.At, run.Commit, passed, failed, run.Results.Length, duration, run.Partial == true);
    }

    private static ScenarioHistory AnalyseScenario(int position, HistoryRoster roster, HistoryRun current, List<HistoryRun> prior, HistoryRoster?[] priorRosters,
        HistoryAnalysisOptions options, HistoryQuarantineList? quarantine, HistoryAliases? aliases, DateOnly today)
    {
        var id = roster.Ids[position];
        var slot = roster.Slots[position];
        var lookFor = aliases?.AllIdsOf(id) ?? [id];

        // Every prior reading of this scenario, oldest first.
        var points = new List<HistoryPoint>();
        for (var r = 0; r < prior.Count; r++)
        {
            var run = prior[r];
            var priorRoster = priorRosters[r];
            if (priorRoster is null) continue;
            var at = -1;
            foreach (var candidate in lookFor)
            {
                at = priorRoster.IndexOf(candidate, slot);
                if (at >= 0) break;
            }
            if (at < 0) continue;
            points.Add(new HistoryPoint(run.Id, run.At, run.Commit, run.ResultAt(at), run.DurationAt(at), run.ShapeSetAt(at), run.ShapeOrderedAt(at), run.CallsAt(at), run.ErrorAt(at), run.AttemptAt(at), run.ShapeVersion));
        }

        var currentPoint = new HistoryPoint(current.Id, current.At, current.Commit, current.ResultAt(position), current.DurationAt(position),
            current.ShapeSetAt(position), current.ShapeOrderedAt(position), current.CallsAt(position), current.ErrorAt(position), current.AttemptAt(position), current.ShapeVersion);
        var all = points.Append(currentPoint).ToList();

        var verdicts = new HashSet<HistoryVerdictKind>();
        var evidence = new List<string>();

        // ── Status ──────────────────────────────────────────
        var real = all.Where(p => HistoryFormat.IsRealVerdict(p.Result)).ToList();
        var realPrior = real.Where(p => !ReferenceEquals(p, currentPoint)).ToList();
        var failures = real.Count(p => p.Result == HistoryFormat.Failed);
        var flips = 0;
        for (var i = 1; i < real.Count; i++)
            if (real[i].Result != real[i - 1].Result) flips++;
        var flipRate = real.Count > 1 ? (double)flips / (real.Count - 1) : 0;
        var failRate = real.Count > 0 ? (double)failures / real.Count : 0;

        var runsSinceLastFlip = 0;
        for (var i = real.Count - 1; i > 0 && real[i].Result == real[i - 1].Result; i--)
            runsSinceLastFlip++;

        int? lastFailedRunsAgo = null;
        for (var i = realPrior.Count - 1; i >= 0; i--)
            if (realPrior[i].Result == HistoryFormat.Failed) { lastFailedRunsAgo = realPrior.Count - i; break; }

        var isNew = prior.Count > 0 && points.Count == 0;
        var currentResult = currentPoint.Result;
        var lastPrior = realPrior.Count > 0 ? realPrior[^1] : null;
        FailingSince? failingSince = null;

        if (currentResult == HistoryFormat.Unknown || !HistoryFormat.IsRealVerdict(currentResult) && currentResult != HistoryFormat.Passed)
        {
            if (currentResult == HistoryFormat.Unknown)
            {
                verdicts.Add(HistoryVerdictKind.Unknown);
                evidence.Add("no verdict in this run: the result was defaulted");
            }
        }

        if (currentResult == HistoryFormat.Failed)
        {
            if (lastPrior is null)
            {
                // Nothing to compare a failure against: no verdict, and never a guess at one.
                if (isNew) evidence.Add("first seen in this stream, failing");
                else
                {
                    verdicts.Add(HistoryVerdictKind.Unknown);
                    evidence.Add(prior.Count == 0
                        ? "no earlier run in this stream to compare against"
                        : $"no earlier pass or fail in the last {prior.Count.ToString(CultureInfo.InvariantCulture)} runs to compare against");
                }
            }
            else if (lastPrior.Result == HistoryFormat.Passed)
            {
                verdicts.Add(HistoryVerdictKind.Broke);
                evidence.Add($"passed in {lastPrior.RunId}{Commit(lastPrior)}, failing now");
            }
            else
            {
                // A streak: walk back through consecutive failures.
                var streakStart = real.Count - 1;
                while (streakStart > 0 && real[streakStart - 1].Result == HistoryFormat.Failed) streakStart--;
                var since = real[streakStart];
                failingSince = new FailingSince(since.RunId, since.At, since.Commit, real.Count - streakStart);
                if (real.All(p => p.Result == HistoryFormat.Failed))
                {
                    verdicts.Add(HistoryVerdictKind.AlwaysFailing);
                    evidence.Add($"failed in every one of the last {real.Count.ToString(CultureInfo.InvariantCulture)} runs with a verdict");
                }
                else
                {
                    verdicts.Add(HistoryVerdictKind.Failing);
                    evidence.Add($"failing since {since.RunId}{Commit(since)}, {failingSince.Runs.ToString(CultureInfo.InvariantCulture)} runs");
                }
            }
        }
        else if (currentResult == HistoryFormat.Passed)
        {
            if (lastPrior is { Result: HistoryFormat.Failed })
            {
                verdicts.Add(HistoryVerdictKind.Fixed);
                var failedStreak = 0;
                for (var i = realPrior.Count - 1; i >= 0 && realPrior[i].Result == HistoryFormat.Failed; i--) failedStreak++;
                evidence.Add($"failed the previous {(failedStreak == 1 ? "run" : failedStreak.ToString(CultureInfo.InvariantCulture) + " runs")}, passing now");
            }
        }

        if (isNew)
            verdicts.Add(HistoryVerdictKind.New);
        if (isNew && currentResult != HistoryFormat.Failed)
            evidence.Add("first seen in this stream");

        // ── Flakiness ───────────────────────────────────────
        // Flaky means it has failed, recovered and failed again: two failing episodes. One break followed
        // by one fix is two flips but one episode, and it is not flakiness however short the window.
        var episodes = 0;
        for (var i = 0; i < real.Count; i++)
            if (real[i].Result == HistoryFormat.Failed && (i == 0 || real[i - 1].Result != HistoryFormat.Failed)) episodes++;
        var passedOnRetry = currentResult == HistoryFormat.Passed && currentPoint.Attempt is > 1;
        var flakyByRate = real.Count >= options.MinRuns && episodes >= 2 && flipRate >= options.FlakyRate;
        if (flakyByRate || passedOnRetry)
        {
            verdicts.Add(HistoryVerdictKind.Flaky);
            var text = $"failed {failures.ToString(CultureInfo.InvariantCulture)} of the last {real.Count.ToString(CultureInfo.InvariantCulture)} runs with a verdict, {flips.ToString(CultureInfo.InvariantCulture)} flips";
            if (lastFailedRunsAgo is { } ago && currentResult != HistoryFormat.Failed)
                text += $", last failed {ago.ToString(CultureInfo.InvariantCulture)} run{(ago == 1 ? "" : "s")} ago";
            if (passedOnRetry)
                text += $", passed on retry {currentPoint.Attempt!.Value.ToString(CultureInfo.InvariantCulture)} in this run";
            evidence.Insert(0, text);
        }

        // ── Duration ────────────────────────────────────────
        int? p95 = null;
        var priorDurations = points.Where(p => p.DurationMs is not null).Select(p => p.DurationMs!.Value).ToList();
        if (priorDurations.Count >= 2)
        {
            // The p95 is taken over the runs before the previous one, so the previous run's own spike
            // does not lift the bar it is measured against.
            var baseline = priorDurations.Take(priorDurations.Count - 1).ToList();
            p95 = Percentile95(baseline);
            if (currentPoint.DurationMs is { } now && baseline.Count >= options.MinRuns && p95 is { } bar && bar > 0)
            {
                var previous = priorDurations[^1];
                if (now > bar * options.SlowerBy && previous > bar * options.SlowerBy)
                {
                    verdicts.Add(HistoryVerdictKind.Slower);
                    evidence.Add($"{now.ToString(CultureInfo.InvariantCulture)} ms now and {previous.ToString(CultureInfo.InvariantCulture)} ms last run, against a p95 of {bar.ToString(CultureInfo.InvariantCulture)} ms over {baseline.Count.ToString(CultureInfo.InvariantCulture)} runs");
                }
            }
        }

        // ── Behaviour ───────────────────────────────────────
        // A fingerprint is comparable only with one the same templating rule made: across a change of
        // rule there is no verdict, and the reader is told, rather than every scenario changing once.
        var rule = currentPoint.ShapeVersion ?? 1;
        var shapedByAnyRule = points.Where(p => p.ShapeSet is { Length: > 0 }).ToList();
        var shaped = shapedByAnyRule.Where(p => (p.ShapeVersion ?? 1) == rule).ToList();
        var previousShaped = shaped.Count > 0 ? shaped[^1] : null;
        if (currentPoint.ShapeSet is { Length: > 0 } && shapedByAnyRule.Count > 0 && (shapedByAnyRule[^1].ShapeVersion ?? 1) != rule)
            evidence.Add($"the calls in {shapedByAnyRule[^1].RunId} were fingerprinted by an earlier rule; behaviour is compared from the next run");
        if (currentPoint.ShapeSet is { Length: > 0 } && previousShaped is not null)
        {
            var changes = 0;
            for (var i = 1; i < shaped.Count; i++)
                if (!string.Equals(shaped[i].ShapeSet, shaped[i - 1].ShapeSet, StringComparison.Ordinal)) changes++;
            var unstable = shaped.Count >= options.MinRuns && changes > (shaped.Count - 1) / 2.0;

            if (unstable)
            {
                verdicts.Add(HistoryVerdictKind.UnstableShape);
                evidence.Add($"the set of calls changed in {changes.ToString(CultureInfo.InvariantCulture)} of the last {(shaped.Count - 1).ToString(CultureInfo.InvariantCulture)} run pairs; behaviour verdicts are suppressed");
            }
            else if (!string.Equals(currentPoint.ShapeSet, previousShaped.ShapeSet, StringComparison.Ordinal))
            {
                var previousResult = previousShaped.Result;
                if (currentResult == previousResult)
                {
                    verdicts.Add(HistoryVerdictKind.BehaviourChanged);
                    var callsText = currentPoint.Calls is { } c && previousShaped.Calls is { } pc && c != pc
                        ? $"calls {pc.ToString(CultureInfo.InvariantCulture)} in {previousShaped.RunId} to {c.ToString(CultureInfo.InvariantCulture)} now"
                        : $"the same number of calls as {previousShaped.RunId}{(currentPoint.Calls is { } n ? " (" + n.ToString(CultureInfo.InvariantCulture) + ")" : "")}, a different set";
                    evidence.Add($"same status, different calls: {callsText}");
                }
            }
            else if (currentPoint.Calls is { } now && previousShaped.Calls is { } before && now != before && currentResult == previousShaped.Result)
            {
                // The same calls, made a different number of times. "3, 3, 3, then 5" is an N+1 regression
                // the set cannot see; "7, 8, 7, then 9" is a retry against a throttled emulator or a
                // consumer's work landing in whichever scenario is running. The scenario's own record
                // tells them apart: the count is a verdict once it has been constant over the minimum
                // runs, and read out otherwise.
                var counted = shaped.Where(p => p.Calls is not null).ToList();
                var countText = $"calls {before.ToString(CultureInfo.InvariantCulture)} in {previousShaped.RunId} to {now.ToString(CultureInfo.InvariantCulture)} now";
                if (counted.Count >= options.MinRuns && counted.All(p => p.Calls == before))
                {
                    verdicts.Add(HistoryVerdictKind.BehaviourChanged);
                    evidence.Add($"the same calls made a different number of times: {countText}, constant over the last {counted.Count.ToString(CultureInfo.InvariantCulture)} runs");
                }
                else if (counted.Count < options.MinRuns)
                {
                    evidence.Add($"{countText}; a count verdict needs {options.MinRuns.ToString(CultureInfo.InvariantCulture)} runs with the count constant");
                }
                else
                {
                    evidence.Add($"{countText}; the count varies run to run for this scenario, so it is not read as behaviour");
                }
            }
            else if (options.ReportReordered && currentPoint.ShapeOrdered is { Length: > 0 } && previousShaped.ShapeOrdered is { Length: > 0 }
                     && !string.Equals(currentPoint.ShapeOrdered, previousShaped.ShapeOrdered, StringComparison.Ordinal))
            {
                verdicts.Add(HistoryVerdictKind.Reordered);
                evidence.Add($"the same calls as {previousShaped.RunId} in a different order");
            }
        }

        // ── Quarantine ──────────────────────────────────────
        var entry = quarantine?.Find(id, today);
        if (entry is not null)
        {
            verdicts.Add(HistoryVerdictKind.Quarantined);
            evidence.Add($"quarantined: {entry.Reason}");
        }

        if (verdicts.Count == 0)
        {
            verdicts.Add(HistoryVerdictKind.Stable);
            evidence.Add(real.Count > 1
                ? $"passed in every one of the last {real.Count.ToString(CultureInfo.InvariantCulture)} runs with a verdict"
                : currentResult == HistoryFormat.Passed ? "passing" : "no verdict to compare");
        }

        var primary = verdicts.OrderBy(Precedence).First();
        var series = string.Concat(all.TakeLast(10).Select(p => p.Result));

        return new ScenarioHistory
        {
            StableId = id,
            Slot = slot,
            Name = roster.Names[position],
            Feature = roster.Features[position],
            Current = currentResult,
            Primary = primary,
            Verdicts = verdicts,
            Evidence = string.Join("; ", evidence),
            RunsSeen = points.Count,
            RealVerdicts = real.Count,
            Failures = failures,
            FailRate = failRate,
            Flips = flips,
            FlipRate = flipRate,
            RunsSinceLastFlip = runsSinceLastFlip,
            LastFailedRunsAgo = lastFailedRunsAgo,
            FailingSince = failingSince,
            Series = series,
            Points = all,
            DurationMs = currentPoint.DurationMs,
            DurationP95 = p95,
            PreviousShapeSet = previousShaped?.ShapeSet,
            ShapeSet = currentPoint.ShapeSet,
            PreviousCalls = previousShaped?.Calls,
            Calls = currentPoint.Calls,
            Quarantine = entry
        };
    }

    private static string Commit(HistoryPoint point) => point.Commit is { Length: > 0 } commit ? $" ({Short(commit)})" : "";

    private static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;

    /// <summary>The nearest-rank 95th percentile.</summary>
    internal static int Percentile95(IReadOnlyList<int> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var rank = (int)Math.Ceiling(0.95 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}
