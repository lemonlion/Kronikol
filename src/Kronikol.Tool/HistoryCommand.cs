using System.Globalization;
using Kronikol.History;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol history</c>: the maintenance verbs over the cross-run ledger
/// (plans/CROSS_RUN_HISTORY_PLAN.md §6, §7) — folding a CI run's fragments into it, creating it, showing
/// what it holds, checking it, and the two explicit rewrites. Reading it against a report is
/// <c>kronikol query history</c>, because that is a question about a run and this is bookkeeping.
///
/// <para><b>Where the ledger is.</b> <c>--history FILE</c> names it; else <c>$KRONIKOL_HISTORY</c>
/// (<c>off</c> is refused here rather than silently obeyed, because somebody typed a ledger command);
/// else the nearest <c>.kronikol</c> or <c>.git</c> above the working directory and above the first
/// input. Nothing found is exit 2 naming every place looked, never a ledger invented somewhere.</para>
///
/// <para>Exit codes follow the tool's convention: 0 done, 1 something could not be read or written, 2
/// usage.</para>
/// </summary>
internal static partial class HistoryCommand
{
    private const int DefaultWindow = 50;

    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error) =>
        Run(args, @out, error, Environment.GetEnvironmentVariable);

    /// <summary><paramref name="getEnv"/> is injected so a test never mutates the process environment.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error, Func<string, string?> getEnv)
    {
        if (args.Count == 0)
        {
            PrintUsage(error);
            return 2;
        }

        if (args[0] is "-h" or "--help")
        {
            PrintUsage(@out);
            return 0;
        }

        var verb = args[0];
        var parsed = new Args();

        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            string? Next()
            {
                if (++i < args.Count) return args[i];
                error.WriteLine("Missing value for " + arg);
                return null;
            }

            switch (arg)
            {
                case "-h" or "--help":
                    PrintUsage(@out);
                    return 0;
                case "--history": if (Next() is not { } ledger) return 2; parsed.Ledger = ledger; break;
                case "--suite": if (Next() is not { } suite) return 2; parsed.Suite = suite; break;
                case "--reason": if (Next() is not { } reason) return 2; parsed.Reason = reason; break;
                case "--by": if (Next() is not { } by) return 2; parsed.By = by; break;
                case "--fail-on": if (Next() is not { } failOn) return 2; parsed.FailOn = failOn; break;
                case "--branch": if (Next() is not { } branch) return 2; parsed.Branch = branch; break;
                case "--run-id": if (Next() is not { } runId) return 2; parsed.RunId = runId; break;
                case "--release": parsed.Release = true; break;
                case "--list": parsed.List = true; break;
                case "--from-ctrf": parsed.FromCtrf = true; break;
                case "--from-allure": parsed.FromAllure = true; break;
                case "--accept-renames": parsed.AcceptRenames = true; break;
                case "--until":
                    if (Next() is not { } until) return 2;
                    if (!DateOnly.TryParseExact(until, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var untilDate))
                    {
                        error.WriteLine("--until takes a date like 2026-12-31.");
                        return 2;
                    }
                    parsed.Until = untilDate;
                    break;
                case "--window":
                    if (Next() is not { } window) return 2;
                    if (!int.TryParse(window, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedWindow) || parsedWindow < 0)
                    {
                        error.WriteLine("--window takes a non-negative number of runs (0 keeps every run).");
                        return 2;
                    }
                    parsed.Window = parsedWindow;
                    break;
                case "--min-runs":
                    if (Next() is not { } minRuns) return 2;
                    if (!int.TryParse(minRuns, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMinRuns) || parsedMinRuns < 1)
                    {
                        error.WriteLine("--min-runs takes a positive number of runs: the bar the flaky and duration verdicts need, the report's HistoryMinRuns (default 5).");
                        return 2;
                    }
                    parsed.MinRuns = parsedMinRuns;
                    break;
                case "--slower-min-ms":
                    if (Next() is not { } floor) return 2;
                    if (!int.TryParse(floor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFloor) || parsedFloor < 0)
                    {
                        error.WriteLine("--slower-min-ms takes a number of milliseconds: the least a scenario must be over the bar before it is slower, the report's HistorySlowerMinMs (default 100).");
                        return 2;
                    }
                    parsed.SlowerMinMs = parsedFloor;
                    break;
                case "--max-new-failures":
                    if (Next() is not { } max) return 2;
                    if (!int.TryParse(max, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMax) || parsedMax < 0)
                    {
                        error.WriteLine("--max-new-failures takes a non-negative count.");
                        return 2;
                    }
                    parsed.MaxNewFailures = parsedMax;
                    break;
                case "--min-pass-rate" or "--flaky-threshold" or "--slower-by":
                    if (Next() is not { } number) return 2;
                    if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
                    {
                        error.WriteLine($"{arg} takes a number.");
                        return 2;
                    }
                    if (arg == "--min-pass-rate") parsed.MinPassRate = value;
                    else if (arg == "--flaky-threshold") parsed.FlakyThreshold = value;
                    else parsed.SlowerBy = value;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error.WriteLine($"Unknown option: {arg}");
                        return 2;
                    }
                    parsed.Inputs.Add(arg);
                    break;
            }
        }

        return verb switch
        {
            "record" => Record(parsed.Inputs, parsed.Ledger, parsed.AcceptRenames, getEnv, @out, error),
            "init" => Init(parsed.Ledger, getEnv, @out, error),
            "show" => Show(parsed.Inputs, parsed.Ledger, parsed.Suite, parsed.Window ?? DefaultWindow, getEnv, @out, error),
            "verify" => Verify(parsed.Inputs, parsed.Ledger, getEnv, @out, error),
            "prune" => Rewrite(parsed.Inputs, parsed.Ledger, parsed.Window ?? DefaultWindow, compact: false, getEnv, @out, error),
            "compact" => Rewrite(parsed.Inputs, parsed.Ledger, parsed.Window ?? DefaultWindow, compact: true, getEnv, @out, error),
            "gate" => Gate(parsed, getEnv, @out, error),
            "quarantine" => Quarantine(parsed, getEnv, @out, error),
            "rename" => Rename(parsed, getEnv, @out, error),
            "doctor" => Doctor(parsed, getEnv, @out, error),
            "import" => Import(parsed, getEnv, @out, error),
            _ => Unknown(verb, error)
        };
    }

    private static int Unknown(string verb, TextWriter error)
    {
        error.WriteLine($"Unknown history command: {verb}");
        PrintUsage(error);
        return 2;
    }

    // ─── Ledger resolution ─────────────────────────────────────

    /// <summary>
    /// The ledger for a tool verb: named, or from the environment, or found above the working directory
    /// and above <paramref name="inputDirectory"/>. Null with the reason on <paramref name="error"/>.
    /// </summary>
    internal static string? Locate(string? explicitPath, string? inputDirectory, Func<string, string?> getEnv, TextWriter error, bool mustExist)
    {
        var cwd = Directory.GetCurrentDirectory();
        var location = HistoryPathResolver.Resolve(explicitPath, cwd, getEnv, inputDirectory ?? cwd);

        switch (location.Source)
        {
            case HistoryLocationSource.Disabled:
                error.WriteLine($"{HistoryFormat.EnvironmentVariable}={HistoryFormat.EnvironmentOff}: history is switched off in this environment. Pass --history <file> to name a ledger anyway.");
                return null;
            case HistoryLocationSource.None:
                error.WriteLine($"No history ledger: nothing named by --history or ${HistoryFormat.EnvironmentVariable}, and no .kronikol or .git directory above {cwd}"
                                + (inputDirectory is null ? "." : $" or {inputDirectory}."));
                error.WriteLine("Run kronikol history init to create one, or pass --history <file>.");
                return null;
        }

        var path = location.Path!;
        if (mustExist && !File.Exists(path))
        {
            error.WriteLine($"No ledger at {path}. Run kronikol history init to create it, or record a run into it: kronikol history record <reports-dir>.");
            return null;
        }

        return path;
    }

    private static string? FirstInputDirectory(IReadOnlyList<string> inputs)
    {
        foreach (var input in inputs)
        {
            try
            {
                var full = Path.GetFullPath(input);
                if (Directory.Exists(full)) return full;
                if (File.Exists(full)) return Path.GetDirectoryName(full);
            }
            catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
            {
                // Not a path; the verb that uses it will say so.
            }
        }
        return null;
    }

    // ─── record ────────────────────────────────────────────────

    /// <summary>
    /// Folds every <c>History.run.json</c> under the inputs into the ledger: one line per run, eight shards
    /// of one run becoming one line, a run already recorded left alone. The fragments are read in path
    /// order so the fold is the same on every machine.
    /// </summary>
    private static int Record(IReadOnlyList<string> inputs, string? explicitLedger, bool acceptRenames, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        if (inputs.Count == 0)
        {
            error.WriteLine($"Nothing to record. Pass one or more reports directories (searched for {HistoryFormat.FragmentFileName}) or fragment files.");
            return 2;
        }

        var ledger = Locate(explicitLedger, FirstInputDirectory(inputs), getEnv, error, mustExist: false);
        if (ledger is null)
            return 2;

        var fragments = new List<(string Path, HistoryFragment Fragment)>();
        var unreadable = 0;
        foreach (var file in FragmentFiles(inputs, error))
        {
            try
            {
                fragments.Add((file, HistoryFragment.Parse(File.ReadAllText(file))));
            }
            catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"Skipped {file}: {exception.Message}");
                unreadable++;
            }
        }

        if (fragments.Count == 0)
        {
            error.WriteLine($"No {HistoryFormat.FragmentFileName} found under the inputs. A run writes one beside its report (GenerateHistoryFragment, on by default).");
            return unreadable > 0 ? 1 : 2;
        }

        var newer = fragments.Where(f => f.Fragment.Version > HistoryFormat.Version).ToList();
        if (newer.Count > 0)
        {
            error.WriteLine($"{newer.Count} fragment(s) declare historyFormatVersion {newer.Max(f => f.Fragment.Version)}; this build understands {HistoryFormat.Version}. Upgrade Kronikol.Tool.");
            return 1;
        }

        var folded = HistoryFold.Fold(fragments.Select(f => f.Fragment));
        var appended = 0;
        var duplicates = 0;
        var failed = 0;

        foreach (var (roster, run) in folded)
        {
            var line = run;
            var previous = LastFullRoster(ledger, run.Suite);

            // A scenario whose id changed but whose feature and name did not is a rename, not a deletion
            // plus an addition; saying so is what keeps its history attached (§7.4). Suggested by
            // default, written only when asked, because a guess written silently is the one kind of
            // alias nobody would ever review.
            if (previous is not null)
            {
                var renames = SuggestRenames(previous, roster);
                if (renames.Count > 0)
                {
                    if (acceptRenames)
                    {
                        var aliasPath = HistoryAliases.PathBeside(ledger);
                        var aliases = LoadAliasesOrNull(ledger) ?? new HistoryAliases();
                        foreach (var (oldId, newId, name) in renames)
                        {
                            aliases.Add(oldId, newId);
                            @out.WriteLine($"aliased {oldId} → {newId}  {name}");
                        }
                        aliases.Save(aliasPath);
                        previous = WithAliases(previous, aliases);
                    }
                    else
                    {
                        @out.WriteLine($"{renames.Count} scenario(s) look renamed (same feature and name, different id):");
                        foreach (var (oldId, newId, name) in renames)
                            @out.WriteLine($"  {oldId} → {newId}  {name}");
                        @out.WriteLine("Run again with --accept-renames to alias them, or kronikol history rename <old> <new> for one.");
                    }
                }
                else if (LoadAliasesOrNull(ledger) is { } known && known.Mappings.Count > 0)
                {
                    previous = WithAliases(previous, known);
                }
            }

            if (line.Partial is null)
            {
                // The heuristic runs here, against the ledger as it stands, so eight shards that each
                // lacked seven eighths of the suite do not each declare themselves partial.
                line = line with { Partial = HistoryAnalyzer.IsPartial(roster, previous, 0.10) };
            }

            var result = HistoryLedgerWriter.Append(ledger, roster, line, Commands.Version);
            var label = $"{run.Id}  {run.Suite ?? "(no suite)"}  {roster.Count} scenarios"
                        + (run.Shards > 1 ? $" from {run.Shards} shards" : "")
                        + (line.Partial == true ? "  partial" : "");
            switch (result.Outcome)
            {
                case HistoryAppendOutcome.Appended:
                    appended++;
                    @out.WriteLine($"recorded   {label}");
                    break;
                case HistoryAppendOutcome.Duplicate:
                    duplicates++;
                    @out.WriteLine($"duplicate  {label}  (already in the ledger)");
                    break;
                default:
                    failed++;
                    error.WriteLine($"not recorded  {label}: {result.Message}");
                    break;
            }
        }

        @out.WriteLine($"{appended} run(s) recorded, {duplicates} already there, {failed} failed, from {fragments.Count} fragment(s) → {ledger}");
        return failed > 0 || unreadable > 0 ? 1 : 0;
    }

    private static IEnumerable<string> FragmentFiles(IReadOnlyList<string> inputs, TextWriter error)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            string full;
            try { full = Path.GetFullPath(input); }
            catch (Exception exception) when (exception is ArgumentException or PathTooLongException or NotSupportedException)
            {
                error.WriteLine($"Not a path: {input}");
                continue;
            }

            if (File.Exists(full))
            {
                files.Add(full);
            }
            else if (Directory.Exists(full))
            {
                foreach (var file in Directory.EnumerateFiles(full, HistoryFormat.FragmentFileName, SearchOption.AllDirectories))
                    files.Add(file);
            }
            else
            {
                error.WriteLine($"Not found: {input}");
            }
        }
        return files;
    }

    /// <summary>The previous roster with every aliased id replaced by its current one, so a rename is not a missing scenario.</summary>
    private static HistoryRoster WithAliases(HistoryRoster previous, HistoryAliases aliases) =>
        HistoryRoster.Create(previous.Suite, previous.Entries().Select(e => e with { StableId = aliases.Current(e.StableId) }).ToArray());

    private static HistoryRoster? LastFullRoster(string ledger, string? suite)
    {
        var read = HistoryLedgerReader.Read(ledger, DefaultWindow);
        if (read.Ledger is not { } held)
            return null;
        var runs = held.Runs(suite);
        for (var i = runs.Count - 1; i >= 0; i--)
            if (runs[i].Partial != true && held.Roster(runs[i].RosterHash) is { } roster)
                return roster;
        return null;
    }

    // ─── init ──────────────────────────────────────────────────

    /// <summary>
    /// Creates the ledger where the resolver will find it, and adds the <c>merge=union</c> attribute that
    /// lets two branches append without a conflict. Both idempotent: run it twice and it says so.
    /// </summary>
    private static int Init(string? explicitLedger, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        var cwd = Directory.GetCurrentDirectory();
        string ledger;
        var fromEnv = getEnv(HistoryFormat.EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitLedger))
            ledger = Path.GetFullPath(explicitLedger);
        else if (!string.IsNullOrWhiteSpace(fromEnv) && !string.Equals(fromEnv.Trim(), HistoryFormat.EnvironmentOff, StringComparison.OrdinalIgnoreCase))
            ledger = Path.GetFullPath(fromEnv.Trim());
        else
        {
            var root = HistoryPathResolver.FindMarker(cwd)?.Root ?? cwd;
            ledger = Path.Combine(root, HistoryFormat.DirectoryName, HistoryFormat.FileName);
        }

        try
        {
            var directory = Path.GetDirectoryName(ledger)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(ledger))
            {
                @out.WriteLine($"exists   {ledger}");
            }
            else
            {
                File.WriteAllText(ledger, HistoryJson.HeaderLine(Commands.Version) + "\n", new System.Text.UTF8Encoding(false));
                @out.WriteLine($"created  {ledger}");
            }

            // The attribute file sits beside the .kronikol directory - the repository root in the usual
            // shape - so `history.jsonl` under it matches at any depth.
            var attributesDirectory = string.Equals(Path.GetFileName(directory), HistoryFormat.DirectoryName, StringComparison.Ordinal)
                ? Path.GetDirectoryName(directory) ?? directory
                : directory;
            var attributes = Path.Combine(attributesDirectory, ".gitattributes");
            var existing = File.Exists(attributes) ? File.ReadAllText(attributes) : "";
            if (existing.Contains("history.jsonl", StringComparison.Ordinal))
            {
                @out.WriteLine($"present  {HistoryFormat.GitAttributesLine} in {attributes}");
            }
            else
            {
                var text = existing.Length == 0 || existing.EndsWith('\n') ? existing : existing + "\n";
                text += (existing.Length == 0 ? "" : "\n") + "# The Kronikol history ledger is append-only: two branches that both appended merge without a conflict\n"
                        + HistoryFormat.GitAttributesLine + "\n";
                File.WriteAllText(attributes, text, new System.Text.UTF8Encoding(false));
                @out.WriteLine($"added    {HistoryFormat.GitAttributesLine} to {attributes}");
            }

            @out.WriteLine($"Commit both. Runs append to the ledger; on CI they write {HistoryFormat.FragmentFileName} beside the report and `kronikol history record <reports-dir>` folds those in.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not initialise the ledger at {ledger}: {exception.Message}");
            return 1;
        }
    }

    // ─── show ──────────────────────────────────────────────────

    private static int Show(IReadOnlyList<string> inputs, string? explicitLedger, string? suiteFilter, int window, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        if (inputs.Count > 0)
        {
            error.WriteLine("show takes no positional arguments; name the ledger with --history.");
            return 2;
        }

        var ledger = Locate(explicitLedger, null, getEnv, error, mustExist: true);
        if (ledger is null)
            return 2;

        var read = HistoryLedgerReader.Read(ledger, window);
        if (read.Ledger is not { } held)
        {
            error.WriteLine(read.Message);
            return 1;
        }

        @out.WriteLine($"ledger   {ledger}");
        @out.WriteLine($"format   v{held.Version?.ToString(CultureInfo.InvariantCulture) ?? "?"} · written by Kronikol {held.Generator ?? "?"} · {held.Stats.LinesScanned} lines, {held.Stats.RostersKept} rosters"
                       + (held.Stats.DamagedLines > 0 ? $" · {held.Stats.DamagedLines} damaged line(s) — run kronikol history verify" : ""));

        var suites = held.Suites
            .Where(s => suiteFilter is null || string.Equals(s, suiteFilter, StringComparison.Ordinal))
            .OrderBy(s => s ?? "", StringComparer.Ordinal)
            .ToList();
        if (suites.Count == 0)
        {
            @out.WriteLine(suiteFilter is null ? "no runs recorded yet" : $"no runs recorded for suite {suiteFilter}");
            return 0;
        }

        foreach (var suite in suites)
        {
            var runs = held.Runs(suite);
            var last = runs[^1];
            var streams = runs.GroupBy(r => r.Stream, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} ({g.Count().ToString(CultureInfo.InvariantCulture)})");
            @out.WriteLine();
            @out.WriteLine($"suite    {suite ?? "(no suite)"}");
            @out.WriteLine($"  runs   {runs.Count.ToString(CultureInfo.InvariantCulture)} in the window of {window.ToString(CultureInfo.InvariantCulture)} · streams: {string.Join(", ", streams)}");
            @out.WriteLine($"  last   {last.Id} · {last.At.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)} · {last.Stream}"
                           + (last.Commit is { } commit ? $" · {(commit.Length > 7 ? commit[..7] : commit)}" : "")
                           + $" · {Describe(last)}"
                           + (last.Partial == true ? " · partial" : ""));
        }

        return 0;
    }

    private static string Describe(HistoryRun run)
    {
        var passed = run.Results.Count(c => c == HistoryFormat.Passed);
        var failed = run.Results.Count(c => c == HistoryFormat.Failed);
        var other = run.Results.Length - passed - failed;
        return $"{run.Results.Length.ToString(CultureInfo.InvariantCulture)} scenarios: {passed.ToString(CultureInfo.InvariantCulture)} passed, {failed.ToString(CultureInfo.InvariantCulture)} failed"
               + (other > 0 ? $", {other.ToString(CultureInfo.InvariantCulture)} other" : "");
    }

    // ─── verify ────────────────────────────────────────────────

    private static int Verify(IReadOnlyList<string> inputs, string? explicitLedger, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        if (inputs.Count > 0)
        {
            error.WriteLine("verify takes no positional arguments; name the ledger with --history.");
            return 2;
        }

        var ledger = Locate(explicitLedger, null, getEnv, error, mustExist: true);
        if (ledger is null)
            return 2;

        IReadOnlyList<string> findings;
        try
        {
            findings = HistoryLedgerReader.Verify(ledger);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not read {ledger}: {exception.Message}");
            return 1;
        }

        if (findings.Count == 0)
        {
            @out.WriteLine($"verified {ledger}: no findings");
            return 0;
        }

        @out.WriteLine($"{findings.Count.ToString(CultureInfo.InvariantCulture)} finding(s) in {ledger}:");
        foreach (var finding in findings)
            @out.WriteLine("  " + finding);
        @out.WriteLine("A damaged line is skipped by every reader; `kronikol history compact` rewrites the file without it.");
        return 1;
    }

    // ─── prune / compact ───────────────────────────────────────

    private static int Rewrite(IReadOnlyList<string> inputs, string? explicitLedger, int window, bool compact, Func<string, string?> getEnv, TextWriter @out, TextWriter error)
    {
        var verb = compact ? "compact" : "prune";
        if (inputs.Count > 0)
        {
            error.WriteLine($"{verb} takes no positional arguments; name the ledger with --history.");
            return 2;
        }

        var ledger = Locate(explicitLedger, null, getEnv, error, mustExist: true);
        if (ledger is null)
            return 2;

        try
        {
            var result = compact
                ? HistoryLedgerWriter.Compact(ledger, window, Commands.Version)
                : HistoryLedgerWriter.Prune(ledger, window, Commands.Version);
            @out.WriteLine(compact
                ? $"compacted {ledger}: {result.RunsKept.ToString(CultureInfo.InvariantCulture)} run(s) kept in format v{result.Version.ToString(CultureInfo.InvariantCulture)}, error text kept for the last {window.ToString(CultureInfo.InvariantCulture)} run(s) per suite, {result.RostersDropped.ToString(CultureInfo.InvariantCulture)} unreferenced roster(s) dropped"
                : $"pruned {ledger}: {result.RunsDropped.ToString(CultureInfo.InvariantCulture)} run(s) dropped, {result.RunsKept.ToString(CultureInfo.InvariantCulture)} kept (the last {window.ToString(CultureInfo.InvariantCulture)} per suite), {result.RostersDropped.ToString(CultureInfo.InvariantCulture)} unreferenced roster(s) dropped");
            if (!compact)
                @out.WriteLine("Note: pruning makes the repository larger, not smaller — dropping the oldest line breaks git's delta chains. It is a read-cost control.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error.WriteLine($"Could not {verb} {ledger}: {exception.Message}");
            return 1;
        }
    }

    // ─── usage ─────────────────────────────────────────────────

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("kronikol history <command> [args]   Maintain the cross-run history ledger (.kronikol/history.jsonl).");
        writer.WriteLine();
        writer.WriteLine("  record   <reports-dir|History.run.json>...   fold a run's fragments into the ledger: one line per run, shards folded, duplicates skipped");
        writer.WriteLine("  init                                          create the ledger above the working directory and add the merge=union attribute");
        writer.WriteLine("  show     [--suite NAME] [--window N]          what the ledger holds: per suite, the runs, the streams, the last run");
        writer.WriteLine("  verify                                        check the file's structure; exit 1 with every finding when it is not sound");
        writer.WriteLine("  prune    [--window N]                         rewrite without the runs outside the window (default 50) - a read-cost control");
        writer.WriteLine("  compact  [--window N]                         rewrite in the current format, every run kept, error text kept for the window");
        writer.WriteLine("  gate     <report|dir> [--fail-on LIST]         exit 1 on what the ledger says is new: --fail-on new-failures,flaky,duration-regression,behaviour-change");
        writer.WriteLine("           [--max-new-failures N] [--min-pass-rate X] [--flaky-threshold X] [--slower-by X] [--slower-min-ms N] [--min-runs N] [--branch NAME]   (default: new-failures; --branch: a pull request's target)");
        writer.WriteLine("  quarantine <sid> --reason TEXT [--by NAME] [--until DATE] | <sid> --release | --list   .kronikol/quarantine.json beside the ledger");
        writer.WriteLine("  rename   <old-sid> <new-sid>                  alias an old stableId to its replacement (.kronikol/aliases.json); record suggests them");
        writer.WriteLine("  doctor                                        the ledger, its companions, the merge attribute and what is expired or damaged");
        writer.WriteLine("  import   <report|dir>... [--from-ctrf|--from-allure] [--suite NAME] [--branch NAME] [--run-id ID]   runs from reports the run did not write");
        writer.WriteLine();
        writer.WriteLine("  --history FILE   the ledger, instead of $KRONIKOL_HISTORY or the .kronikol/history.jsonl above the working directory");
        writer.WriteLine("  record --accept-renames   write the rename aliases record suggests");
        writer.WriteLine();
        writer.WriteLine("  Reading the ledger against a report is `kronikol query history <report>`.");
        writer.WriteLine("  Exit codes: 0 done, 1 could not read or write, 2 usage.");
    }
}
