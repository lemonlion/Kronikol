using Kronikol.Reports;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol query</c>: answers questions about a <c>TestRunReport.json</c> without anyone
/// having to read it.
///
/// <para>A real report reaches 10 MB — around 2.7 million tokens, with single embedded diagrams past
/// 160,000 — so reading it is not a slow way to debug a test run, it is an impossible one. Every command
/// here prints an answer plus the addresses that fetch the next thing, and the payloads that make up the
/// bulk of the file are fetched only when they are named.</para>
/// </summary>
internal static partial class QueryCommand
{
    /// <summary>
    /// Dispatches one <c>kronikol query</c> invocation and returns its exit code.
    ///
    /// <para><paramref name="getEnv"/> is how to read an environment variable, injected rather than read
    /// directly so a test never has to mutate process environment - the query tests run in the same
    /// parallel xunit process as the rest of the suite. The repo convention, cf.
    /// <c>CiMetadataDetector.Detect</c>.</para>
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error, Func<string, string?>? getEnv = null, string? workingDirectory = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        // Injected for the same reason as getEnv: the history verb walks up from the working directory
        // when nothing else names a ledger, and a test that runs inside this repository would otherwise
        // find this repository's.
        _workingDirectory = workingDirectory;
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

        // Needs no report and is answered before one is looked for: it is the document a wrapper reads to
        // learn what it may ask, so it has to work in an empty directory.
        if (args.Any(a => a is "--describe"))
        {
            @out.Write(Describe());
            return 0;
        }

        // Under --json every failure has to reach stdout as an envelope too, and the failures are raised
        // by forty-odd call sites that each write a sentence to `error` and return a code. Rather than
        // rewrite all of them, the error writer is teed: stderr still gets the prose, and whatever was
        // said is available here to put in `error.message` if the call ends non-zero. Asked for as a flag
        // rather than inferred, because `--json` is parsed by QueryOptions further down and a wrapper
        // needs the envelope even when the failure is the parse itself.
        var wantsJson = args.Any(a => a is "--json");
        var captured = wantsJson ? new StringWriter() : null;
        var errorSink = captured is null ? error : new TeeTextWriter(error, captured);
        var exitCode = RunCore(args, @out, errorSink, getEnv);
        if (exitCode != 0 && captured is not null)
        {
            @out.Write(QueryErrorEnvelope.Build(
                args[0], _lastResolvedReport, _lastKronikolVersion, exitCode, captured.ToString()));
        }

        return exitCode;
    }

    /// <summary>What the failure envelope reports as the file it was reading, when it got that far.</summary>
    [ThreadStatic] private static string? _lastResolvedReport;

    [ThreadStatic] private static string? _lastKronikolVersion;

    /// <summary>Where the invocation runs from, for the verbs that look around it; null means the process's own.</summary>
    [ThreadStatic] private static string? _workingDirectory;

    /// <summary>Called with the report's path once it has been scanned, so a test can stand in for the run that replaces it then.</summary>
    [ThreadStatic] internal static Action<string>? AfterScan;

    private static int RunCore(IReadOnlyList<string> args, TextWriter @out, TextWriter error, Func<string, string?> getEnv)
    {
        _lastResolvedReport = null;
        _lastKronikolVersion = null;

        var command = args[0];

        // The table is what exists; the switch below is only how a verb is reached. A name absent from
        // the table is refused here, before a report is opened, so a switch arm without a row is
        // unreachable and --describe cannot promise a verb the tool will not dispatch.
        if (VerbTable.Find(command) is null)
            return Unknown(command, error);

        var options = QueryOptions.Parse(args.Skip(1).ToList(), error);
        if (options is null)
            return 2;

        if (options.File is null)
        {
            // One verb answers without a report: the ledger holds the run a re-run overwrote (#81).
            if (command is not "history")
            {
                error.WriteLine("No report given. Pass a TestRunReport.json, or a directory holding one.");
                return 2;
            }

            if (RefuseWhatNeedsNoReport(command, options, error) is { } refusedWithoutReport)
                return refusedWithoutReport;

            var ledgerWriter = new QueryWriter(@out, options.MaxBytes, options.Json ? new QueryEnvelope(command, null, null, []) : null, options.Out);
            var ledgerExit = HistoryFromLedger(options, ledgerWriter, error, getEnv);
            return ledgerExit == 0 && !ledgerWriter.Flush(error) ? 1 : ledgerExit;
        }

        // --run: the newest run is what the path names; an earlier one is kept beneath it, and is read in
        // its place - its fragment, attachments and HTML are beside it, so every verb follows. Only history
        // has somewhere else to look: the ledger outlives the report.
        var target = options.File;
        if (options.Run is { } wantedRun)
        {
            switch (RetainedRunResolver.Resolve(options.File, wantedRun, error, out var retainedReport, out var resolvedName))
            {
                case RetainedRunOutcome.Found:
                    target = retainedReport!;
                    options.RunIsResolved = true;
                    options.RunResolvedTo(resolvedName!);
                    break;
                case RetainedRunOutcome.NotRetained when command is "history":
                    break;
                case RetainedRunOutcome.NotRetained:
                    RetainedRunResolver.RefuseNotRetained(options.File, wantedRun, error);
                    return 2;
                default:
                    return 2;
            }
        }

        var resolved = ResolveReport(target, error);
        if (resolved is null)
            return 2;

        _lastResolvedReport = resolved;

        ReportIndex index;
        try
        {
            index = ReportScanner.Scan(resolved);
        }
        catch (ReportChangedException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not read {resolved}: {exception.Message}");
            return 1;
        }
        catch (System.Text.Json.JsonException exception)
        {
            error.WriteLine($"{resolved} is not valid JSON: {exception.Message}");
            return 1;
        }

        _lastKronikolVersion = index.KronikolVersion;
        AfterScan?.Invoke(resolved);

        if (ReportGate.Refuse(index, resolved, error) is { } notAReport)
            return notAReport;

        if (RefuseWhatNeedsNoReport(command, options, error) is { } refused)
            return refused;

        // http, body, note and diagram write the payload to --out themselves, and a second writer aiming
        // at the same file would overwrite it with one line. Everywhere else the answer IS what --out saves.
        var outPath = command is "http" or "body" or "note" or "diagram" ? null : options.Out;
        var envelope = options.Json ? new QueryEnvelope(command, index.Path, index.KronikolVersion, [.. options.Positional]) : null;
        var writer = new QueryWriter(@out, options.MaxBytes, envelope, outPath);
        WriteProvenance(writer, index, command, options, error);

        int exit;
        try
        {
            exit = Dispatch(command, index, options, writer, error, getEnv);
        }
        catch (IOException exception)
        {
            // Payloads are read after the scan, through a fresh open of the same path. A report that was
            // replaced in between says so (ReportChangedException); anything else the file system refused
            // is the same kind of failure, and used to leave the tool as an unhandled exception.
            error.WriteLine(exception is ReportChangedException ? exception.Message : $"Could not read {resolved}: {exception.Message}");
            return 1;
        }

        if (exit == 0 && !writer.Flush(error))
            return 1;

        return exit;
    }

    /// <summary>The three refusals that need no report: a sort the verb cannot do, a <c>--json</c> it cannot give, a flag it does not read.</summary>
    private static int? RefuseWhatNeedsNoReport(string command, QueryOptions options, TextWriter error)
    {
        // Only `services` and `interactions --group-by` order their rows; everywhere else the order is the
        // report's and cannot be changed. Accepting --sort and discarding it is the same silence the
        // per-verb validators were added to remove - an agent reads row one as the slowest or the worst
        // when it is merely the first, and nothing said otherwise. `interactions` answers for itself,
        // because there the flag is legal with --group-by and refused without it.
        if (options.Sort is not null && command is not ("services" or "interactions"))
        {
            error.WriteLine($"{command} lists rows in the report's own order and cannot sort them.");
            error.WriteLine("Only `services --sort calls|duration|bytes|errors` and `interactions --group-by … --sort calls|duration|errors` order their rows.");
            return 2;
        }

        if (options.Json && !JsonCommands.Contains(command, StringComparer.Ordinal))
        {
            error.WriteLine($"--json is not available on '{command}'. It answers: " + string.Join(", ", JsonCommands) + ".");
            error.WriteLine("The rest print prose - a step tree, a payload, a trace - that has no honest object form.");
            return 2;
        }

        return RefuseFlagsTheVerbCannotRead(command, options, error);
    }

    private static int Dispatch(string command, ReportIndex index, QueryOptions options, QueryWriter writer, TextWriter error, Func<string, string?> getEnv) =>
        command switch
        {
            "summary" => Summary(index, options, writer, error),
            "scenarios" => Scenarios(index, options, writer, error),
            "failures" => Failures(index, options, writer, error, getEnv),
            "repro" => Repro(index, options, writer, error),
            "steps" => Steps(index, options, writer, error),
            "assertions" => Assertions(index, options, writer, error),
            "services" => Services(index, options, writer, error),
            "flow" => Flow(index, options, writer, error),
            "interactions" => Interactions(index, options, writer, error),
            "values" => Values(index, options, writer, error),
            "annotations" => Annotations(index, options, writer, error),
            "http" => Http(index, options, writer, error),
            "body" => Body(index, options, writer, error),
            "note" => Note(index, options, writer, error),
            "diagram" => Diagram(index, options, writer, error),
            "grep" => Grep(index, options, writer, error),
            "trace" => Trace(index, options, writer, error),
            "compare" => Compare(index, options, writer, error),
            "diff" => Diff(index, options, writer, error, getEnv),
            "history" => History(index, options, writer, error, getEnv),
            _ => Unknown(command, error)
        };

    /// <summary>
    /// The verbs <c>--json</c> answers: the ones whose output is a list of like things, plus
    /// <c>summary</c> and <c>diff</c>, whose sections are named members of the envelope. Every other verb
    /// prints something shaped for a reader - a step tree, a payload, a chronology - and an object form of
    /// it would either be an array of rendered strings or a second data model to keep in step with this
    /// one. Internal so SkillDriftTests can hold the docs to the same list.
    /// </summary>
    internal static readonly string[] JsonCommands = VerbTable.JsonVerbs;

    /// <summary>Every verb <see cref="RunCore"/> dispatches, in the order the help prints them - read from <see cref="VerbTable"/>, which <c>--describe</c> prints.</summary>
    internal static readonly string[] Verbs = VerbTable.Names;

    /// <summary>
    /// Flags that mean the same thing on every verb because no verb implements them: the byte budget and
    /// the escape to a file are applied by <see cref="QueryWriter"/> around whatever the verb produced.
    /// </summary>
    internal static readonly string[] UniversalFlags = VerbTable.UniversalFlags;

    /// <summary>
    /// What each verb actually reads, beside <see cref="UniversalFlags"/>. Not documentation — the list is
    /// enforced, so a flag missing from it is refused rather than silently discarded.
    ///
    /// <para>The distinction the table draws is between the flags that filter <em>scenarios</em>
    /// (<c>--result</c>, <c>--feature</c>, <c>--label</c>, <c>--slower-than</c>) and the flags that filter
    /// <em>calls</em> (<c>--service</c>, <c>--status</c>, <c>--method</c>, <c>--step</c>). They read alike
    /// and they are not interchangeable: <c>--grep</c> matches a scenario name on <c>scenarios</c> and a
    /// URI on <c>interactions</c>, and neither verb has any use for the other's set.</para>
    /// </summary>
    internal static readonly Dictionary<string, string[]> FlagsByVerb = VerbTable.FlagsByVerb();

    /// <summary>
    /// Refuses a flag the verb does not read, and says which verbs do. <c>--sort</c> and <c>--json</c> are
    /// left to the two validators below, which know enough about the verb to say something more useful
    /// than a list.
    /// </summary>
    private static int? RefuseFlagsTheVerbCannotRead(string command, QueryOptions options, TextWriter error)
    {
        if (!FlagsByVerb.TryGetValue(command, out var legal))
            return null;

        foreach (var flag in options.Given)
        {
            if (flag is "--sort" or "--json"
                || legal.Contains(flag, StringComparer.Ordinal)
                || UniversalFlags.Contains(flag, StringComparer.Ordinal))
                continue;

            error.WriteLine($"{command} does not read {flag}, so it is refused rather than ignored: an ignored flag makes an answer look filtered, sorted or counted when it is not.");

            var elsewhere = Verbs.Where(v => FlagsByVerb[v].Contains(flag, StringComparer.Ordinal)).ToArray();
            if (elsewhere.Length > 0)
                error.WriteLine($"{flag} is read by: {string.Join(", ", elsewhere)}.");

            var accepted = legal.Concat(UniversalFlags).ToArray();
            error.WriteLine(accepted.Length > 0
                ? $"{command} reads: {string.Join(", ", accepted)}."
                : $"{command} reads no flags beyond {string.Join(", ", UniversalFlags)}.");
            return 2;
        }

        return null;
    }

    private static int Unknown(string command, TextWriter error)
    {
        error.WriteLine($"Unknown query command: {command}");
        error.WriteLine("Run 'kronikol query --help' for the list.");
        return 2;
    }

    /// <summary>
    /// One header line, and only when it changes how the answer should be read: an old report whose
    /// assertion detail and step attribution are absent, or a mergeable-format file. Silence means the
    /// answer came from the full data.
    /// </summary>
    /// <remarks>
    /// <c>--count</c> is one token by contract, and the callers that read it back parse the whole of
    /// stdout. A note printed above the number made the answer unparseable on exactly the runs the note
    /// exists to warn about, so on that one path the notes go to stderr instead - still said, just not
    /// into the answer. The JSON envelope has a <c>notes</c> member and needs no such thing.
    /// </remarks>
    private static void WriteProvenance(QueryWriter writer, ReportIndex index, string command, QueryOptions options, TextWriter error)
    {
        // `diff` holds two reports and an unlabelled note would not say which one it is about, so it
        // writes its own header - named by side - once both are resolved. See Diff in
        // QueryCommand.Search.cs.
        if (command is "diff")
            return;

        Action<string> note = options.Count && !options.Json
            ? error.WriteLine
            : writer.Note;

        foreach (var line in ProvenanceNotes(index))
            note("! " + line);
    }

    /// <summary>
    /// The diagnostics that change how an answer must be read, as opposed to the ones that describe the
    /// suite's prose.
    /// </summary>
    /// <remarks>
    /// Every kind here means the report holds less than the run produced, and none of them is visible in
    /// the answer itself: a degraded capture makes <c>services</c> - the one view whose whole point is to
    /// answer a negative question - report a service as never called when it was called and the record
    /// was lost; a mismatched step marker makes <c>flow</c> attribution partial; a defaulted verdict makes
    /// a dead worker read as a pass. <see cref="DiagnosticKind.StepsNotStartingWithCapital"/> and
    /// <see cref="DiagnosticKind.TitlesNotStartingWithCapital"/> are deliberately absent: they are about
    /// how the suite writes English, they change no answer, and they are the two kinds that fire in bulk
    /// on a healthy run. Both still appear in <c>summary</c>'s Diagnostics section, which is the inventory.
    /// </remarks>
    private static readonly HashSet<string> AnswerAffectingDiagnostics = new(StringComparer.Ordinal)
    {
        nameof(DiagnosticKind.ResultDefaulted),
        nameof(DiagnosticKind.CaptureDegraded),
        nameof(DiagnosticKind.StepAttributionMismatch),
        nameof(DiagnosticKind.UnattributedInteractions),
        nameof(DiagnosticKind.DroppedUnattributed),
        nameof(DiagnosticKind.DroppedOutsideRunWindow),
        nameof(DiagnosticKind.MalformedLine),
        nameof(DiagnosticKind.AttachmentFailure),
        nameof(DiagnosticKind.RenderFailure),
        nameof(DiagnosticKind.OutputFailure),
        nameof(DiagnosticKind.Other),
    };

    /// <summary>
    /// What a reader has to know before the answer, one line each and without the leading <c>!</c> so the
    /// caller can say which report the line belongs to. Empty means the answer came from the full data.
    /// </summary>
    /// <remarks>
    /// Grouped by kind and capped, because these lines are the tool's voice wrapped around a message the
    /// report supplied: a malformed-line diagnostic can be recorded thousands of times, and a header that
    /// pushes the answer off the budget is a worse failure than the one it warns about.
    /// </remarks>
    internal static IEnumerable<string> ProvenanceNotes(ReportIndex index)
    {
        if (!index.Enriched)
            yield return "report predates step attribution and assertion detail — stepPath, assertion messages and source locations are absent. Re-run the suite on a current Kronikol to get them.";

        foreach (var group in index.Diagnostics
                     .Where(d => AnswerAffectingDiagnostics.Contains(d.Kind))
                     .GroupBy(d => d.Kind, StringComparer.Ordinal))
        {
            var message = QueryWriter.OneLine(group.First().Message, 160);
            yield return group.Count() == 1 ? message : $"{message} (×{group.Count()})";
        }

        // The flag means "the superset format a runner writes for kronikol merge", not "the result of a
        // merge": every shard of a sharded build produces one.
        if (index.Mergeable)
            yield return "mergeable-format report";
    }

    /// <summary>
    /// Accepts a file or the directory holding one, because a solution with several test projects has
    /// several reports and an agent should not have to guess which. Ambiguity is reported, never resolved
    /// by picking one. Internal because <c>kronikol ctrf</c> takes a report the same way and must find it
    /// by the same rule.
    /// </summary>
    internal static string? ResolveReport(string path, TextWriter error)
    {
        if (File.Exists(path))
            return path;

        if (!Directory.Exists(path))
        {
            error.WriteLine($"No such file or directory: {path}");
            return null;
        }

        var direct = Path.Combine(path, "TestRunReport.json");
        if (File.Exists(direct))
            return direct;

        // A `baseline/` folder beside the report is the --baseline convention, not a second report:
        // without this, adopting the convention turns every directory lookup into an ambiguity.
        // Nor are the runs kept under `runs/`: they are earlier runs of the report above them, opened
        // with --run, and counted here they would make every lookup from a parent directory ambiguous.
        var found = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).EndsWith("TestRunReport.json", StringComparison.OrdinalIgnoreCase)
                        && !ReportFolders.IsReserved(path, f))
            .Take(20)
            .ToArray();

        switch (found.Length)
        {
            case 0:
                error.WriteLine($"No TestRunReport.json under {path}.");
                return null;
            case 1:
                return found[0];
            default:
                error.WriteLine($"Several reports under {path} — name the one you mean:");
                foreach (var file in found)
                    error.WriteLine("  " + file);
                return null;
        }
    }

    /// <summary>The conventional folder name a <c>--baseline</c> report lives in, beside the current one.</summary>
    internal const string BaselineFolderName = ReportFolders.BaselineFolderName;

    /// <summary>
    /// Finds the report a <c>--baseline</c> diff compares against: the conventional
    /// <c>&lt;reports&gt;/baseline/</c> folder beside the current report first, then
    /// <c>$KRONIKOL_BASELINE</c> (a file or a directory, resolved the same way any report path is).
    /// Convention wins, so a stale exported variable cannot quietly override what is on disk.
    /// </summary>
    private static string? ResolveBaseline(ReportIndex index, Func<string, string?> getEnv, TextWriter error)
    {
        var folder = Path.Combine(index.Directory, BaselineFolderName);
        var conventional = Path.Combine(folder, "TestRunReport.json");
        if (File.Exists(conventional))
            return conventional;

        if (Directory.Exists(folder))
        {
            var single = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (single.Length == 1)
                return single[0];
        }

        if (getEnv("KRONIKOL_BASELINE") is { Length: > 0 } named)
        {
            // Silent here: ResolveReport has already said precisely what was wrong with the path.
            var resolved = ResolveReport(named, error);
            if (resolved is not null)
                return resolved;
            return null;
        }

        error.WriteLine($"No baseline to compare against. Looked for {conventional}, and $KRONIKOL_BASELINE is not set.");
        error.WriteLine("Point $KRONIKOL_BASELINE at a report (or a directory holding one), name the older report (kronikol query diff <old.json> <new.json>), or a run kept under runs/ (kronikol query diff <report> --baseline-run last-failed).");
        return null;
    }

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("kronikol query <command> <report> [args]   Debug a test run without reading the report.");
        writer.WriteLine();
        writer.WriteLine("  <report> is a TestRunReport.json, or a directory holding one.");

        // Rendered from the table --describe prints, so the help cannot list a verb the table lacks or
        // omit one it has. The lines themselves are the table's, verbatim.
        foreach (var (group, caption) in VerbTable.Groups)
        {
            writer.WriteLine();
            writer.WriteLine(caption is null ? group : $"{group} ({caption})");
            foreach (var verb in VerbTable.Verbs.Where(v => string.Equals(v.Group, group, StringComparison.Ordinal)))
                foreach (var line in verb.Usage)
                    writer.WriteLine(line);
        }

        writer.WriteLine();
        writer.WriteLine("Everywhere");
        writer.WriteLine("  --max-bytes N   output budget, default 6000 (0 removes it)");
        writer.WriteLine("  --out FILE      write the answer to a file instead");
        writer.WriteLine("                  (--out lifts the byte budget: a file is not a context window)");
        writer.WriteLine("  --run ID        read a run kept under <reports>/runs/ instead of the newest: an id, a unique part of one,");
        writer.WriteLine("                  its folder name, last-failed or previous (history reads a run that is not kept from the ledger)");
        writer.WriteLine("  --describe      the verbs, their flags, the address forms and the exit codes as one JSON");
        writer.WriteLine("                  document, for tooling; needs no report");
        writer.WriteLine();
        writer.WriteLine("On the verbs that list rows");
        writer.WriteLine("  --offset N      resume a truncated listing         --limit N   cap rows");
        writer.WriteLine("  --count         print only how many matched");
        writer.WriteLine("  --json          one envelope { formatVersion, command, report, kronikolVersion, notes, items, total,");
        writer.WriteLine("                  truncated, next } instead of text, on: " + string.Join(", ", JsonCommands));
        writer.WriteLine("                  Text is the default and is what to read in a terminal - JSON costs about twice the");
        writer.WriteLine("                  tokens. Errors stay plain text on stderr in both formats.");
        writer.WriteLine();
        writer.WriteLine("  Flags are per-verb. A flag a verb does not read is refused and named, never accepted and");
        writer.WriteLine("  ignored - `failures --service X` used to print every failure under a filter that never ran.");
    }
}
