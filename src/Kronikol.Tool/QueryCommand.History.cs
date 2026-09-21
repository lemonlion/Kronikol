using System.Globalization;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// <c>kronikol query history</c>: what the last runs say about this one, from the cross-run ledger
/// (plans/CROSS_RUN_HISTORY_PLAN.md §6.4). The verdicts are the library's — the same analysis the run
/// itself performed when it wrote its digest — read again here against the ledger as it stands now, so
/// a report downloaded from CI answers "has this been flaky?" without the run that wrote it.
/// </summary>
internal static partial class QueryCommand
{
    private static int History(ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error, Func<string, string?> getEnv)
    {
        var location = ReportHistory.Locate(options.HistoryPath, index.Directory, getEnv, _workingDirectory);
        if (location.Source == HistoryLocationSource.Disabled)
        {
            error.WriteLine($"{HistoryFormat.EnvironmentVariable}={HistoryFormat.EnvironmentOff}: history is switched off in this environment. Pass --history <file> to read a ledger anyway.");
            return 2;
        }

        if (location.Path is not { } ledgerPath)
        {
            error.WriteLine($"No history ledger for {Path.GetFileName(index.Path)}: nothing named by --history or ${HistoryFormat.EnvironmentVariable}, and no .kronikol or .git directory above {index.Directory}.");
            error.WriteLine("Pass --history <file>, set KRONIKOL_HISTORY, or run kronikol history init in the repository the tests live in.");
            return 2;
        }

        // A run named by --run can be anywhere in the ledger: every run is read, and the analysis applies
        // the window behind the run chosen.
        var read = HistoryLedgerReader.Read(ledgerPath, options.Run is null ? options.HistoryWindowOrDefault : 0);
        if (read.Ledger is not { } ledger)
        {
            error.WriteLine(read.Message ?? $"the ledger at {ledgerPath} could not be read");
            return 1;
        }

        if (!ReportHistory.TryBuild(index, options.SuiteOverride, error, out var roster, out var run, out var fromFragment, out var shapes))
            return 2;

        if (!fromFragment)
            ReportHistory.AdoptOwnLine(ref ledger, ledgerPath, options.Run is null ? options.HistoryWindowOrDefault : 0, roster, ref run, ref shapes, ref fromFragment);

        // Under --count the text answer is one bare number, so the caveats go to stderr - the same rule
        // WriteProvenance applies to the provenance notes.
        Action<string> note = options.Count && !options.Json ? error.WriteLine : writer.Note;

        // --run with a report: the report names the ledger and the suite, the flag names the run. Its own
        // run is the ordinary reading; any other is read from the ledger, which is all that is left of it.
        if (options.Run is { } wanted && !options.RunIsResolved && !ReportHistory.Names(run, wanted))
        {
            if (ReportHistory.ResolveRun(ledger, run.Suite, options.Branch, wanted, error) is not { } other)
                return 2;
            options.RunResolvedTo(other.Id);
            if (!string.Equals(other.Id, run.Id, StringComparison.Ordinal))
            {
                note($"! {Path.GetFileName(index.Path)} describes {run.Id} — reading {other.Id} from the ledger instead");
                return HistoryOfLedgerRun(ledger, other, ledgerPath, location.Source, options, writer, error, note, getEnv);
            }
        }

        var quarantine = ReportHistory.LoadQuarantine(ledgerPath, writer);
        var aliases = ReportHistory.LoadAliases(ledgerPath, writer);
        var analysis = ReportHistory.AnalysisOptions(options, getEnv);
        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, run, analysis, quarantine, aliases, shapes: shapes);

        if (read.Outcome == HistoryReadOutcome.Missing)
            note($"! no ledger at {ledgerPath} yet — every scenario reads as its first run; the next recorded run starts the history");
        if (ledger.Stats.DamagedLines > 0)
            note($"! {ledger.Stats.DamagedLines} line(s) of the ledger could not be parsed and were skipped — kronikol history verify says which");
        if (!fromFragment)
            note($"! no {HistoryFormat.FragmentFileName} beside the report — behaviour verdicts are off; results, attempts and durations still compare");

        var subjects = index.Scenarios.Select(HistorySubject.Of).ToArray();
        var reading = new HistoryReading(verdicts, subjects, ledgerPath, location.Source, fromFragment, HistoryReading.FromReport, analysis, run, shapes);

        HistorySubject? one = null;
        if (options.Positional.Count > 0)
        {
            if (!TryScenario(index, options, error, out var scenario, out var step))
                return 2;
            if (step is not null)
                note($"! history is per scenario — scoped to {scenario.Address}; `steps {scenario.Address}/{step}` answers for the step");
            one = subjects[scenario.Ordinal];
        }
        else if (options.Sid is { } sid)
        {
            // The flag the report-less form needs, accepted here too: the scenario the sid: positional names.
            one = subjects.FirstOrDefault(s => string.Equals(s.StableId, sid, StringComparison.OrdinalIgnoreCase));
            if (one is null)
            {
                error.WriteLine($"sid:{sid} is not in {Path.GetFileName(index.Path)} — `history {index.Path}` lists what is.");
                return 2;
            }
        }

        return WriteHistory(reading, one, options, writer, error, note);
    }

    /// <summary>
    /// <c>history</c> with no report (#81): the ledger holds the run a re-run overwrote, and everything the
    /// analysis takes - the run line, its roster, its calls - is in it. What it cannot give is what only a
    /// report has: steps, calls and payloads.
    /// </summary>
    private static int HistoryFromLedger(QueryOptions options, QueryWriter writer, TextWriter error, Func<string, string?> getEnv)
    {
        var start = _workingDirectory ?? Directory.GetCurrentDirectory();
        var location = ReportHistory.Locate(options.HistoryPath, start, getEnv, _workingDirectory);
        if (location.Source == HistoryLocationSource.Disabled)
        {
            error.WriteLine($"{HistoryFormat.EnvironmentVariable}={HistoryFormat.EnvironmentOff}: history is switched off in this environment. Pass --history <file> to read a ledger anyway.");
            return 2;
        }

        if (location.Path is not { } ledgerPath)
        {
            error.WriteLine($"No report given, and no history ledger: nothing named by --history or ${HistoryFormat.EnvironmentVariable}, and no .kronikol or .git directory above {start}.");
            error.WriteLine("Pass a TestRunReport.json (or a directory holding one), or --history <file> to read a ledger without a report.");
            return 2;
        }

        var read = HistoryLedgerReader.Read(ledgerPath, options.Run is null ? options.HistoryWindowOrDefault : 0);
        if (read.Ledger is not { } ledger)
        {
            error.WriteLine(read.Message ?? $"the ledger at {ledgerPath} could not be read");
            return 1;
        }

        if (read.Outcome == HistoryReadOutcome.Missing || ledger.IsEmpty)
        {
            error.WriteLine($"No report given, and no run is recorded in {ledgerPath}.");
            error.WriteLine("Pass a TestRunReport.json (or a directory holding one); a ledger answers on its own once a run has been recorded.");
            return 2;
        }

        string? suite;
        if (options.SuiteOverride is { } named)
        {
            if (!ledger.Suites.Contains(named, StringComparer.Ordinal))
            {
                error.WriteLine($"No suite {named} in {ledgerPath}. It holds: {string.Join(", ", ledger.Suites.Select(s => s ?? "(unnamed)"))}.");
                return 2;
            }
            suite = named;
        }
        else if (ledger.Suites.Count == 1)
        {
            suite = ledger.Suites[0];
        }
        else
        {
            error.WriteLine($"{ledgerPath} holds {ledger.Suites.Count} suites — name one with --suite: {string.Join(", ", ledger.Suites.Select(s => s ?? "(unnamed)"))}.");
            return 2;
        }

        HistoryRun? run;
        if (options.Run is { } wanted)
        {
            run = ReportHistory.ResolveRun(ledger, suite, options.Branch, wanted, error);
            if (run is null)
                return 2;
            // The pointer to page two names the run, never the alias: `last-failed` is a different run
            // the moment another failure is recorded.
            options.RunResolvedTo(run.Id);
        }
        else
        {
            run = ledger.LatestRun(suite);
            if (run is null)
            {
                error.WriteLine($"No run of {suite ?? "(unnamed)"} is recorded in {ledgerPath}.");
                return 2;
            }
        }

        Action<string> note = options.Count && !options.Json ? error.WriteLine : writer.Note;
        return HistoryOfLedgerRun(ledger, run, ledgerPath, location.Source, options, writer, error, note, getEnv);
    }

    /// <summary>One run read from its own ledger line: the roster and the calls it indexes are in the ledger beside it.</summary>
    private static int HistoryOfLedgerRun(HistoryLedger ledger, HistoryRun run, string ledgerPath, HistoryLocationSource source,
        QueryOptions options, QueryWriter writer, TextWriter error, Action<string> note, Func<string, string?> getEnv)
    {
        if (ledger.Roster(run.RosterHash) is not { } roster)
        {
            error.WriteLine($"The ledger has run {run.Id} but not its roster ({run.RosterHash}) — kronikol history verify says what else is missing.");
            return 1;
        }

        var shapes = run.ShapesHash is { } hash ? ledger.Shapes(hash) : null;
        var quarantine = ReportHistory.LoadQuarantine(ledgerPath, writer);
        var aliases = ReportHistory.LoadAliases(ledgerPath, writer);
        var analysis = ReportHistory.AnalysisOptions(options, getEnv);
        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, run, analysis, quarantine, aliases, shapes: shapes);

        if (ledger.Stats.DamagedLines > 0)
            note($"! {ledger.Stats.DamagedLines} line(s) of the ledger could not be parsed and were skipped — kronikol history verify says which");

        var subjects = Enumerable.Range(0, roster.Count).Select(i => HistorySubject.Of(roster, run, i)).ToArray();
        var reading = new HistoryReading(verdicts, subjects, ledgerPath, source, run.ShapeSet is not null, HistoryReading.FromLedger, analysis, run, shapes);

        HistorySubject? one = null;
        if (options.Sid is { } sid)
        {
            one = subjects.FirstOrDefault(s => string.Equals(s.StableId, sid, StringComparison.OrdinalIgnoreCase));
            if (one is null)
            {
                error.WriteLine($"sid:{sid} is not in run {run.Id} — `history --run {run.Id}` lists what is, and the run that has it is the one to name.");
                return 2;
            }
        }
        else if (options.Positional.Count > 0)
        {
            // s3 is an ordinal in a REPORT; a run read from the ledger has none anybody else can use.
            error.WriteLine($"{options.Positional[0]} addresses a scenario of a report, and run {run.Id} is being read from the ledger — name the scenario with --sid <id> (`history --run {run.Id}` lists them).");
            return 2;
        }

        return WriteHistory(reading, one, options, writer, error, note);
    }

    /// <summary>Both readings, rendered once: one scenario in full, or the run.</summary>
    private static int WriteHistory(HistoryReading reading, HistorySubject? one, QueryOptions options, QueryWriter writer, TextWriter error, Action<string> note)
    {
        var verdicts = reading.Verdicts;
        var ledgerOnly = reading.Source == HistoryReading.FromLedger;
        var ledgerOnlyLine = $"ledger only — no report read; steps, calls and payloads need one (kronikol query failures <reports-dir> --run {verdicts.RunId}, when the run is retained)";

        // ── One scenario ───────────────────────────────────
        if (one is not null)
        {
            var entry = verdicts.At(one.Ordinal, one.StableId);
            if (entry is null)
            {
                error.WriteLine($"{one.Address} has no history entry — the report's scenario list and the ledger disagree; re-run kronikol history record for this run.");
                return 1;
            }

            if (options.Count)
            {
                writer.Count(entry.Verdicts.Count);
                return 0;
            }

            WriteScenario(writer, verdicts, one, entry);
            var callLines = options.Calls ? ReportHistory.CallsOf(verdicts, entry, reading.Run, reading.Shapes) : null;
            if (options.Calls)
                WriteCalls(writer, callLines);
            if (verdicts.Compare is { } compared && compared.At(one.Ordinal, one.StableId) is { } other)
            {
                writer.Line();
                writer.Line($"on {compared.Stream}: {HistoryVerdictNames.Name(other.Primary)} — {QueryWriter.Flat(other.Evidence)} · {other.Series}");
            }
            if (ledgerOnly)
                writer.Line(ledgerOnlyLine);
            writer.Data("history", ReportHistory.RunLevel(reading, []));
            writer.Item(ReportHistory.Row(one, entry, detailed: true, calls: options.Calls, callLines: callLines));
            writer.Footer($"{one.Address} · {verdicts.RunsRecorded} earlier run(s) on {verdicts.Stream} · next: history{(ledgerOnly ? $" --run {verdicts.RunId}" : " · failures")}");
            return 0;
        }

        // ── The run ────────────────────────────────────────
        var filtered = ReportHistory.Filter(reading.Subjects, verdicts, options);
        var misses = filtered.Count == 0 ? ReportHistory.NearMisses(reading, options) : [];
        var outside = verdicts.FailingOutside.Count > 0 && (!ReportHistory.AnyFilter(options) || options.Failing || options.Regressed)
            ? verdicts.FailingOutside
            : [];
        if (options.Count)
        {
            writer.Count(filtered.Count);
            // One bare number is the contract; what the number does not say goes beside it, not into it.
            foreach (var hint in ReportHistory.HintLines(reading, misses, outside))
                note(hint);
            return 0;
        }

        var scenarioCount = verdicts.Scenarios.Count;
        writer.Line($"history: {HistorySummary.Line(verdicts)}");
        writer.Line($"ledger: {reading.LedgerPath} · stream {verdicts.Stream} · run {verdicts.RunId}");
        // Its own line: `· partial` at the end of the line above had the weight of the stream name, and
        // what it means - every filter below answers for this roster only - is the thing #84 missed.
        if (verdicts.Partial)
        {
            var these = scenarioCount == 1 ? "this one" : $"these {scenarioCount}";
            writer.Line(verdicts.PreviousFullCount is { } full
                ? $"this run is partial: {scenarioCount} scenario{(scenarioCount == 1 ? "" : "s")}, the last full run had {full} — a filter answers for {these} only"
                : $"this run is partial: {scenarioCount} scenario{(scenarioCount == 1 ? "" : "s")} — a filter answers for {these} only");
        }
        if (ledgerOnly)
            writer.Line(ledgerOnlyLine);
        // The run's label speaks once, here: in a contended run a third of the rows would carry a note.
        if (HistorySummary.Degraded(verdicts.Runs[^1]) is { } degraded)
            writer.Line($"{degraded}, so no scenario is read slower in this run");
        if (verdicts.ColdStart)
            writer.Line(verdicts.ColdStartMessage!);
        if (verdicts.Compare is { } compare)
            writer.Line($"on {compare.Stream}: {HistorySummary.Line(compare)}");
        writer.Line();

        var absentNoted = false;
        var forOne = ledgerOnly ? $"--run {verdicts.RunId} --sid <id>" : "s3";
        if (filtered.Count == 0)
        {
            // Said outright rather than through the pager, whose empty page reads "nothing at --offset 0".
            writer.Note($"{ReportHistory.EmptyNote(reading, options)} · {scenarioCount} scenarios read against {verdicts.RunsRecorded} earlier run(s)");
        }
        else
        writer.Page(filtered, options.Offset, options.PageSize(50, writer, "scenarios"), "scenarios", pair =>
        {
            var (scenario, entry) = pair;
            writer.Line($"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}");
            // A list cuts, and says so: the footer names the view that prints this row's evidence whole.
            var cutBefore = writer.HasCut;
            writer.Line($"  {HistoryVerdictNames.Name(entry.Primary),-18} {entry.Series,-10} {writer.Cut(entry.Evidence, 180)}");
            if (!cutBefore && writer.HasCut)
                writer.CutNotice($"… marks cut text — history {reading.Next(scenario)} prints it whole");
            if (entry.Verdicts.Count > 1)
                writer.Line($"  also: {string.Join(", ", entry.Verdicts.Where(v => v != entry.Primary).OrderBy(HistoryAnalyzer.Precedence).Select(HistoryVerdictNames.Name))}");
        }, options.RerunArgs(), pair => ReportHistory.Row(pair.Scenario, pair.Entry, detailed: false),
            whenComplete: $"{filtered.Count} scenario(s) with a verdict{ReportHistory.FilterLabel(options)} · history {forOne} for one in full");

        foreach (var hint in ReportHistory.HintLines(reading, misses, outside))
            writer.Note(hint);

        if (!ReportHistory.AnyFilter(options))
        {
            if (verdicts.Absent.Count > 0)
            {
                writer.Line();
                writer.Line($"absent — in {verdicts.Absent[0].LastRunId} and not in this run:");
                foreach (var absent in verdicts.Absent.Take(20))
                    writer.Line($"  sid:{absent.StableId}  {absent.Feature} › {absent.Name}");
                if (verdicts.Absent.Count > 20)
                    writer.Line($"  … and {verdicts.Absent.Count - 20} more");
                absentNoted = true;
            }

            if (verdicts.NewDependencies.Count > 0)
            {
                if (!absentNoted) writer.Line();
                writer.Line($"new dependencies this run: {string.Join(", ", verdicts.NewDependencies)}");
            }
        }

        writer.Data("history", ReportHistory.RunLevel(reading, misses));
        return 0;
    }

    /// <summary>
    /// What the templater made of the scenario's calls in this run. A wrong <c>{id}</c> is silent - two
    /// routes collapse into one line and a real change disappears - so the lines are there to be read.
    /// </summary>
    private static void WriteCalls(QueryWriter writer, IReadOnlyList<string>? calls)
    {
        writer.Line();
        if (calls is null)
        {
            writer.Line($"no call list for this run: it needs the {HistoryFormat.FragmentFileName} the run wrote beside the report (3.17.0 or later, HistoryShapes on)");
            return;
        }

        writer.Line($"distinct calls, as templated ({calls.Count}):");
        foreach (var call in calls)
            writer.Line($"  {call}");
    }

    private static void WriteScenario(QueryWriter writer, HistoryVerdicts verdicts, HistorySubject scenario, ScenarioHistory entry)
    {
        writer.Line(scenario.Address.StartsWith("sid:", StringComparison.Ordinal)
            ? $"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}"
            : $"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}  sid:{scenario.StableId}");
        writer.Line($"verdict: {entry.VerdictNames}");
        // Nothing in this view is cut (#82): it is at most fifteen rows of strings the ledger has already
        // capped, and the characters a cut removes are the ones that say what failed.
        writer.Line($"evidence: {QueryWriter.Flat(entry.Evidence)}");
        // The episode count sits beside the flips because `flips 2 · flip rate 0.50` alone is what made a
        // one-off read as flaky (#84): two flips is one break and one fix.
        writer.Line($"runs seen: {entry.RunsSeen} · verdicts: {entry.RealVerdicts} · failed {entry.Failures}{(entry.FailuresInDegradedRuns > 0 ? $" ({entry.FailuresInDegradedRuns} in a degraded run)" : "")} · flips {entry.Flips} · failing episodes {entry.FailingEpisodes} · flip rate {entry.FlipRate.ToString("0.00", CultureInfo.InvariantCulture)} · fail rate {entry.FailRate.ToString("0.00", CultureInfo.InvariantCulture)}"
                    + (entry.LastFailedRunsAgo is { } ago ? $" · last failed {ago} run(s) ago" : ""));
        if (entry.FailingSince is { } since)
            writer.Line($"failing since: {since.RunId}{(since.Commit is { } commit ? $" ({(commit.Length > 7 ? commit[..7] : commit)})" : "")} · {since.Runs} run(s)");
        if (entry.DurationMs is { } duration)
            // The bar is scaled to this run's speed, so it can stand over every raw reading under it: the
            // label says so. For a partial run nothing is scaled, and the label says that instead.
            writer.Line($"duration: {duration} ms" + (entry.DurationP95 is not { } p95 ? ""
                : entry.DurationP95IsRaw ? $" · p95 of earlier full runs {p95} ms (this run is partial, so nothing is scaled to its speed)"
                : $" · p95 of earlier full runs, at this run's speed {p95} ms"));
        if (entry.Calls is { } calls)
            writer.Line($"calls: {calls}" + (entry.PreviousCalls is { } previous && previous != calls ? $" (was {previous})" : "")
                        + (entry.PreviousShapeSet is { } shape && entry.ShapeSet is { } now && !string.Equals(shape, now, StringComparison.Ordinal) ? " · set of calls changed since the previous run" : ""));
        // The evidence names three of each and says "and N more"; the rest were only in --json.
        foreach (var call in entry.NewCalls)
            writer.Line($"  new:  {call}");
        foreach (var call in entry.GoneCalls)
            writer.Line($"  gone: {call}");
        if (entry.Quarantine is { } quarantine)
            writer.Line($"quarantined: {QueryWriter.Flat(quarantine.Reason)}"
                        + (quarantine.AddedBy is { } by ? $" · by {by}" : "")
                        + $" · since {quarantine.AddedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                        + (quarantine.Until is { } until ? $" · until {until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : ""));
        writer.Line($"stream: {verdicts.Stream} · {verdicts.RunsRecorded} earlier run(s) in the window");
        writer.Line("last runs, oldest first (this run last):");
        var runs = verdicts.Runs.ToDictionary(r => r.RunId, StringComparer.Ordinal);
        var firstLineOnly = false;
        foreach (var point in entry.Points.TakeLast(15))
        {
            writer.Line($"  {point.Result}  {point.RunId,-24} {point.At.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}"
                        + (point.Commit is { } c ? $"  {(c.Length > 7 ? c[..7] : c)}" : "")
                        // The reading over the scenario's usual is a fact, not a cause: a failing test is usually
                        // slow because it failed. Only the run's own label says anything about the machine.
                        + (point.DurationMs is { } ms ? $"  {ms} ms" + (point.OverUsual ? $" ({HistorySummary.Times(point.TimesUsual!.Value)} usual)" : "") : "")
                        + (point.Partial ? "  (partial)" : "")
                        + (point.RunDegraded && runs.TryGetValue(point.RunId, out var degradedRun) && HistorySummary.Degraded(degradedRun) is { } label ? $"  [run {label}]" : "")
                        + (point.Attempt is { } attempt && attempt > 1 ? $"  attempt {attempt}" : ""));
            // Under the row and whole. At the end of the row it was cut at 80 characters, which for an
            // assertion is "expected X" without "but found Y".
            if (point.Error is not { } e)
                continue;
            // An error on a pass is an earlier attempt's (a retry overlaid on its run): said so, because a
            // message under a `P` row reads as a pass that failed.
            writer.Line($"       {(point.Result == HistoryFormat.Passed ? "an earlier attempt failed: " : "")}{QueryWriter.Flat(e)}");
            firstLineOnly |= e.Length >= HistoryFormat.ErrorKeyLimit - 1 && e.EndsWith('…');
        }

        // The ledger keeps the first line of a message, up to 199 characters: text that ends in ITS
        // ellipsis is not cut by this view, and the rest is not in the ledger at all.
        if (firstLineOnly)
            writer.Line("       … first line only — the whole message is in that run's Failures.md (kronikol query failures <reports-dir> --run <id> opens it while the run is retained)");
    }
}

/// <summary>The report-side half of history: the run a report describes, and the ledger it belongs to.</summary>
internal static class ReportHistory
{
    /// <summary>
    /// The ledger for a report, the tool's way: <c>--history</c>, then <c>$KRONIKOL_HISTORY</c>, then the
    /// nearest marker above the report's own directory, then above the working directory.
    /// </summary>
    public static HistoryLocation Locate(string? explicitPath, string reportDirectory, Func<string, string?> getEnv, string? workingDirectory = null) =>
        HistoryPathResolver.Resolve(explicitPath, workingDirectory ?? Directory.GetCurrentDirectory(), getEnv, reportDirectory);

    /// <summary>
    /// The verdicts for a report when a ledger resolves without being asked for, and null — silently —
    /// when nothing does. What <c>failures</c> uses for its <c>history:</c> line: a verb that never
    /// mentioned history must not start failing over it.
    /// </summary>
    public static HistoryVerdicts? TryVerdictsSilently(ReportIndex index, Func<string, string?> getEnv, string? workingDirectory = null)
    {
        try
        {
            var location = Locate(null, index.Directory, getEnv, workingDirectory);
            if (location.Path is not { } path || !File.Exists(path))
                return null;
            var read = HistoryLedgerReader.Read(path, 50, new HistoryLockBudget(Attempts: 20, MaxDelayMilliseconds: 10));
            if (read.Outcome != HistoryReadOutcome.Read || read.Ledger is not { } ledger || ledger.IsEmpty)
                return null;
            if (!TryBuild(index, null, TextWriter.Null, out var roster, out var run, out var fromFragment, out var shapes))
                return null;
            if (!fromFragment)
                AdoptOwnLine(ref ledger, path, 50, roster, ref run, ref shapes, ref fromFragment);
            HistoryQuarantineList? quarantine = null;
            HistoryAliases? aliases = null;
            try { quarantine = HistoryQuarantineList.Load(HistoryQuarantineList.PathBeside(path)); } catch (Exception e) when (IsBenign(e)) { }
            try { aliases = HistoryAliases.Load(HistoryAliases.PathBeside(path)); } catch (Exception e) when (IsBenign(e)) { }
            return HistoryAnalyzer.Analyse(ledger, roster, run, new HistoryAnalysisOptions(), quarantine, aliases);
        }
        catch (Exception exception) when (IsBenign(exception))
        {
            return null;
        }
    }

    private static bool IsBenign(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException or ArgumentException;

    public static HistoryQuarantineList? LoadQuarantine(string ledgerPath, QueryWriter writer)
    {
        try
        {
            return HistoryQuarantineList.Load(HistoryQuarantineList.PathBeside(ledgerPath));
        }
        catch (Exception exception) when (IsBenign(exception))
        {
            writer.Note($"! the quarantine list beside the ledger could not be read and was ignored: {QueryWriter.OneLine(exception.Message, 120)}");
            return null;
        }
    }

    public static HistoryAliases? LoadAliases(string ledgerPath, QueryWriter writer)
    {
        try
        {
            return HistoryAliases.Load(HistoryAliases.PathBeside(ledgerPath));
        }
        catch (Exception exception) when (IsBenign(exception))
        {
            writer.Note($"! the alias file beside the ledger could not be read and was ignored: {QueryWriter.OneLine(exception.Message, 120)}");
            return null;
        }
    }

    /// <summary>
    /// The run a report describes, as a roster and a run line. The <c>History.run.json</c> beside the
    /// report is preferred when it lists the same scenarios — it is the run's own line, fingerprints
    /// included — and the report is read directly otherwise, which yields results, attempts, durations
    /// and errors but no fingerprints, so behaviour verdicts are then off.
    /// </summary>
    public static bool TryBuild(ReportIndex index, string? suiteOverride, TextWriter error, out HistoryRoster roster, out HistoryRun run, out bool fromFragment)
        => TryBuild(index, suiteOverride, error, out roster, out run, out fromFragment, out _);

    /// <summary>
    /// The run and roster for a report: from the fragment beside it when that is this run's, else rebuilt from
    /// the report itself. <paramref name="shapes"/> is the run's distinct calls, when the fragment carried them;
    /// null when the run was rebuilt from the report.
    /// </summary>
    public static bool TryBuild(ReportIndex index, string? suiteOverride, TextWriter error, out HistoryRoster roster, out HistoryRun run, out bool fromFragment, out HistoryShapes? shapes)
    {
        roster = null!;
        run = null!;
        fromFragment = false;
        shapes = null;

        var ids = index.Scenarios.Select(s => s.StableId).ToArray();
        if (ids.Any(string.IsNullOrEmpty))
        {
            error.WriteLine($"{Path.GetFileName(index.Path)} carries no stableIds (written before 3.0.47): history is keyed on them. Re-run the suite on a current Kronikol.");
            return false;
        }

        var suite = suiteOverride ?? index.Suite;

        var fragmentPath = Path.Combine(index.Directory, HistoryFormat.FragmentFileName);
        if (File.Exists(fragmentPath))
        {
            try
            {
                var fragment = HistoryFragment.Parse(File.ReadAllText(fragmentPath));
                if (fragment.Roster.Ids.SequenceEqual(ids, StringComparer.Ordinal)
                    && (suiteOverride is null || string.Equals(fragment.Run.Suite, suiteOverride, StringComparison.Ordinal)))
                {
                    roster = fragment.Roster;
                    run = fragment.Run;
                    shapes = fragment.Shapes;
                    fromFragment = true;
                    return true;
                }
            }
            catch (Exception exception) when (IsBenign(exception))
            {
                // Not this run's fragment, or not a fragment: the report itself is read instead.
            }
        }

        var entries = index.Scenarios.Select(s => new HistoryRosterEntry(s.StableId, s.Name, s.FeatureName,
            string.IsNullOrEmpty(s.SourceFile) ? null : s.SourceLine is { } line ? $"{s.SourceFile}:{line.ToString(CultureInfo.InvariantCulture)}" : s.SourceFile)).ToArray();
        roster = HistoryRoster.Create(suite, entries);

        var results = new char[entries.Length];
        var attempts = new char[entries.Length];
        var durations = new int?[entries.Length];
        var errors = new string?[entries.Length];
        var errorText = new Dictionary<string, string>(StringComparer.Ordinal);
        var errorKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var deps = new SortedSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < index.Scenarios.Count; i++)
        {
            var scenario = index.Scenarios[i];
            results[i] = Enum.TryParse<ExecutionResult>(scenario.Result, ignoreCase: true, out var parsed) ? HistoryFormat.ResultChar(parsed) : HistoryFormat.Unknown;
            attempts[i] = HistoryFormat.AttemptChar(scenario.Attempt);
            durations[i] = scenario.DurationSeconds > 0 ? (int)Math.Round(scenario.DurationSeconds * 1000, MidpointRounding.AwayFromZero) : null;
            if (results[i] == HistoryFormat.Failed)
            {
                var text = FailureText.Truncate(FailureText.FirstLine(scenario.ErrorMessage), HistoryFormat.ErrorKeyLimit - 1);
                if (!errorKeys.TryGetValue(text, out var key))
                {
                    key = "e" + (errorKeys.Count + 1).ToString(CultureInfo.InvariantCulture);
                    errorKeys[text] = key;
                    errorText[key] = text;
                }
                errors[i] = key;
            }
            foreach (var interaction in scenario.Interactions)
                if (interaction.Type == "Request" && !string.IsNullOrEmpty(interaction.CallerName) && !string.IsNullOrEmpty(interaction.ServiceName))
                    deps.Add(interaction.CallerName + ">" + interaction.ServiceName);
        }

        var ci = CiOf(index);
        var at = DateTimeOffset.TryParse(index.EndTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var end)
            ? end
            : DateTimeOffset.UtcNow;
        run = new HistoryRun
        {
            Id = HistoryRunBuilder.RunId(ci, at),
            Suite = suite,
            Partial = null,
            At = at,
            Branch = ci?.Branch,
            Commit = ci?.CommitSha,
            Provider = ci is { Provider: not CiEnvironment.None } ? ci.Provider.ToString() : null,
            Url = ci?.PipelineUrl,
            Shards = 1,
            RosterHash = roster.Hash,
            Results = new string(results),
            Attempts = new string(attempts),
            Durations = durations,
            Calls = null,
            ShapeSet = null,
            ShapeOrdered = null,
            Errors = errors,
            ErrorText = errorText,
            Deps = deps.ToArray()
        };
        return true;
    }

    /// <summary>
    /// A report read without its fragment gets a run id minted HERE, and a local id is salted by the
    /// minting process's own directory - so it never equals the id the run recorded itself under, and the
    /// run would be read against its own ledger line: a scenario that broke reads `failing since` its own
    /// run. When the ledger holds that line it is the better reading anyway: it is the run's own,
    /// fingerprints included, so behaviour verdicts come back with it.
    /// </summary>
    public static void AdoptOwnLine(ref HistoryLedger ledger, string ledgerPath, int window, HistoryRoster roster, ref HistoryRun run, ref HistoryShapes? shapes, ref bool fromFragment)
    {
        var own = OwnLine(ledger, roster, run);
        // The reader's window: a report older than the oldest run it was given is looked for in the whole
        // ledger before it is read as a run nobody recorded.
        if (own is null && window > 0 && ledger.Runs(run.Suite) is { Count: > 0 } kept && run.At < kept[0].At
            && HistoryLedgerReader.Read(ledgerPath, 0).Ledger is { } whole && OwnLine(whole, roster, run) is { } older)
        {
            ledger = whole;
            own = older;
        }

        if (own is null)
            return;
        run = own;
        shapes = own.ShapesHash is { } shapesHash ? ledger.Shapes(shapesHash) : null;
        fromFragment = own.ShapeSet is not null;
    }

    /// <summary>
    /// The ledger's own line for a run rebuilt from a report: same suite, same scenarios in the same
    /// order, and - for a local id - the same second and the same results, whatever the salt. Null when
    /// the ledger does not hold it.
    /// </summary>
    public static HistoryRun? OwnLine(HistoryLedger ledger, HistoryRoster roster, HistoryRun rebuilt)
    {
        var runs = ledger.Runs(rebuilt.Suite);
        for (var i = runs.Count - 1; i >= 0; i--)
        {
            var candidate = runs[i];
            // The same id is not enough: on CI the ledger's line for an id is the FOLD of every shard, and
            // a shard's report read against it would take each position from somebody else's results. The
            // rebuilt run keeps the id, so the analyzer still finds where it stands.
            if (string.Equals(candidate.Id, rebuilt.Id, StringComparison.Ordinal))
                return ledger.Roster(candidate.RosterHash) is { } folded && folded.Ids.SequenceEqual(roster.Ids, StringComparer.Ordinal)
                    ? candidate
                    : null;
            var stampEnd = rebuilt.Id.LastIndexOf(':');
            var sameSecond = rebuilt.Id.StartsWith("local:", StringComparison.Ordinal)
                             && candidate.Id.Length > stampEnd
                             && string.CompareOrdinal(candidate.Id, 0, rebuilt.Id, 0, stampEnd + 1) == 0;
            if (sameSecond
                && string.Equals(candidate.Results, rebuilt.Results, StringComparison.Ordinal)
                && ledger.Roster(candidate.RosterHash) is { } theirs
                && theirs.Ids.SequenceEqual(roster.Ids, StringComparer.Ordinal))
                return candidate;
        }
        return null;
    }

    private static CiMetadata? CiOf(ReportIndex index)
    {
        if (!index.OnCi || !Enum.TryParse<CiEnvironment>(index.CiProvider, ignoreCase: true, out var provider) || provider == CiEnvironment.None)
            return null;
        return new CiMetadata(provider, index.CiBuildNumber, index.CiBranch, index.CiCommitSha, index.CiPipelineUrl, index.CiRepository, index.CiRunId, index.CiRunAttempt);
    }

    public static bool AnyFilter(QueryOptions options) =>
        options.Flaky || options.New || options.Failing || options.Regressed || options.Changed;

    public static string FilterLabel(QueryOptions options)
    {
        var parts = new List<string>();
        if (options.Regressed) parts.Add("regressed");
        if (options.Flaky) parts.Add("flaky");
        if (options.New) parts.Add("new");
        if (options.Failing) parts.Add("failing");
        if (options.Changed) parts.Add("changed");
        return parts.Count == 0 ? "" : " (" + string.Join(" or ", parts) + ")";
    }

    /// <summary>
    /// The scenarios worth a row: those carrying any verdict but stable when no filter is given, or
    /// those carrying one of the verdicts asked for. Regressions first, then the rest by address.
    /// </summary>
    public static IReadOnlyList<(HistorySubject Scenario, ScenarioHistory Entry)> Filter(IReadOnlyList<HistorySubject> subjects, HistoryVerdicts verdicts, QueryOptions options)
    {
        var rows = new List<(HistorySubject, ScenarioHistory)>();
        foreach (var scenario in subjects)
        {
            if (verdicts.At(scenario.Ordinal, scenario.StableId) is not { } entry)
                continue;
            var wanted = AnyFilter(options)
                ? options.Regressed && entry.Has(HistoryVerdictKind.Broke)
                  || options.Flaky && entry.Has(HistoryVerdictKind.Flaky)
                  || options.New && entry.Has(HistoryVerdictKind.New)
                  || options.Failing && (entry.Has(HistoryVerdictKind.Failing) || entry.Has(HistoryVerdictKind.AlwaysFailing))
                  || options.Changed && (entry.Has(HistoryVerdictKind.BehaviourChanged) || entry.Has(HistoryVerdictKind.Reordered) || entry.Has(HistoryVerdictKind.Slower))
                : entry.Primary != HistoryVerdictKind.Stable;
            if (wanted)
                rows.Add((scenario, entry));
        }

        return rows.OrderBy(r => HistorySummary.Rank(r.Item2)).ThenBy(r => r.Item1.Ordinal).ToArray();
    }

    /// <summary>
    /// The scenario's distinct calls in the run being read, as the templater wrote them; null when the
    /// run's line did not record them. Read from the run itself: the analysis spells out only the call
    /// lists a verdict is named from (#91), and this one is wanted whether or not anything changed.
    /// </summary>
    public static IReadOnlyList<string>? CallsOf(HistoryVerdicts verdicts, ScenarioHistory entry, HistoryRun run, HistoryShapes? shapes)
    {
        // The entry's place in the analysis is its place in the roster, and so in the run's columns.
        var position = -1;
        for (var i = 0; i < verdicts.Scenarios.Count && position < 0; i++)
            if (ReferenceEquals(verdicts.Scenarios[i], entry))
                position = i;
        return run.CallSetAt(position) is { } indices && shapes is not null
            ? indices.Select(shapes.At).Where(line => line is not null).Select(line => line!).ToArray()
            : null;
    }

    /// <summary>One scenario's row of the <c>--json</c> envelope.</summary>
    public static object Row(HistorySubject scenario, ScenarioHistory entry, bool detailed, bool calls = false, IReadOnlyList<string>? callLines = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["address"] = scenario.Address,
            ["stableId"] = scenario.StableId,
            ["feature"] = scenario.FeatureName,
            ["scenario"] = scenario.Name,
            ["result"] = scenario.Result,
            ["primary"] = HistoryVerdictNames.Name(entry.Primary),
            ["verdicts"] = entry.Verdicts.OrderBy(HistoryAnalyzer.Precedence).Select(HistoryVerdictNames.Name).ToArray(),
            ["evidence"] = entry.Evidence,
            ["newCalls"] = entry.NewCalls,
            ["goneCalls"] = entry.GoneCalls,
            ["series"] = entry.Series,
            ["runsSeen"] = entry.RunsSeen,
            ["failRate"] = Math.Round(entry.FailRate, 3),
            ["flipRate"] = Math.Round(entry.FlipRate, 3),
            ["flips"] = entry.Flips,
            ["failingEpisodes"] = entry.FailingEpisodes,
            ["failingSince"] = entry.FailingSince is { } since ? new { runId = since.RunId, at = since.At, commit = since.Commit, runs = since.Runs } : null,
            ["durationMs"] = entry.DurationMs,
            ["durationP95Ms"] = entry.DurationP95,
            ["durationP95IsRaw"] = entry.DurationP95IsRaw,
            ["failuresInDegradedRuns"] = entry.FailuresInDegradedRuns,
            ["quarantine"] = entry.Quarantine is { } q ? new { reason = q.Reason, addedBy = q.AddedBy, addedOn = q.AddedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), until = q.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } : null
        };
        if (calls)
            row["calls"] = callLines;
        if (detailed)
            row["runs"] = entry.Points.Select(p => new { runId = p.RunId, at = p.At, commit = p.Commit, result = p.Result.ToString(), durationMs = p.DurationMs, timesUsual = p.TimesUsual is { } t ? Math.Round(t, 2) : (double?)null, overUsual = p.OverUsual, partial = p.Partial, runDegraded = p.RunDegraded, attempt = p.Attempt, error = p.Error }).ToArray();
        return row;
    }

    /// <summary>The run-level member of the envelope.</summary>
    public static object RunLevel(HistoryReading reading, IReadOnlyList<NearMiss> misses)
    {
        var verdicts = reading.Verdicts;
        return new
        {
            summary = HistorySummary.Line(verdicts),
            // Where the run being read came from: a report, or - with no report, or with --run naming
            // another run - its own line of the ledger.
            source = reading.Source,
            ledger = reading.LedgerPath,
            ledgerSource = reading.LedgerSource.ToString(),
            stream = verdicts.Stream,
            runId = verdicts.RunId,
            runsRecorded = verdicts.RunsRecorded,
            minRuns = verdicts.MinRuns,
            coldStart = verdicts.ColdStart,
            partial = verdicts.Partial,
            previousFullCount = verdicts.PreviousFullCount,
            pace = verdicts.Runs.Count > 0 && verdicts.Runs[^1].Pace is { } pace ? Math.Round(pace, 2) : (double?)null,
            degraded = verdicts.Runs.Count > 0 && verdicts.Runs[^1].Degraded,
            behaviourVerdicts = reading.BehaviourVerdicts,
            counts = verdicts.Counts.OrderBy(p => HistoryAnalyzer.Precedence(p.Key)).ToDictionary(p => HistoryVerdictNames.Name(p.Key), p => p.Value),
            absent = verdicts.Absent.Select(a => new { stableId = a.StableId, feature = a.Feature, scenario = a.Name, lastRunId = a.LastRunId }).ToArray(),
            newDependencies = verdicts.NewDependencies,
            nearMisses = misses.Select(m => new { address = m.Subject.Address, stableId = m.Subject.StableId, kind = m.Kind, reason = m.Reason, runId = m.RunId, runsAgo = m.RunsAgo }).ToArray(),
            failingOutside = verdicts.FailingOutside.Select(o => new { stableId = o.StableId, feature = o.Feature, scenario = o.Name, runId = o.RunId, runsAgo = o.RunsAgo }).ToArray(),
            compare = verdicts.Compare is { } c ? new { stream = c.Stream, summary = HistorySummary.Line(c), runsRecorded = c.RunsRecorded } : null
        };
    }

    /// <summary>The analysis options the verb's flags give, the same for a report and for a ledger line.</summary>
    public static HistoryAnalysisOptions AnalysisOptions(QueryOptions options, Func<string, string?> getEnv)
    {
        var analysis = new HistoryAnalysisOptions
        {
            Window = options.HistoryWindowOrDefault,
            // A pull request build reads against the branch it targets, as the run itself did.
            Branch = options.Branch ?? CiMetadataDetector.PullRequestTarget(getEnv),
            CompareBranch = options.CompareBranch
        };
        if (options.MinRuns is { } minRuns)
            analysis = analysis with { MinRuns = minRuns };
        if (options.AlternatingRuns is { } alternatingRuns)
            analysis = analysis with { AlternatingRuns = alternatingRuns };
        if (options.CountRuns is { } countRuns)
            analysis = analysis with { CountRuns = countRuns };
        if (options.DegradedBy is { } degradedBy)
            analysis = analysis with { DegradedBy = degradedBy };
        return analysis;
    }

    /// <summary>Whether <paramref name="wanted"/> is this run's own id, spelled out.</summary>
    public static bool Names(HistoryRun run, string wanted) => string.Equals(run.Id, wanted, StringComparison.Ordinal);

    /// <summary>The two names a run can be asked for by without knowing its id.</summary>
    public const string LastFailedAlias = "last-failed", PreviousAlias = "previous";

    /// <summary>
    /// The run <c>--run</c> names, among the suite's runs (and the stream's, when <paramref name="stream"/>
    /// is given): a whole id, a substring only one id has, or an alias. Null - with the refusal, and the
    /// runs there are, already written - when it names none or more than one, so the refusal is the index.
    /// </summary>
    public static HistoryRun? ResolveRun(HistoryLedger ledger, string? suite, string? stream, string wanted, TextWriter error)
    {
        var runs = ledger.Runs(suite).Where(r => stream is null || string.Equals(r.Stream, stream, StringComparison.Ordinal)).ToList();
        var label = (suite ?? "(unnamed)") + (stream is null ? "" : $" on {stream}");

        if (string.Equals(wanted, LastFailedAlias, StringComparison.OrdinalIgnoreCase))
        {
            if (runs.LastOrDefault(r => r.Results.Contains(HistoryFormat.Failed)) is { } failed)
                return failed;
            error.WriteLine($"--run {LastFailedAlias}: no run of {label} in the ledger has a failure ({runs.Count} run(s) read).");
            return null;
        }

        if (string.Equals(wanted, PreviousAlias, StringComparison.OrdinalIgnoreCase))
        {
            if (runs.Count >= 2)
                return runs[^2];
            error.WriteLine($"--run {PreviousAlias}: the ledger holds {runs.Count} run(s) of {label} — there is no run before the newest.");
            return null;
        }

        if (runs.LastOrDefault(r => string.Equals(r.Id, wanted, StringComparison.Ordinal)) is { } exact)
            return exact;

        var matches = runs.Where(r => r.Id.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
            return matches[0];

        error.WriteLine(matches.Count == 0
            ? $"No run matching {wanted} among the {runs.Count} run(s) of {label} in the ledger. The newest:"
            : $"{matches.Count} runs of {label} match {wanted} — name one. The newest:");
        foreach (var run in (matches.Count == 0 ? runs : matches).TakeLast(10).Reverse())
        {
            var failed = run.Results.Count(c => c == HistoryFormat.Failed);
            error.WriteLine($"  {run.Id}  {run.At.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)}  {run.Stream}  {run.Results.Count(c => c == HistoryFormat.Passed)} passed, {failed} failed{(run.Partial == true ? ", partial" : "")}");
        }
        error.WriteLine($"Aliases: {LastFailedAlias} (the newest run with a failure), {PreviousAlias} (the run before the newest).");
        return null;
    }

    /// <summary>What an empty answer is empty OF: this run, and the verdict asked for - never "nothing failed".</summary>
    public static string EmptyNote(HistoryReading reading, QueryOptions options)
    {
        if (!AnyFilter(options))
            return "no scenario in this run has a verdict other than stable";

        // `failing` is a verdict - failing now AND before - so a scenario that broke in this run fails it
        // and is not in the answer. Only a run with no failure at all may be told nothing is failing.
        var failedNow = reading.Verdicts.Scenarios.Any(s => s.Current == HistoryFormat.Failed);
        var parts = new List<string>();
        if (options.Regressed) parts.Add(failedNow ? "broke in this run (what fails in it was failing already)" : "broke in this run");
        if (options.Flaky) parts.Add("is flaky as of this run");
        if (options.New) parts.Add("is new in this run");
        if (options.Failing) parts.Add(failedNow ? "has been failing since an earlier run (what fails in this one broke in it)" : "is failing in this run");
        if (options.Changed) parts.Add("changed its calls or its duration in this run");
        return "no scenario " + string.Join(", or ", parts);
    }

    /// <summary>
    /// What an empty filter nearly held: a scenario of this run that failed earlier in the window, one that
    /// fails now under another verdict, one that has flipped without being flaky. Newest failure first.
    /// </summary>
    public static IReadOnlyList<NearMiss> NearMisses(HistoryReading reading, QueryOptions options)
    {
        var verdicts = reading.Verdicts;
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < verdicts.Runs.Count; i++)
            position[verdicts.Runs[i].RunId] = i;
        // The distance in the STREAM's runs: LastFailedRunsAgo counts the scenario's own verdicts, which
        // on a stream of filtered re-runs is a smaller number than the reader means.
        int? Ago(string runId) => position.TryGetValue(runId, out var at) ? verdicts.Runs.Count - 1 - at : null;

        var misses = new List<NearMiss>();
        foreach (var subject in reading.Subjects)
        {
            if (verdicts.At(subject.Ordinal, subject.StableId) is not { } entry)
                continue;
            var failedNow = entry.Current == HistoryFormat.Failed;
            var lastFailure = entry.Points.Take(entry.Points.Count - 1).LastOrDefault(p => p.Result == HistoryFormat.Failed);

            if (options.Failing || options.Regressed)
            {
                if (failedNow)
                    misses.Add(new NearMiss(subject, entry, NearMiss.FailedNow, HistoryVerdictNames.Name(entry.Primary), verdicts.RunId, 0));
                else if (lastFailure is not null)
                    misses.Add(new NearMiss(subject, entry, NearMiss.FailedEarlier, null, lastFailure.RunId, Ago(lastFailure.RunId) ?? entry.LastFailedRunsAgo ?? 0));
            }

            if (options.Flaky && entry.FlakyShortfall != HistoryFlakyShortfall.None)
                misses.Add(new NearMiss(subject, entry, NearMiss.FlipsNotFlaky, ShortfallName(entry.FlakyShortfall),
                    failedNow ? verdicts.RunId : lastFailure?.RunId, failedNow ? 0 : lastFailure is null ? 0 : Ago(lastFailure.RunId) ?? entry.LastFailedRunsAgo ?? 0));
        }

        return misses.OrderBy(m => m.RunsAgo).ThenBy(m => m.Subject.Ordinal).ToArray();
    }

    private static string ShortfallName(HistoryFlakyShortfall shortfall)
    {
        var names = new List<string>();
        if (shortfall.HasFlag(HistoryFlakyShortfall.OneEpisode)) names.Add("one-episode");
        if (shortfall.HasFlag(HistoryFlakyShortfall.TooFewVerdicts)) names.Add("too-few-verdicts");
        if (shortfall.HasFlag(HistoryFlakyShortfall.BelowRate)) names.Add("below-rate");
        return string.Join("+", names);
    }

    /// <summary>
    /// The reason, from the analyzer's own record and never inferred here (#84 inferred it, and named a bar
    /// the scenario had met). The episode leads when it applies, and then the bar is stated without its
    /// flag: lowering <c>--min-runs</c> makes nothing flaky that has failed in one stretch only.
    /// </summary>
    internal static string ShortfallText(ScenarioHistory entry, HistoryReading reading)
    {
        var shortfall = entry.FlakyShortfall;
        var parts = new List<string>();
        if (shortfall.HasFlag(HistoryFlakyShortfall.OneEpisode))
            parts.Add("one failing episode");
        if (shortfall.HasFlag(HistoryFlakyShortfall.TooFewVerdicts))
            parts.Add(shortfall.HasFlag(HistoryFlakyShortfall.OneEpisode)
                ? $"and {entry.RealVerdicts} of the {reading.Verdicts.MinRuns} verdicts"
                : $"{entry.RealVerdicts} of the {reading.Verdicts.MinRuns} verdicts flaky needs (--min-runs)");
        if (shortfall.HasFlag(HistoryFlakyShortfall.BelowRate) && !shortfall.HasFlag(HistoryFlakyShortfall.OneEpisode))
            parts.Add($"a flip rate of {entry.FlipRate.ToString("0.00", CultureInfo.InvariantCulture)}, under the {reading.Analysis.FlakyRate.ToString("0.00", CultureInfo.InvariantCulture)} flaky needs");
        return string.Join(" ", parts);
    }

    /// <summary>The hint lines under an empty answer, and the partial run's <c>also:</c> line. At most five addresses each.</summary>
    public static IEnumerable<string> HintLines(HistoryReading reading, IReadOnlyList<NearMiss> misses, IReadOnlyList<FailingOutside> outside)
    {
        const int runsBack = 5, addresses = 5;
        static string Plural(int n, string one, string many) => n == 1 ? one : many;
        static string More(int total) => total > addresses ? $" … and {total - addresses} more" : "";

        var now = misses.Where(m => m.Kind == NearMiss.FailedNow).ToList();
        if (now.Count > 0)
            yield return $"{now.Count} {Plural(now.Count, "scenario", "scenarios")} failed in this run under another verdict: "
                         + string.Join(", ", now.Take(addresses).Select(m => $"{m.Subject.Address} ({m.Reason})")) + More(now.Count)
                         + $" · next: history {reading.Next(now[0].Subject)}";

        // An older failure is counted, not listed - but never without an address. The run view lists
        // verdicts, and a scenario that failed eight runs ago and has passed since is `stable`: it is in no
        // listing this verb prints, so "see the unfiltered view" would be one more empty answer.
        var earlier = misses.Where(m => m.Kind == NearMiss.FailedEarlier).ToList();
        var recent = earlier.Where(m => m.RunsAgo <= runsBack).ToList();
        var older = earlier.Count - recent.Count;
        var newestOlder = earlier.FirstOrDefault(m => m.RunsAgo > runsBack);
        if (recent.Count > 0)
        {
            // The run is said once per run of addresses that share it: five scenarios one run broke are one fact.
            string? said = null;
            var named = recent.Take(addresses).Select(m =>
            {
                var where = $"{m.RunsAgo} {Plural(m.RunsAgo, "run", "runs")} ago, {m.RunId}";
                var text = where == said ? m.Subject.Address : $"{m.Subject.Address} ({where})";
                said = where;
                return text;
            }).ToList();
            yield return $"{recent.Count} {Plural(recent.Count, "scenario", "scenarios")} here failed earlier in the window: {string.Join(", ", named)}{More(recent.Count)}"
                         + (newestOlder is not null ? $" · and {older} older, the newest {newestOlder.RunsAgo} runs ago in {newestOlder.RunId}" : "")
                         + $" · next: history {reading.Next(recent[0].Subject)}";
        }
        else if (newestOlder is not null)
        {
            yield return $"{older} {Plural(older, "scenario", "scenarios")} here failed more than {runsBack} runs ago, the newest {newestOlder.Subject.Address} ({newestOlder.RunsAgo} runs ago, {newestOlder.RunId})"
                         + $" · next: history {reading.Next(newestOlder.Subject)}";
        }

        var flips = misses.Where(m => m.Kind == NearMiss.FlipsNotFlaky).ToList();
        var recentFlips = flips.Where(m => m.RunsAgo <= runsBack).ToList();
        if (recentFlips.Count > 0)
            yield return $"{recentFlips.Count} {Plural(recentFlips.Count, "scenario has", "scenarios have")} flips but {Plural(recentFlips.Count, "is", "are")} not flaky: "
                         + string.Join("; ", recentFlips.Take(addresses).Select(m => $"{m.Subject.Address} — {m.Entry.Flips} {Plural(m.Entry.Flips, "flip", "flips")}, {ShortfallText(m.Entry, reading)}")) + More(recentFlips.Count)
                         + (recentFlips.Any(m => m.Entry.FlakyShortfall.HasFlag(HistoryFlakyShortfall.OneEpisode)) ? "; flaky needs two" : "")
                         + (flips.Count > recentFlips.Count ? $" · and {flips.Count - recentFlips.Count} whose last failure is more than {runsBack} runs back" : "")
                         + $" · next: history {reading.Next(recentFlips[0].Subject)}";
        else if (flips.Count > 0)
            yield return $"{flips.Count} {Plural(flips.Count, "scenario has", "scenarios have")} flips but {Plural(flips.Count, "is", "are")} not flaky, the last failure more than {runsBack} runs back: "
                         + $"{flips[0].Subject.Address} — {flips[0].Entry.Flips} {Plural(flips[0].Entry.Flips, "flip", "flips")}, {ShortfallText(flips[0].Entry, reading)} ({flips[0].RunsAgo} runs ago)"
                         + $" · next: history {reading.Next(flips[0].Subject)}";

        if (outside.Count > 0)
        {
            // The run is named, not aliased: `last-failed` is the newest run with ANY failure, which a newer
            // filtered run can be. And unfiltered: what failed there may have BROKEN there, and `--failing`
            // would be empty a second time.
            var newest = outside.OrderBy(o => o.RunsAgo).First();
            yield return $"also: {outside.Count} {Plural(outside.Count, "scenario", "scenarios")} outside this partial run {Plural(outside.Count, "was", "were")} failing when {Plural(outside.Count, "it", "they")} last ran "
                         + (outside.Count == 1 ? $"({newest.RunId})" : $"(most recently in {newest.RunId})")
                         + $" · next: history --run {newest.RunId}";
        }
    }
}

/// <summary>
/// What a history row is about: a scenario of the report (<c>s3</c>), or - with no report - a position of
/// the run's roster, which has no ordinal anybody else can use and is addressed by its stable id.
/// </summary>
internal sealed record HistorySubject(string Address, int Ordinal, string StableId, string FeatureName, string Name, string Result)
{
    public static HistorySubject Of(ScenarioEntry scenario) =>
        new(scenario.Address, scenario.Ordinal, scenario.StableId, scenario.FeatureName, scenario.Name, scenario.Result);

    public static HistorySubject Of(HistoryRoster roster, HistoryRun run, int position) =>
        new("sid:" + roster.Ids[position], position, roster.Ids[position], roster.Features[position], roster.Names[position], ResultName(run.ResultAt(position)));

    /// <summary>What follows <c>history</c> to ask for this scenario alone: the address with a report, the flag without one.</summary>
    public string NextArgs => Address.StartsWith("sid:", StringComparison.Ordinal) ? "--sid " + StableId : Address;

    private static string ResultName(char result) => result switch
    {
        HistoryFormat.Passed => nameof(ExecutionResult.Passed),
        HistoryFormat.Failed => nameof(ExecutionResult.Failed),
        HistoryFormat.Skipped => nameof(ExecutionResult.Skipped),
        HistoryFormat.Bypassed => nameof(ExecutionResult.Bypassed),
        HistoryFormat.SkippedAfterFailure => nameof(ExecutionResult.SkippedAfterFailure),
        _ => "Unknown"
    };
}

/// <summary>One reading of one run, however the run was come by.</summary>
/// <param name="Source"><see cref="FromReport"/> or <see cref="FromLedger"/>.</param>
/// <param name="Run">The run that was analysed, for what is read off its own line (<c>--calls</c>).</param>
internal sealed record HistoryReading(HistoryVerdicts Verdicts, IReadOnlyList<HistorySubject> Subjects, string LedgerPath, HistoryLocationSource LedgerSource,
    bool BehaviourVerdicts, string Source, HistoryAnalysisOptions Analysis, HistoryRun Run, HistoryShapes? Shapes)
{
    public const string FromReport = "report", FromLedger = "ledger";

    /// <summary>
    /// What follows <c>history</c> to ask for one scenario of THIS reading. A run read from the ledger is
    /// named every time, the newest included: followed a run later, a bare <c>--sid</c> would land in
    /// another run, and one the scenario may not be in.
    /// </summary>
    public string Next(HistorySubject subject) =>
        Source == FromLedger ? $"--run {Verdicts.RunId} {subject.NextArgs}" : subject.NextArgs;
}

/// <summary>A scenario an empty filter nearly held.</summary>
/// <param name="Kind">One of the three constants.</param>
/// <param name="Reason">For <see cref="FlipsNotFlaky"/>, the analyzer's shortfall; for <see cref="FailedNow"/>, the verdict it has instead.</param>
internal sealed record NearMiss(HistorySubject Subject, ScenarioHistory Entry, string Kind, string? Reason, string? RunId, int RunsAgo)
{
    public const string FailedEarlier = "failed-earlier", FailedNow = "failed-now", FlipsNotFlaky = "flips-not-flaky";
}
