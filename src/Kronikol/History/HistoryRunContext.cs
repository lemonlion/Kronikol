using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.History;

/// <summary>
/// Everything a report generation knows about history, gathered once before any output is written and
/// handed to every writer that wants it — the digest, the CTRF document, the console pointer, the HTML —
/// so they all say the same thing about the same run.
/// </summary>
public sealed class HistoryRunContext
{
    private HistoryRunContext(HistoryLocation location, HistoryRoster roster, HistoryRun run, HistoryShapes? shapes, HistoryVerdicts? verdicts, HistoryLedger? ledger,
        bool writeLedger, string generator, HistoryQuarantineList? quarantine, HistoryAliases? aliases)
    {
        Shapes = shapes;
        Location = location;
        Roster = roster;
        Run = run;
        Verdicts = verdicts;
        Ledger = ledger;
        WriteLedger = writeLedger;
        Generator = generator;
        Quarantine = quarantine;
        Aliases = aliases;
    }

    /// <summary>Where the ledger is, or why not.</summary>
    public HistoryLocation Location { get; }

    /// <summary>This run's roster.</summary>
    public HistoryRoster Roster { get; }

    /// <summary>This run's line, with <see cref="HistoryRun.Partial"/> resolved once the verdicts are.</summary>
    public HistoryRun Run { get; private set; }

    /// <summary>The distinct calls this run's line indexes into; null when shapes are switched off.</summary>
    public HistoryShapes? Shapes { get; }

    /// <summary>The verdicts against the ledger; null when there was no ledger to read.</summary>
    public HistoryVerdicts? Verdicts { get; }

    /// <summary>The ledger as read; null when unavailable.</summary>
    public HistoryLedger? Ledger { get; }

    /// <summary>Whether this run appends its line to the ledger.</summary>
    public bool WriteLedger { get; }

    /// <summary>The Kronikol version, written as the generator.</summary>
    public string Generator { get; }

    /// <summary>The quarantine list beside the ledger, when there is one.</summary>
    public HistoryQuarantineList? Quarantine { get; }

    /// <summary>The rename aliases beside the ledger, when there are any.</summary>
    public HistoryAliases? Aliases { get; }

    /// <summary>The fragment for this run — the shard unit <c>kronikol history record</c> folds.</summary>
    public string Fragment() => HistoryFragment.Write(Roster, Run, Generator, Shapes);

    /// <summary>
    /// Builds the context for a run: resolves the ledger, reads it, builds the run, analyses it, and
    /// records a diagnostic for anything that stopped it. Never throws — a test run must not fail over
    /// its history — and returns null only when history is switched off or the run has nothing in it.
    /// </summary>
    public static HistoryRunContext? Create(Feature[] features, RequestResponseLog?[] logs, string? suite, CiMetadata? ci, DateTimeOffset at,
        ReportConfigurationOptions options, string reportsDirectory, string generator, Func<string, string?>? getEnv = null, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            var env = getEnv ?? Environment.GetEnvironmentVariable;
            var location = HistoryPathResolver.Resolve(options.HistoryFilePath, reportsDirectory, env, baseDirectory ?? AppContext.BaseDirectory);
            if (location.Source == HistoryLocationSource.Disabled)
                return null;

            var build = new HistoryBuildOptions
            {
                Durations = options.HistoryDurations,
                Shapes = options.HistoryShapes,
                ErrorKeys = options.HistoryErrorKeys,
                Partial = options.HistoryPartialRun
            };
            var (roster, run, shapes) = HistoryRunBuilder.Build(features, logs, suite, ci, at, build,
                string.IsNullOrWhiteSpace(options.HistoryRunId) ? null : options.HistoryRunId.Trim());

            HistoryLedger? ledger = null;
            HistoryVerdicts? verdicts = null;
            HistoryQuarantineList? quarantine = null;
            HistoryAliases? aliases = null;

            if (location.Path is { } path)
            {
                var read = HistoryLedgerReader.Read(path, Math.Max(options.HistoryWindow, 1));
                switch (read.Outcome)
                {
                    case HistoryReadOutcome.Read:
                    case HistoryReadOutcome.Missing:
                        ledger = read.Ledger;
                        break;
                    default:
                        ReportDiagnosticsScope.Record(DiagnosticKind.HistoryUnavailable, read.Message ?? $"the ledger at {path} could not be read");
                        break;
                }

                if (ledger is { Stats.DamagedLines: > 0 })
                    ReportDiagnosticsScope.Record(DiagnosticKind.HistoryLedgerDamaged,
                        $"{ledger.Stats.DamagedLines} line(s) of the ledger at {path} could not be parsed and were skipped; run kronikol history verify");

                if (ledger is not null)
                {
                    quarantine = TryLoad(() => HistoryQuarantineList.Load(HistoryQuarantineList.PathBeside(path)), "quarantine list");
                    aliases = TryLoad(() => HistoryAliases.Load(HistoryAliases.PathBeside(path)), "alias file");
                    verdicts = HistoryAnalyzer.Analyse(ledger, roster, run, new HistoryAnalysisOptions
                    {
                        Window = options.HistoryWindow,
                        MinRuns = options.HistoryMinRuns,
                        FlakyRate = options.HistoryFlakyRate,
                        SlowerBy = options.HistorySlowerBy,
                        SlowerMinMs = options.HistorySlowerMinMs,
                        AlternatingRuns = options.HistoryAlternatingRuns,
                        CountRuns = options.HistoryCountRuns,
                        PartialThreshold = options.HistoryPartialThreshold,
                        ReportReordered = options.HistoryReordered,
                        // A pull request's runs form their own stream, and the question a pull request asks is
                        // what changed against the branch it targets: that is the default stream on a pull
                        // request build. A push, a schedule and a run off CI read their own.
                        Branch = options.HistoryBranch is null ? CiMetadataDetector.PullRequestTarget(env)
                            : string.IsNullOrWhiteSpace(options.HistoryBranch) ? null : options.HistoryBranch.Trim(),
                        CompareBranch = string.IsNullOrWhiteSpace(options.HistoryCompareBranch) ? null : options.HistoryCompareBranch.Trim()
                    }, quarantine, aliases, shapes: shapes);

                    if (verdicts.Partial)
                    {
                        run = run with { Partial = true };
                        if (options.HistoryPartialRun is null)
                            ReportDiagnosticsScope.Record(DiagnosticKind.HistoryPartialRun,
                                $"this run has fewer than {100 - (int)(options.HistoryPartialThreshold * 100)}% of the previous run's scenarios and is recorded as partial: nothing is reported absent, and it is not the run the next one is compared against. Set ReportConfigurationOptions.HistoryPartialRun to say otherwise.");
                    }
                    else if (run.Partial is null)
                    {
                        run = run with { Partial = false };
                    }
                }
            }
            else if (location.Source == HistoryLocationSource.None)
            {
                ReportDiagnosticsScope.Record(DiagnosticKind.HistoryUnavailable, location.Message ?? "no history ledger");
            }

            // A ledger that could not be read is not appended to either: the writer would refuse or time
            // out for the same reason, and one diagnostic says it.
            var writeLedger = options.WriteHistoryLedger ?? (location.Source is HistoryLocationSource.Option or HistoryLocationSource.Environment || ci is null);
            return new HistoryRunContext(location, roster, run, shapes, verdicts, ledger, writeLedger && location.Path is not null && ledger is not null, generator, quarantine, aliases);
        }
        catch (Exception exception)
        {
            ReportDiagnosticsScope.Record(DiagnosticKind.HistoryUnavailable, "history could not be prepared for this run", exception);
            return null;
        }
    }

    private static T? TryLoad<T>(Func<T> load, string what) where T : class
    {
        try
        {
            return load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            ReportDiagnosticsScope.Record(DiagnosticKind.HistoryUnavailable, $"the {what} beside the ledger could not be read and was ignored", exception);
            return null;
        }
    }

    /// <summary>
    /// Appends this run's line to the ledger, when the run writes one. Called after every output is on
    /// disk, so a run whose report failed to write still has its fragment for the fold, and a lock that
    /// cannot be won costs a diagnostic rather than the run.
    /// </summary>
    public HistoryAppendResult? Append()
    {
        if (!WriteLedger || Location.Path is not { } path)
            return null;

        try
        {
            var result = HistoryLedgerWriter.Append(path, Roster, Run, Generator, shapes: Shapes);
            switch (result.Outcome)
            {
                case HistoryAppendOutcome.Appended:
                case HistoryAppendOutcome.Duplicate:
                    break;
                default:
                    ReportDiagnosticsScope.Record(DiagnosticKind.HistoryUnavailable, result.Message ?? $"the run was not appended to the ledger at {path} ({result.Outcome})");
                    break;
            }
            return result;
        }
        catch (Exception exception)
        {
            ReportDiagnosticsScope.Record(DiagnosticKind.HistoryUnavailable, $"the run could not be appended to the ledger at {path}", exception);
            return null;
        }
    }

    /// <summary>
    /// The one line the console pointer and the CI summary print about history: the counts that matter,
    /// or why there are none.
    /// </summary>
    public string Summary()
    {
        if (Verdicts is not null)
            return "history: " + HistorySummary.Line(Verdicts) + HistorySummary.CompareTail(Verdicts);

        return Location.Source switch
        {
            HistoryLocationSource.None => "history: no ledger found (the History.run.json fragment was written; the HistoryUnavailable diagnostic says how to enable it)",
            _ => "history: the ledger could not be read (see the HistoryUnavailable diagnostic)"
        };
    }
}

/// <summary>The one-line reading of a run's verdicts every surface prints the same way.</summary>
public static class HistorySummary
{
    /// <summary>The counts that matter and what they were measured against; "nothing changed" when none do.</summary>
    public static string Line(HistoryVerdicts verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);

        var parts = new List<string>();
        void Count(HistoryVerdictKind kind, string label)
        {
            var n = verdicts.Count(kind);
            if (n > 0) parts.Add($"{n} {label}");
        }

        Count(HistoryVerdictKind.Broke, "broke");
        Count(HistoryVerdictKind.Fixed, "fixed");
        Count(HistoryVerdictKind.Flaky, "flaky");
        Count(HistoryVerdictKind.Failing, "still failing");
        Count(HistoryVerdictKind.AlwaysFailing, "always failing");
        Count(HistoryVerdictKind.New, "new");
        Count(HistoryVerdictKind.Slower, "slower");
        Count(HistoryVerdictKind.BehaviourChanged, "behaviour changed");
        Count(HistoryVerdictKind.Quarantined, "quarantined");
        if (verdicts.Absent.Count > 0) parts.Add($"{verdicts.Absent.Count} absent");

        var head = parts.Count == 0 ? "nothing changed" : string.Join(", ", parts);
        var tail = verdicts.ColdStart
            ? $" ({verdicts.ColdStartMessage})"
            : $" (against {verdicts.RunsRecorded} earlier run{(verdicts.RunsRecorded == 1 ? "" : "s")} on {verdicts.Stream})";
        return head + tail;
    }

    /// <summary>
    /// The second reading, when the run was read against another stream too: <c> · on main: 1 broke
    /// (against 12 earlier runs on main)</c>, or nothing.
    /// </summary>
    public static string CompareTail(HistoryVerdicts verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        return verdicts.Compare is { } compare ? $" · on {compare.Stream}: {Line(compare)}" : "";
    }

    /// <summary>
    /// The order failures are worked through when history is known: what broke first, then what has no
    /// history, then what was already failing, then what flips, then what is quarantined. A reader with
    /// five minutes reads the regression, not the flake.
    /// </summary>
    public static int Rank(ScenarioHistory? history) => history?.Primary switch
    {
        null => 2,
        HistoryVerdictKind.Broke => 0,
        HistoryVerdictKind.New => 1,
        HistoryVerdictKind.Unknown => 2,
        HistoryVerdictKind.Failing => 3,
        HistoryVerdictKind.AlwaysFailing => 4,
        HistoryVerdictKind.Flaky => 5,
        _ => history.Has(HistoryVerdictKind.Quarantined) ? 6 : 2
    };
}
