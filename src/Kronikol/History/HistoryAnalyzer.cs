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
public static partial class HistoryAnalyzer
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
        HistoryVerdictKind.Alternating,
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
    /// <param name="ledger">The ledger. Its read window does not bound the analysis: a stream's runs are read back as far as <see cref="HistoryAnalysisOptions.Window"/> asks.</param>
    /// <param name="roster">The current run's roster.</param>
    /// <param name="current">The current run.</param>
    /// <param name="options">Thresholds and streams.</param>
    /// <param name="quarantine">The quarantine list, when there is one.</param>
    /// <param name="aliases">The rename aliases, when there are any.</param>
    /// <param name="today">The date quarantine expiry is judged on; the current run's date when null.</param>
    /// <param name="shapes">The current run's distinct calls, which its line indexes into; null when not recorded.</param>
    public static HistoryVerdicts Analyse(HistoryLedger ledger, HistoryRoster roster, HistoryRun current, HistoryAnalysisOptions options,
        HistoryQuarantineList? quarantine = null, HistoryAliases? aliases = null, DateOnly? today = null, HistoryShapes? shapes = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(options);

        var stream = options.Branch ?? current.Stream;
        var result = AnalyseStream(ledger, roster, current, options, stream, quarantine, aliases, today ?? DateOnly.FromDateTime(current.At.UtcDateTime), shapes);
        if (options.CompareBranch is { Length: > 0 } compare && !string.Equals(compare, stream, StringComparison.Ordinal))
            result = result with { Compare = AnalyseStream(ledger, roster, current, options, compare, quarantine, aliases, today ?? DateOnly.FromDateTime(current.At.UtcDateTime), shapes) };
        return result;
    }

    private static HistoryVerdicts AnalyseStream(HistoryLedger ledger, HistoryRoster roster, HistoryRun current, HistoryAnalysisOptions options,
        string stream, HistoryQuarantineList? quarantine, HistoryAliases? aliases, DateOnly today, HistoryShapes? shapes)
    {
        // The prior runs: the last `window` of the stream that were appended before the current run's own
        // line, oldest first. The line may already be there, and on a later read of the same report there
        // are runs after it: those are not its history (#95).
        var prior = ledger.PriorRuns(current.Suite, stream, current.Id, options.Window).ToList();

        var priorRosters = prior.Select(r => ledger.Roster(r.RosterHash)).ToArray();
        var priorPositions = PositionsOf(priorRosters);
        var priorShapes = prior.Select(r => r.ShapesHash is { } hash ? ledger.Shapes(hash) : null).ToArray();
        var previousFull = LastFull(prior, priorRosters);
        var partial = current.Partial ?? IsPartial(roster, previousFull.Roster, options.PartialThreshold);

        // A partial run's speed is the median of whichever scenarios its filter left, so a full run is not
        // read against it: measured, the same scenarios' bars stood at 1.88x in a filtered run from the mix
        // alone.
        var speeds = new RunSpeeds((partial ? prior : prior.Where(r => r.Partial != true)).Append(current));
        var paces = new RunPaces(prior, priorRosters, current, roster, partial, aliases, options);
        var scenarios = new List<ScenarioHistory>(roster.Count);
        var currentIds = new HashSet<(string, int)>();
        for (var i = 0; i < roster.Count; i++)
        {
            currentIds.Add((roster.Ids[i], roster.Slots[i]));
            scenarios.Add(AnalyseScenario(i, roster, current, prior, priorPositions, priorShapes, shapes, speeds, options, quarantine, aliases, today, partial, paces));
        }

        // Absent: in the previous full run of the stream, not in this one, and this one not partial. The
        // previous run only, deliberately — a scenario deleted a month ago is not news fifty times.
        var absent = new List<AbsentScenario>();
        if (!partial && previousFull.Roster is { } previousRoster && previousFull.Run is { } previousRun)
        {
            for (var i = 0; i < previousRoster.Count; i++)
            {
                // The fold scenario exists only in the runs where unattributed traffic survived.
                if (previousRun.ResultAt(i) == HistoryFormat.NotATest) continue;
                var id = aliases?.Current(previousRoster.Ids[i]) ?? previousRoster.Ids[i];
                if (!currentIds.Contains((id, previousRoster.Slots[i])) && !currentIds.Contains((previousRoster.Ids[i], previousRoster.Slots[i])))
                    absent.Add(new AbsentScenario(previousRoster.Ids[i], previousRoster.Names[i], previousRoster.Features[i], previousRun.Id));
            }
        }

        var counts = new Dictionary<HistoryVerdictKind, int>();
        foreach (var scenario in scenarios)
            foreach (var kind in scenario.Verdicts)
                counts[kind] = counts.GetValueOrDefault(kind) + 1;

        var runs = prior.Select((r, i) => RunPointOf(r) with { Pace = paces.Of(i), Degraded = paces.Degraded(i) }).ToList();
        runs.Add(RunPointOf(current) with { Partial = partial, Pace = paces.Of(prior.Count), Degraded = paces.Degraded(prior.Count) });

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

    /// <summary>
    /// Where each scenario sits in each prior roster, built once per distinct roster: a window of fifty
    /// runs usually shares one or two. Looking a scenario up by scanning the roster made the analysis
    /// quadratic in the scenario count (#91). The first holder of an (id, slot) wins, as the scan had it.
    /// Held here for the length of one analysis and not on the roster: it is a record, so a cached field
    /// would join its equality and be copied by <c>with</c>.
    /// </summary>
    private static Dictionary<(string Id, int Slot), int>?[] PositionsOf(HistoryRoster?[] rosters)
    {
        var built = new Dictionary<HistoryRoster, Dictionary<(string Id, int Slot), int>>(ReferenceEqualityComparer.Instance);
        var result = new Dictionary<(string Id, int Slot), int>?[rosters.Length];
        for (var r = 0; r < rosters.Length; r++)
        {
            if (rosters[r] is not { } roster) continue;
            if (!built.TryGetValue(roster, out var positions))
            {
                positions = new Dictionary<(string Id, int Slot), int>(roster.Count);
                for (var i = 0; i < roster.Count; i++)
                    positions.TryAdd((roster.Ids[i], roster.Slots[i]), i);
                built[roster] = positions;
            }
            result[r] = positions;
        }
        return result;
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

    private static ScenarioHistory AnalyseScenario(int position, HistoryRoster roster, HistoryRun current, List<HistoryRun> prior, Dictionary<(string Id, int Slot), int>?[] priorPositions,
        HistoryShapes?[] priorShapes, HistoryShapes? currentShapes, RunSpeeds speeds, HistoryAnalysisOptions options, HistoryQuarantineList? quarantine, HistoryAliases? aliases, DateOnly today,
        bool currentIsPartial, RunPaces paces)
    {
        var id = roster.Ids[position];
        var slot = roster.Slots[position];
        var lookFor = aliases?.AllIdsOf(id) ?? [id];

        if (current.ResultAt(position) == HistoryFormat.NotATest)
            return NotATest(position, roster, current, currentIsPartial);

        // Every prior reading of this scenario, oldest first.
        var usual = paces.UsualOf(id, slot);
        var points = new List<HistoryPoint>(prior.Count);
        var sources = new List<(int Run, int At)>(prior.Count);
        for (var r = 0; r < prior.Count; r++)
        {
            var run = prior[r];
            var positions = priorPositions[r];
            if (positions is null) continue;
            var at = -1;
            foreach (var candidate in lookFor)
            {
                if (positions.TryGetValue((candidate, slot), out var held))
                {
                    at = held;
                    break;
                }
            }
            if (at < 0) continue;
            points.Add(new HistoryPoint(run.Id, run.At, run.Commit, run.ResultAt(at), run.DurationAt(at), run.ShapeSetAt(at), run.ShapeOrderedAt(at), run.CallsAt(at), run.ErrorAt(at), run.AttemptAt(at), run.ShapeVersion,
                run.Partial == true,
                paces.TimesUsual(usual, r, run.DurationAt(at), run.ResultAt(at)), paces.Degraded(r), ShapeRules: run.ShapeRules).WithOverUsual(options));
            sources.Add((r, at));
        }

        var currentPoint = new HistoryPoint(current.Id, current.At, current.Commit, current.ResultAt(position), current.DurationAt(position),
            current.ShapeSetAt(position), current.ShapeOrderedAt(position), current.CallsAt(position), current.ErrorAt(position), current.AttemptAt(position), current.ShapeVersion,
            currentIsPartial,
            paces.TimesUsual(usual, prior.Count, current.DurationAt(position), current.ResultAt(position)), paces.Degraded(prior.Count), ShapeRules: current.ShapeRules).WithOverUsual(options);

        // The calls a point made, spelled out: asked for the points a changed set is named from, and no
        // others. Spelling out every point of every scenario was a third of the analysis (#91). By
        // reference, because a point is a record and two readings can be equal field for field.
        IReadOnlyList<string>? CallSetOf(HistoryPoint point)
        {
            if (ReferenceEquals(point, currentPoint))
                return Resolve(current.CallSetAt(position), currentShapes);
            var index = points.FindIndex(p => ReferenceEquals(p, point));
            if (index < 0) return null;
            var (r, at) = sources[index];
            return Resolve(prior[r].CallSetAt(at), priorShapes[r]);
        }
        var all = points.Append(currentPoint).ToList();

        var verdicts = new HashSet<HistoryVerdictKind>();
        var evidence = new List<string>();
        IReadOnlyList<string> newCalls = [];
        IReadOnlyList<string> goneCalls = [];

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
            // Still flaky: a failure in a degraded run is a failure. But a reader deciding whether to chase
            // it should hear first that the record was made on bad days.
            if (failures > 0 && real.Where(p => p.Result == HistoryFormat.Failed).All(p => p.RunDegraded))
                text = "every failure was in a degraded run; " + text;
            evidence.Insert(0, text);
        }

        // A failure inside a run where everything was slow is weak evidence against the test (#83). What
        // is said is a fact about the run, and only the run-level signal earns a causal word: a failing
        // test is usually slow because it failed, so its own reading over its usual explains nothing.
        if (currentResult == HistoryFormat.Failed && currentPoint.RunDegraded && paces.Of(prior.Count) is { } pace)
            evidence.Add($"this run was degraded: its passing scenarios took {pace.ToString("0.0", CultureInfo.InvariantCulture)}× their usual");

        // ── Duration ────────────────────────────────────────
        // A scenario is slower when it got slower than its run did. Every duration is read against the
        // speed of its run — the median of the OTHER scenarios' durations in that run — so a slow runner
        // lifts the bar with the readings. Measured on a consumer's CI before this: a lane read eighteen
        // scenarios slower on a healthy day because the runner was. A roster of one has no others and
        // is read raw.
        //
        // A partial run's pass or fail is a fact about the scenario; its duration relative to the run and
        // its set of captured calls are facts about the run's conditions (a filtered run is alone on the
        // machine, or pays the cold start with fewer scenarios to spread it over; with one worker running,
        // window attribution is suddenly exclusive for everything). So a full run is read against full
        // runs, here and under Behaviour below, and status above reads every run.
        int? p95 = null;
        var p95IsRaw = false;
        var comparable = currentIsPartial ? points : points.Where(p => !p.Partial).ToList();
        // A degraded run's durations are facts about the machine, as a partial run's are about the filter.
        // Under contention the slowdown is nowhere near uniform (measured in one run: p25 2x, p90 54x), so
        // reading each scenario against its run's median cannot absorb it, and every scenario whose
        // previous reading happened to sit over its bar was handed a slower it did not earn. Left in the
        // baseline it costs again: a nearest-rank p95 of a dozen readings is their maximum, so one degraded
        // run lifts the bar, and hides a real slowdown, for as long as it stays in the window. So no slower
        // is read in a degraded run, and a run that is not degraded is not read against one. Pass and fail
        // are never discounted: a failure in a degraded run is still a fact about the test.
        var currentIsDegraded = currentPoint.RunDegraded;
        var timed = (currentIsDegraded ? comparable : comparable.Where(p => !p.RunDegraded)).Where(p => p.DurationMs is not null)
            .Select(p => (Ms: p.DurationMs!.Value, Relative: p.DurationMs!.Value / speeds.Of(p.RunId, p.DurationMs!.Value)))
            .ToList();
        if (currentIsPartial)
        {
            // This run's speed is the median of an arbitrary subset: nothing is scaled to it and no slower
            // verdict is read. The bar is the plain p95 of what the scenario took in full runs - all of
            // them: the previous reading is held out of a scaled bar so that its own spike cannot lift the
            // bar it is judged against, and nothing is judged here.
            var raw = points.Where(p => !p.Partial && p.DurationMs is not null).Select(p => p.DurationMs!.Value).ToList();
            if (raw.Count >= 2)
            {
                p95 = Percentile95(raw);
                p95IsRaw = true;
            }
        }
        else if (timed.Count >= 2)
        {
            // The p95 is taken over the runs before the previous one, so the previous run's own spike
            // does not lift the bar it is measured against.
            var baseline = timed.Take(timed.Count - 1).Select(t => t.Relative).ToList();
            var bar = Percentile95(baseline);
            var speed = currentPoint.DurationMs is { } ms ? speeds.Of(current.Id, ms) : 1.0;
            // The bar in this run's milliseconds: what the reader compares the duration with.
            p95 = (int)Math.Round(bar * speed);
            if (!currentIsDegraded && currentPoint.DurationMs is { } now && baseline.Count >= options.MinRuns && bar > 0)
            {
                var previous = timed[^1];
                var over = bar * options.SlowerBy;
                // Over the bar by the factor in both runs, and over it by enough milliseconds to notice: a
                // scenario measured in single digits clears the factor on runner jitter alone.
                if (now / speed > over && previous.Relative > over && now - over * speed >= options.SlowerMinMs)
                {
                    verdicts.Add(HistoryVerdictKind.Slower);
                    evidence.Add($"{now.ToString(CultureInfo.InvariantCulture)} ms now and {previous.Ms.ToString(CultureInfo.InvariantCulture)} ms last run, against a p95 of {p95.Value.ToString(CultureInfo.InvariantCulture)} ms over {baseline.Count.ToString(CultureInfo.InvariantCulture)} runs at this run's speed");
                }
            }
        }

        // ── Behaviour ───────────────────────────────────────
        // A fingerprint is comparable only with one the same templating rule made: across a change of
        // rule there is no verdict, and the reader is told, rather than every scenario changing once.
        // The rule is the pair: the version of the built-in rules and the hash of the consumer's
        // (HistoryShapeTemplates). An edit to either costs one quiet run.
        bool SameRule(HistoryPoint p) => (p.ShapeVersion ?? 1) == (currentPoint.ShapeVersion ?? 1) && string.Equals(p.ShapeRules, currentPoint.ShapeRules, StringComparison.Ordinal);
        // Filtered once, upstream of everything that reads it: the previous shaped point, the unstable
        // count, the alternating memory and the count stretch. Taking partial runs out of the memory alone
        // leaves the previous shaped point partial, and the first full run after a partial one then reads
        // behaviour-changed against a run the partial diagnostic says it is not compared against.
        var shapedByAnyRule = comparable.Where(p => p.ShapeSet is { Length: > 0 }).ToList();
        var shaped = shapedByAnyRule.Where(SameRule).ToList();
        var previousShaped = shaped.Count > 0 ? shaped[^1] : null;
        if (currentPoint.ShapeSet is { Length: > 0 } && shapedByAnyRule.Count > 0 && !SameRule(shapedByAnyRule[^1]))
            evidence.Add($"the calls in {shapedByAnyRule[^1].RunId} were fingerprinted by an earlier rule; behaviour is compared from the next run");
        if (currentPoint.ShapeSet is { Length: > 0 } && previousShaped is not null)
        {
            var changes = 0;
            for (var i = 1; i < shaped.Count; i++)
                if (!string.Equals(shaped[i].ShapeSet, shaped[i - 1].ShapeSet, StringComparison.Ordinal)) changes++;
            var unstable = shaped.Count >= options.MinRuns && changes > (shaped.Count - 1) / 2.0;
            var changed = !string.Equals(currentPoint.ShapeSet, previousShaped.ShapeSet, StringComparison.Ordinal);

            // Alternating: the scenario is back on a set it held within the last AlternatingRuns passing
            // runs, and a different set was held in between. Two states, both its own (a warm cache or a
            // cold one), and which one a run sees depends on ordering: the first sighting of each was a
            // change, every return is the same fact again. A set that stayed is not alternating (nothing
            // came between), and a set held only beyond the memory is a change again, so a regression to
            // how the scenario behaved long ago still reads as one. Failed runs are not the memory: what a
            // failure left is not a state.
            var memory = shaped.Where(p => p.Result == HistoryFormat.Passed).TakeLast(options.AlternatingRuns).ToList();
            var earliest = currentResult == HistoryFormat.Passed
                ? memory.FindIndex(p => string.Equals(p.ShapeSet, currentPoint.ShapeSet, StringComparison.Ordinal))
                : -1;
            var alternating = earliest >= 0 && memory.Skip(earliest + 1).Any(p => !string.Equals(p.ShapeSet, currentPoint.ShapeSet, StringComparison.Ordinal));

            if (alternating)
            {
                verdicts.Add(HistoryVerdictKind.Alternating);
                var distinct = memory.Select(p => p.ShapeSet).Distinct(StringComparer.Ordinal).Count();
                var held = memory.Count(p => string.Equals(p.ShapeSet, currentPoint.ShapeSet, StringComparison.Ordinal));
                var named = "";
                if (changed && CallSetOf(currentPoint) is { } nowSet && CallSetOf(previousShaped) is { } beforeSet)
                {
                    newCalls = nowSet.Except(beforeSet, StringComparer.Ordinal).ToArray();
                    goneCalls = beforeSet.Except(nowSet, StringComparer.Ordinal).ToArray();
                    named = NamedCalls("new", newCalls) + NamedCalls("gone", goneCalls);
                }
                else if (!changed && CallSetOf(currentPoint) is { } sameSet
                         && memory.LastOrDefault(p => !string.Equals(p.ShapeSet, currentPoint.ShapeSet, StringComparison.Ordinal)) is { } other
                         && CallSetOf(other) is { } otherSet)
                {
                    // The previous run held this set too, so the diff against it is empty and the evidence
                    // named no call. The state it alternates WITH is what the reader is after: the most
                    // recent run in the memory that held a different set, and what differs there.
                    named = $"; other set last held in {other.RunId}"
                            + NamedCalls("new there", otherSet.Except(sameSet, StringComparer.Ordinal).ToArray())
                            + NamedCalls("gone there", sameSet.Except(otherSet, StringComparer.Ordinal).ToArray());
                }
                evidence.Add($"alternating between {distinct.ToString(CultureInfo.InvariantCulture)} sets of calls over the last {memory.Count.ToString(CultureInfo.InvariantCulture)} runs: this set in {held.ToString(CultureInfo.InvariantCulture)} of them{named}");
            }
            else if (unstable)
            {
                verdicts.Add(HistoryVerdictKind.UnstableShape);
                evidence.Add($"the set of calls changed in {changes.ToString(CultureInfo.InvariantCulture)} of the last {(shaped.Count - 1).ToString(CultureInfo.InvariantCulture)} run pairs; behaviour verdicts are suppressed");
            }
            else if (changed)
            {
                var previousResult = previousShaped.Result;
                if (currentResult == previousResult)
                {
                    verdicts.Add(HistoryVerdictKind.BehaviourChanged);
                    var callsText = currentPoint.Calls is { } c && previousShaped.Calls is { } pc && c != pc
                        ? $"calls {pc.ToString(CultureInfo.InvariantCulture)} in {previousShaped.RunId} to {c.ToString(CultureInfo.InvariantCulture)} now"
                        : $"the same number of calls as {previousShaped.RunId}{(currentPoint.Calls is { } n ? " (" + n.ToString(CultureInfo.InvariantCulture) + ")" : "")}, a different set";
                    // Named when both runs recorded their call lists: what appeared and what disappeared, by
                    // its templated line. A count alone sends the reader to two reports to find the call.
                    if (CallSetOf(currentPoint) is { } now && CallSetOf(previousShaped) is { } before)
                    {
                        newCalls = now.Except(before, StringComparer.Ordinal).ToArray();
                        goneCalls = before.Except(now, StringComparer.Ordinal).ToArray();
                    }
                    evidence.Add($"same status, different calls: {callsText}{NamedCalls("new", newCalls)}{NamedCalls("gone", goneCalls)}{OnlyIdsDiffer(newCalls, goneCalls)}");
                }
            }
            else if (currentPoint.Calls is { } now && previousShaped.Calls is not null && currentResult == previousShaped.Result)
            {
                // The same calls, made a different number of times. "3, 3, 3, then 5" is an N+1 regression
                // the set cannot see; "7, 8, 7, then 9" is a retry against a throttled emulator or a
                // consumer's work landing in whichever scenario is running. The scenario's own record
                // tells them apart: the count is a verdict once it had been constant over the minimum
                // runs, and read out otherwise. And a count that moved once reverts more often than it
                // holds (measured on a consumer's ledger: ten of thirteen), so the new count must be held
                // for CountRuns runs before it is behaviour: the run that shows it reads it out, the run
                // that confirms it is the verdict, and the evidence names both.
                var counted = shaped.Where(p => p.Calls is not null).ToList();
                // The latest prior runs that already hold this run's count with this run's set: the change
                // being confirmed, at most CountRuns - 1 of them. What comes before them is the stretch the
                // count must have been constant over.
                var held = 0;
                while (held < options.CountRuns - 1 && held < counted.Count
                       && counted[^(held + 1)].Calls == now
                       && string.Equals(counted[^(held + 1)].ShapeSet, currentPoint.ShapeSet, StringComparison.Ordinal))
                    held++;
                var stretch = counted.Take(counted.Count - held).ToList();
                if (stretch.Count > 0 && stretch[^1].Calls is { } before && before != now)
                {
                    var heldRuns = string.Join(", ", counted.Skip(counted.Count - held).Select(p => p.RunId));
                    var nowText = now.ToString(CultureInfo.InvariantCulture);
                    var beforeText = before.ToString(CultureInfo.InvariantCulture);
                    var from = $"calls {beforeText} in {stretch[^1].RunId}";
                    var to = held == 0 ? $"to {nowText} now" : $"to {nowText} in {heldRuns} and now";
                    var constant = stretch.Count >= options.MinRuns && stretch.All(p => p.Calls == before);
                    // A count that changed with the set was reported then, as a different set of calls.
                    var countOnly = string.Equals(stretch[^1].ShapeSet, currentPoint.ShapeSet, StringComparison.Ordinal);
                    if (!countOnly)
                    {
                        // Nothing to add: the set change said it.
                    }
                    else if (constant && held == options.CountRuns - 1)
                    {
                        verdicts.Add(HistoryVerdictKind.BehaviourChanged);
                        evidence.Add(held == 0
                            ? $"the same calls made a different number of times: {from} {to}, constant over the last {stretch.Count.ToString(CultureInfo.InvariantCulture)} runs"
                            : $"the same calls made a different number of times: calls {beforeText} {to}, constant over the {stretch.Count.ToString(CultureInfo.InvariantCulture)} runs before");
                    }
                    else if (constant)
                    {
                        evidence.Add($"{from} {to}; a count verdict needs the new count held for {options.CountRuns.ToString(CultureInfo.InvariantCulture)} runs");
                    }
                    else if (stretch.Count < options.MinRuns)
                    {
                        evidence.Add($"{from} {to}; a count verdict needs {options.MinRuns.ToString(CultureInfo.InvariantCulture)} runs with the count constant");
                    }
                    else if (held == 0 && stretch.Count > 1 && stretch.Take(stretch.Count - 1).All(p => p.Calls == now))
                    {
                        evidence.Add($"{from} {to}; back to the count held over the {(stretch.Count - 1).ToString(CultureInfo.InvariantCulture)} runs before it");
                    }
                    else
                    {
                        evidence.Add($"{from} {to}; the count varies run to run for this scenario, so it is not read as behaviour");
                    }
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
            DurationP95IsRaw = p95IsRaw,
            PreviousShapeSet = previousShaped?.ShapeSet,
            ShapeSet = currentPoint.ShapeSet,
            PreviousCalls = previousShaped?.Calls,
            Calls = currentPoint.Calls,
            NewCalls = newCalls,
            GoneCalls = goneCalls,
            Quarantine = entry,
            FailuresInDegradedRuns = real.Count(p => p.Result == HistoryFormat.Failed && p.RunDegraded)
        };
    }

    /// <summary>
    /// The fold scenario: not a test, so no verdict of any kind is read for it. It is "stable" because the
    /// vocabulary has no word for "nothing to say about this position" other than that one, and it is never
    /// new: it appears whenever unattributed traffic survived an ingest, which is not news.
    /// </summary>
    private static ScenarioHistory NotATest(int position, HistoryRoster roster, HistoryRun current, bool currentIsPartial)
    {
        var point = new HistoryPoint(current.Id, current.At, current.Commit, HistoryFormat.NotATest, current.DurationAt(position),
            current.ShapeSetAt(position), current.ShapeOrderedAt(position), current.CallsAt(position), null, null, current.ShapeVersion, currentIsPartial);
        return new ScenarioHistory
        {
            StableId = roster.Ids[position],
            Slot = roster.Slots[position],
            Name = roster.Names[position],
            Feature = roster.Features[position],
            Current = HistoryFormat.NotATest,
            Primary = HistoryVerdictKind.Stable,
            Verdicts = new HashSet<HistoryVerdictKind> { HistoryVerdictKind.Stable },
            Evidence = "not a test: it collects the traffic no test could be given",
            RunsSeen = 0,
            RealVerdicts = 0,
            Failures = 0,
            FailRate = 0,
            Flips = 0,
            FlipRate = 0,
            RunsSinceLastFlip = 0,
            LastFailedRunsAgo = null,
            FailingSince = null,
            Series = HistoryFormat.NotATest.ToString(),
            Points = [point],
            DurationMs = point.DurationMs,
            DurationP95 = null,
            PreviousShapeSet = null,
            ShapeSet = point.ShapeSet,
            PreviousCalls = null,
            Calls = point.Calls,
            NewCalls = [],
            GoneCalls = [],
            Quarantine = null
        };
    }

    /// <summary>
    /// The two-part test a reading over its usual must clear before a surface prints it, the same one a
    /// slower verdict clears and for the same reason: the factor, and enough milliseconds to notice.
    /// </summary>
    private static HistoryPoint WithOverUsual(this HistoryPoint point, HistoryAnalysisOptions options) =>
        point is { TimesUsual: { } times, DurationMs: { } ms } && options.DegradedBy > 0 && times >= options.DegradedBy && ms - ms / times >= options.SlowerMinMs
            ? point with { OverUsual = true }
            : point;

    /// <summary>The call lines a position's indices name, or null when either side is unrecorded.</summary>
    private static IReadOnlyList<string>? Resolve(IReadOnlyList<int>? indices, HistoryShapes? shapes) =>
        indices is null || shapes is null ? null : indices.Select(shapes.At).Where(line => line is not null).Select(line => line!).ToArray();

    /// <summary>"; new: a, b and 2 more" for the evidence line; empty when there is nothing to name.</summary>
    private static string NamedCalls(string label, IReadOnlyList<string> calls)
    {
        const int shown = 3;
        if (calls.Count == 0)
            return "";
        var listed = string.Join(", ", calls.Take(shown));
        var more = calls.Count > shown ? $" and {(calls.Count - shown).ToString(CultureInfo.InvariantCulture)} more" : "";
        return $"; {label}: {listed}{more}";
    }

    /// <summary>
    /// The hint that a templating rule is missing: the new and the gone lines pair one to one once every
    /// run of letters, digits, hyphens and underscores that holds a digit is masked. It is a hint and
    /// never a verdict - the mask cannot tell a missed id from /v2/ becoming /v3/, which pairs one to one
    /// too and is exactly the change the verdict exists for. The status, the last word of a call line,
    /// is never masked: 200 becoming 404 is not an id.
    /// </summary>
    private static string OnlyIdsDiffer(IReadOnlyList<string> newCalls, IReadOnlyList<string> goneCalls)
    {
        if (newCalls.Count == 0 || newCalls.Count != goneCalls.Count)
            return "";

        static (string Head, string Status) Split(string line)
        {
            var at = line.LastIndexOf(' ');
            return at < 0 ? (line, "") : (line[..at], line[at..]);
        }
        static string Mask(string line)
        {
            var (head, status) = Split(line);
            return IdLikePattern().Replace(head, "\u0001") + status;
        }

        var maskedNew = newCalls.Select(Mask).OrderBy(l => l, StringComparer.Ordinal).ToArray();
        var maskedGone = goneCalls.Select(Mask).OrderBy(l => l, StringComparer.Ordinal).ToArray();
        if (!maskedNew.SequenceEqual(maskedGone, StringComparer.Ordinal))
            return "";

        // One example, from the first gone line and the new line it pairs with: the first token that differs.
        var gone = goneCalls.OrderBy(Mask, StringComparer.Ordinal).First();
        var now = newCalls.OrderBy(Mask, StringComparer.Ordinal).First();
        var before = IdLikePattern().Matches(Split(gone).Head).Select(m => m.Value).ToArray();
        var after = IdLikePattern().Matches(Split(now).Head).Select(m => m.Value).ToArray();
        var example = "";
        for (var i = 0; i < Math.Min(before.Length, after.Length); i++)
            if (!string.Equals(before[i], after[i], StringComparison.Ordinal))
            {
                example = $" ({Short8(before[i])} → {Short8(after[i])})";
                break;
            }
        return $"; the calls differ only in what looks like an id{example}: a HistoryShapeTemplates rule would make them compare equal";
    }

    private static string Short8(string token) => token.Length > 8 ? token[..8] + "…" : token;

    [System.Text.RegularExpressions.GeneratedRegex(@"[0-9A-Za-z_\-]*\d[0-9A-Za-z_\-]*", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex IdLikePattern();

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

    internal static double Percentile95(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var rank = (int)Math.Ceiling(0.95 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    /// <summary>
    /// The pace of each full run in the window (#83): the median, over the scenarios that passed in it, of
    /// the reading over the scenario's usual - its median passing duration over the OTHER full runs. Each
    /// scenario is measured against itself, so a roster that gained fifty slow scenarios has the pace it
    /// had; passing scenarios only, so timeouts do not inflate it. A partial run is in no usual and has no
    /// pace: its conditions differ in either direction.
    ///
    /// <para>Two readings wait for different things. A ROW is read against its usual from two other
    /// readings, which is when a duration bar first appears beside it: it is a reading, not a verdict. A
    /// RUN is paced only from scenarios with the minimum runs of other readings each, because the label
    /// gates a verdict: measured over 394 healthy CI runs, a run paced against two to four others read 2.0
    /// or more one to three times in a thousand, and against five, never.</para>
    ///
    /// <para>A degraded run stays in the usual. A median shrugs off a minority of bad runs, and if degraded
    /// runs left it a genuine, sustained slowdown of the whole suite would read as degraded for ever
    /// instead of for half a window.</para>
    ///
    /// <para>Cost: one dictionary lookup per scenario of each DISTINCT roster (a stream normally has one
    /// or two), then arrays. Every run's scenarios are read twice, once to gather and once to pace.</para>
    /// </summary>
    internal sealed class RunPaces
    {
        /// <summary>The fewest qualifying scenarios a run needs before it has a pace.</summary>
        internal const int MinScenarios = 5;

        /// <summary>The fewest other full-run passing readings a row is read against.</summary>
        internal const int MinReadingsForARow = 2;

        /// <summary>
        /// A scenario counts towards a run's pace when its usual is at least this many milliseconds.
        /// Measured under real contention: scenarios that usually take under 10 ms barely register the
        /// load (and 3 ms read as 9 is a timer tick), and they were half the votes. With them, a run that
        /// handed out twenty false slower verdicts read 2.25 against a worst healthy run of 1.63; without
        /// them, 6.85 against 1.56. A larger floor throws the signal away instead (100 ms: 4.98 for 7.38).
        /// </summary>
        internal const int UsualFloorMs = 10;

        private readonly Dictionary<(string Id, int Slot), int> _index = new();
        private readonly List<int[]> _usual = [];
        private readonly double?[] _pace;
        private readonly bool[] _full;
        private readonly HistoryAliases? _aliases;
        private readonly double _degradedBy;

        /// <summary>Paces every run of the window: the prior runs at their indices, the current run last.</summary>
        public RunPaces(IReadOnlyList<HistoryRun> prior, IReadOnlyList<HistoryRoster?> priorRosters, HistoryRun current, HistoryRoster roster,
            bool currentIsPartial, HistoryAliases? aliases, HistoryAnalysisOptions options)
        {
            _aliases = aliases;
            // Zero or less switches the label off: nothing is degraded, and slower reads as it did before.
            _degradedBy = options.DegradedBy > 0 ? options.DegradedBy : double.PositiveInfinity;
            var minRuns = Math.Max(1, options.MinRuns);
            var count = prior.Count + 1;
            _pace = new double?[count];
            _full = new bool[count];

            HistoryRun RunAt(int r) => r < prior.Count ? prior[r] : current;
            HistoryRoster? RosterAt(int r) => r < prior.Count ? priorRosters[r] : roster;

            // Each distinct roster's positions as indices into one list of scenarios, resolved once.
            var positions = new Dictionary<HistoryRoster, int[]>(ReferenceEqualityComparer.Instance);
            var readings = new List<List<int>>();
            int[] PositionsOf(HistoryRoster of)
            {
                if (positions.TryGetValue(of, out var map)) return map;
                map = new int[of.Count];
                for (var i = 0; i < of.Count; i++)
                {
                    var key = Key(of.Ids[i], of.Slots[i]);
                    if (!_index.TryGetValue(key, out var at))
                    {
                        _index[key] = at = readings.Count;
                        readings.Add([]);
                    }
                    map[i] = at;
                }
                return positions[of] = map;
            }

            for (var r = 0; r < count; r++)
            {
                var run = RunAt(r);
                var partial = r == prior.Count ? currentIsPartial : run.Partial == true;
                if (partial || RosterAt(r) is not { } of || run.Durations is null) continue;
                _full[r] = true;
                var map = PositionsOf(of);
                for (var i = 0; i < of.Count; i++)
                    if (run.ResultAt(i) == HistoryFormat.Passed && run.DurationAt(i) is { } ms)
                        readings[map[i]].Add(ms);
            }

            foreach (var list in readings)
            {
                var sorted = list.ToArray();
                Array.Sort(sorted);
                _usual.Add(sorted);
            }

            var ratios = new List<double>();
            for (var r = 0; r < count; r++)
            {
                if (!_full[r] || RosterAt(r) is not { } of) continue;
                var run = RunAt(r);
                var map = positions[of];
                ratios.Clear();
                for (var i = 0; i < of.Count; i++)
                    if (run.ResultAt(i) == HistoryFormat.Passed && run.DurationAt(i) is { } ms
                        && Usual(_usual[map[i]], ms, minRuns) is { } usual && usual >= UsualFloorMs)
                        ratios.Add(ms / usual);
                if (ratios.Count < MinScenarios) continue;
                ratios.Sort();
                _pace[r] = ratios.Count % 2 == 1 ? ratios[ratios.Count / 2] : (ratios[ratios.Count / 2 - 1] + ratios[ratios.Count / 2]) / 2.0;
            }
        }

        /// <summary>The pace of the run at <paramref name="index"/> (the current run is last), or null when it is partial or too little is known.</summary>
        public double? Of(int index) => _pace[index];

        /// <summary>Whether the run's passing scenarios took the degraded factor over their usual.</summary>
        public bool Degraded(int index) => _pace[index] is { } pace && pace >= _degradedBy;

        /// <summary>The scenario's passing full-run durations, sorted; null when it has none.</summary>
        public int[]? UsualOf(string id, int slot) => _index.TryGetValue(Key(id, slot), out var at) ? _usual[at] : null;

        /// <summary>A reading over the scenario's usual; null in a partial run, without a duration, or with fewer than two other readings.</summary>
        public double? TimesUsual(int[]? usualReadings, int index, int? durationMs, char result)
        {
            if (usualReadings is null || durationMs is not { } ms || !_full[index]) return null;
            // A passing reading is one of the readings, and is left out of its own usual.
            return Usual(usualReadings, result == HistoryFormat.Passed ? ms : null, MinReadingsForARow) is { } usual ? ms / usual : null;
        }

        private (string, int) Key(string id, int slot) => (_aliases?.Current(id) ?? id, slot);

        /// <summary>The median of the sorted readings without one occurrence of <paramref name="ownMs"/>; null under <paramref name="minOthers"/> others.</summary>
        private static double? Usual(int[] sorted, int? ownMs, int minOthers)
        {
            var self = ownMs is { } own ? Array.BinarySearch(sorted, own) : -1;
            if (self < 0) self = sorted.Length;
            var others = self < sorted.Length ? sorted.Length - 1 : sorted.Length;
            if (others < minOthers) return null;
            int At(int index) => sorted[index < self ? index : index + 1];
            var median = others % 2 == 1 ? At(others / 2) : (At(others / 2 - 1) + At(others / 2)) / 2.0;
            return median > 0 ? median : null;
        }
    }

    /// <summary>
    /// The speed of each run in the window: the median duration of a run's scenarios, taken without the
    /// scenario being read so that a scenario's own regression cannot move the bar it is read against —
    /// on a roster of two, the run's median IS the other scenario. One sort per run; each reading is a
    /// binary search and an index.
    /// </summary>
    private sealed class RunSpeeds
    {
        private readonly Dictionary<string, int[]> _sorted = new(StringComparer.Ordinal);

        public RunSpeeds(IEnumerable<HistoryRun> runs)
        {
            foreach (var run in runs)
            {
                if (run.Durations is not { } durations) continue;
                var sorted = durations.Where(d => d.HasValue).Select(d => d!.Value).ToArray();
                Array.Sort(sorted);
                _sorted[run.Id] = sorted;
            }
        }

        /// <summary>The run's speed in milliseconds without one occurrence of <paramref name="ownMs"/>; 1 when nothing else was timed.</summary>
        public double Of(string runId, int ownMs)
        {
            if (!_sorted.TryGetValue(runId, out var sorted) || sorted.Length < 2) return 1.0;
            var self = Array.BinarySearch(sorted, ownMs);
            if (self < 0) self = sorted.Length; // not this run's own reading: read against the whole run
            var others = self < sorted.Length ? sorted.Length - 1 : sorted.Length;
            int At(int index) => sorted[index < self ? index : index + 1];
            var median = others % 2 == 1
                ? At(others / 2)
                : (At(others / 2 - 1) + At(others / 2)) / 2.0;
            return median > 0 ? median : 1.0;
        }
    }
}
