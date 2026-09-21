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

        var read = HistoryLedgerReader.Read(ledgerPath, options.HistoryWindowOrDefault);
        if (read.Ledger is not { } ledger)
        {
            error.WriteLine(read.Message ?? $"the ledger at {ledgerPath} could not be read");
            return 1;
        }

        if (!ReportHistory.TryBuild(index, options.SuiteOverride, error, out var roster, out var run, out var fromFragment, out var shapes))
            return 2;

        if (!fromFragment)
            ReportHistory.AdoptOwnLine(ref ledger, ledgerPath, options.HistoryWindowOrDefault, roster, ref run, ref shapes, ref fromFragment);

        var quarantine = ReportHistory.LoadQuarantine(ledgerPath, writer);
        var aliases = ReportHistory.LoadAliases(ledgerPath, writer);
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
        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, run, analysis, quarantine, aliases, shapes: shapes);

        // Under --count the text answer is one bare number, so the caveats go to stderr - the same rule
        // WriteProvenance applies to the provenance notes.
        Action<string> note = options.Count && !options.Json ? error.WriteLine : writer.Note;
        if (read.Outcome == HistoryReadOutcome.Missing)
            note($"! no ledger at {ledgerPath} yet — every scenario reads as its first run; the next recorded run starts the history");
        if (ledger.Stats.DamagedLines > 0)
            note($"! {ledger.Stats.DamagedLines} line(s) of the ledger could not be parsed and were skipped — kronikol history verify says which");
        if (!fromFragment)
            note($"! no {HistoryFormat.FragmentFileName} beside the report — behaviour verdicts are off; results, attempts and durations still compare");

        // ── One scenario ───────────────────────────────────
        if (options.Positional.Count > 0)
        {
            if (!TryScenario(index, options, error, out var scenario, out var step))
                return 2;
            if (step is not null)
                note($"! history is per scenario — scoped to {scenario.Address}; `steps {scenario.Address}/{step}` answers for the step");

            var entry = verdicts.At(scenario.Ordinal, scenario.StableId);
            if (entry is null)
            {
                error.WriteLine($"{scenario.Address} has no history entry — the report's scenario list and the ledger disagree; re-run kronikol history record for this run.");
                return 1;
            }

            if (options.Count)
            {
                writer.Count(entry.Verdicts.Count);
                return 0;
            }

            WriteScenario(writer, verdicts, scenario, entry);
            var callLines = options.Calls ? ReportHistory.CallsOf(verdicts, entry, run, shapes) : null;
            if (options.Calls)
                WriteCalls(writer, callLines);
            if (verdicts.Compare is { } compared && compared.At(scenario.Ordinal, scenario.StableId) is { } other)
            {
                writer.Line();
                writer.Line($"on {compared.Stream}: {HistoryVerdictNames.Name(other.Primary)} — {QueryWriter.Flat(other.Evidence)} · {other.Series}");
            }
            writer.Data("history", ReportHistory.RunLevel(verdicts, ledgerPath, location.Source, fromFragment));
            writer.Item(ReportHistory.Row(scenario, entry, detailed: true, calls: options.Calls, callLines: callLines));
            writer.Footer($"{scenario.Address} · {verdicts.RunsRecorded} earlier run(s) on {verdicts.Stream} · next: history · failures");
            return 0;
        }

        // ── The run ────────────────────────────────────────
        var filtered = ReportHistory.Filter(index, verdicts, options);
        if (options.Count)
        {
            writer.Count(filtered.Count);
            return 0;
        }

        writer.Line($"history: {HistorySummary.Line(verdicts)}");
        writer.Line($"ledger: {ledgerPath} · stream {verdicts.Stream} · run {verdicts.RunId}"
                    + (verdicts.Partial ? " · partial" : ""));
        // The run's label speaks once, here: in a contended run a third of the rows would carry a note.
        if (HistorySummary.Degraded(verdicts.Runs[^1]) is { } degraded)
            writer.Line($"{degraded}, so no scenario is read slower in this run");
        if (verdicts.ColdStart)
            writer.Line(verdicts.ColdStartMessage!);
        if (verdicts.Compare is { } compare)
            writer.Line($"on {compare.Stream}: {HistorySummary.Line(compare)}");
        writer.Line();

        var absentNoted = false;
        if (filtered.Count == 0)
        {
            // Said outright rather than through the pager, whose empty page reads "nothing at --offset 0".
            writer.Note($"no scenario with a verdict{ReportHistory.FilterLabel(options)} · {verdicts.Scenarios.Count} scenarios read against {verdicts.RunsRecorded} earlier run(s)");
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
                writer.CutNotice($"… marks cut text — history {scenario.Address} prints it whole");
            if (entry.Verdicts.Count > 1)
                writer.Line($"  also: {string.Join(", ", entry.Verdicts.Where(v => v != entry.Primary).OrderBy(HistoryAnalyzer.Precedence).Select(HistoryVerdictNames.Name))}");
        }, options.RerunArgs(), pair => ReportHistory.Row(pair.Scenario, pair.Entry, detailed: false),
            whenComplete: $"{filtered.Count} scenario(s) with a verdict{ReportHistory.FilterLabel(options)} · history s3 for one in full");

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

        writer.Data("history", ReportHistory.RunLevel(verdicts, ledgerPath, location.Source, fromFragment));
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

    private static void WriteScenario(QueryWriter writer, HistoryVerdicts verdicts, ScenarioEntry scenario, ScenarioHistory entry)
    {
        writer.Line($"{scenario.Address}  {scenario.FeatureName} › {scenario.Name}  sid:{scenario.StableId}");
        writer.Line($"verdict: {entry.VerdictNames}");
        // Nothing in this view is cut (#82): it is at most fifteen rows of strings the ledger has already
        // capped, and the characters a cut removes are the ones that say what failed.
        writer.Line($"evidence: {QueryWriter.Flat(entry.Evidence)}");
        writer.Line($"runs seen: {entry.RunsSeen} · verdicts: {entry.RealVerdicts} · failed {entry.Failures}{(entry.FailuresInDegradedRuns > 0 ? $" ({entry.FailuresInDegradedRuns} in a degraded run)" : "")} · flips {entry.Flips} · flip rate {entry.FlipRate.ToString("0.00", CultureInfo.InvariantCulture)} · fail rate {entry.FailRate.ToString("0.00", CultureInfo.InvariantCulture)}"
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
            writer.Line($"       {QueryWriter.Flat(e)}");
            firstLineOnly |= e.Length >= HistoryFormat.ErrorKeyLimit - 1 && e.EndsWith('…');
        }

        // The ledger keeps the first line of a message, up to 199 characters: text that ends in ITS
        // ellipsis is not cut by this view, and the rest is not in the ledger at all.
        if (firstLineOnly)
            writer.Line("       … first line only — the whole message is in that run's Failures.md");
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
    public static IReadOnlyList<(ScenarioEntry Scenario, ScenarioHistory Entry)> Filter(ReportIndex index, HistoryVerdicts verdicts, QueryOptions options)
    {
        var rows = new List<(ScenarioEntry, ScenarioHistory)>();
        foreach (var scenario in index.Scenarios)
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
    public static object Row(ScenarioEntry scenario, ScenarioHistory entry, bool detailed, bool calls = false, IReadOnlyList<string>? callLines = null)
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
    public static object RunLevel(HistoryVerdicts verdicts, string ledgerPath, HistoryLocationSource source, bool fromFragment) => new
    {
        summary = HistorySummary.Line(verdicts),
        ledger = ledgerPath,
        ledgerSource = source.ToString(),
        stream = verdicts.Stream,
        runId = verdicts.RunId,
        runsRecorded = verdicts.RunsRecorded,
        minRuns = verdicts.MinRuns,
        coldStart = verdicts.ColdStart,
        partial = verdicts.Partial,
        pace = verdicts.Runs.Count > 0 && verdicts.Runs[^1].Pace is { } pace ? Math.Round(pace, 2) : (double?)null,
        degraded = verdicts.Runs.Count > 0 && verdicts.Runs[^1].Degraded,
        behaviourVerdicts = fromFragment,
        counts = verdicts.Counts.OrderBy(p => HistoryAnalyzer.Precedence(p.Key)).ToDictionary(p => HistoryVerdictNames.Name(p.Key), p => p.Value),
        absent = verdicts.Absent.Select(a => new { stableId = a.StableId, feature = a.Feature, scenario = a.Name, lastRunId = a.LastRunId }).ToArray(),
        newDependencies = verdicts.NewDependencies,
        compare = verdicts.Compare is { } c ? new { stream = c.Stream, summary = HistorySummary.Line(c), runsRecorded = c.RunsRecorded } : null
    };
}
