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
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error, Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
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

        var command = args[0];
        var options = QueryOptions.Parse(args.Skip(1).ToList(), error);
        if (options is null)
            return 2;

        if (options.File is null)
        {
            error.WriteLine("No report given. Pass a TestRunReport.json, or a directory holding one.");
            return 2;
        }

        var resolved = ResolveReport(options.File, error);
        if (resolved is null)
            return 2;

        ReportIndex index;
        try
        {
            index = ReportScanner.Scan(resolved);
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

        if (index.MergeableFormatVersion is { } version and not 1)
        {
            error.WriteLine($"{resolved} declares mergeableFormatVersion {version}, which this tool does not understand. Upgrade Kronikol.Tool.");
            return 1;
        }

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

        // http, body, note and diagram write the payload to --out themselves, and a second writer aiming
        // at the same file would overwrite it with one line. Everywhere else the answer IS what --out saves.
        var outPath = command is "http" or "body" or "note" or "diagram" ? null : options.Out;
        var envelope = options.Json ? new QueryEnvelope(command, index.Path, index.KronikolVersion, [.. options.Positional]) : null;
        var writer = new QueryWriter(@out, options.MaxBytes, envelope, outPath);
        WriteProvenance(writer, index, command);

        var exit = command switch
        {
            "summary" => Summary(index, options, writer),
            "scenarios" => Scenarios(index, options, writer),
            "failures" => Failures(index, options, writer),
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
            _ => Unknown(command, error)
        };

        if (exit == 0 && !writer.Flush(error))
            return 1;

        return exit;
    }

    /// <summary>
    /// The verbs <c>--json</c> answers: the ones whose output is a list of like things, plus
    /// <c>summary</c> and <c>diff</c>, whose sections are named members of the envelope. Every other verb
    /// prints something shaped for a reader - a step tree, a payload, a chronology - and an object form of
    /// it would either be an array of rendered strings or a second data model to keep in step with this
    /// one. Internal so SkillDriftTests can hold the docs to the same list.
    /// </summary>
    internal static readonly string[] JsonCommands =
        ["summary", "scenarios", "failures", "services", "interactions", "assertions", "diff"];

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
    private static void WriteProvenance(QueryWriter writer, ReportIndex index, string command)
    {
        if (command is "diff")
            return;

        if (!index.Enriched)
            writer.Note("! report predates step attribution and assertion detail — stepPath, assertion messages and source locations are absent. Re-run the suite on a current Kronikol to get them.");

        // A scenario that never reported a verdict took the configured default. Everything below reads as
        // if it were a real result, so the reader is told before the answer, not after it.
        foreach (var defaulted in index.Diagnostics.Where(d => d.Kind == nameof(DiagnosticKind.ResultDefaulted)))
            writer.Note("! " + defaulted.Message);

        // The flag means "the superset format a runner writes for kronikol merge", not "the result of a
        // merge": every shard of a sharded build produces one.
        if (index.Mergeable)
            writer.Note("! mergeable-format report");
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
        var found = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).EndsWith("TestRunReport.json", StringComparison.OrdinalIgnoreCase)
                        && !IsUnderBaselineFolder(path, f))
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
    internal const string BaselineFolderName = "baseline";

    private static bool IsUnderBaselineFolder(string root, string file) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, BaselineFolderName, StringComparison.OrdinalIgnoreCase));

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
        error.WriteLine("Point $KRONIKOL_BASELINE at a report (or a directory holding one), or name the older report: kronikol query diff <old.json> <new.json>");
        return null;
    }

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("kronikol query <command> <report> [args]   Debug a test run without reading the report.");
        writer.WriteLine();
        writer.WriteLine("  <report> is a TestRunReport.json, or a directory holding one.");
        writer.WriteLine();
        writer.WriteLine("Overview");
        writer.WriteLine("  summary      <report>                        run header, per-feature results, slowest scenarios, diagnostics");
        writer.WriteLine("  scenarios    <report> [--result Failed] [--feature X] [--label L] [--grep T] [--slower-than 5]");
        writer.WriteLine("  services     <report> [s3] [--sort duration]  per service: calls, status mix, errors, bytes, timings");
        writer.WriteLine();
        writer.WriteLine("Narrative");
        writer.WriteLine("  failures     <report>                        why each failing test failed, in context");
        writer.WriteLine("  steps        <report> s3                     the step and assertion tree, with interaction ranges");
        writer.WriteLine("  assertions   <report> [s3] [--failed]        flat assertion list with results and source locations");
        writer.WriteLine("  flow         <report> s3 [--step 2] [--service X] [--errors-only]");
        writer.WriteLine("  annotations  <report> s3                     example-row markers and injected diagram fragments");
        writer.WriteLine();
        writer.WriteLine("Aggregation (reads bodies freely, prints values one-lined, never whole payloads)");
        writer.WriteLine("  values       <report> [s3] --path '$.status' [--service X] [--status 5xx] [--method M] [--step 2]");
        writer.WriteLine("               [--grep URI] [--where E] [--stats] [--request|--both]   distinct values × counts, with addresses");
        writer.WriteLine();
        writer.WriteLine("Payloads (never printed unless asked for)");
        writer.WriteLine("  interactions <report> [s3] [--service X] [--status 5xx] [--method GET] [--grep T] [--group]");
        writer.WriteLine("               [--where \"$.success = false\"]   repeatable; AND; req: prefix targets the request body");
        writer.WriteLine("               [--group-by service,status]      buckets with calls/errors/median/max/bodies; dims:");
        writer.WriteLine("                                                service method status path step phase category kind capturedBy");
        writer.WriteLine("  http         <report> s3/i47 [--headers] [--body] [--keys] [--path $.a.b] [--lines 20-60] [--out F]");
        writer.WriteLine("  body         <report> b:4bdea521 [--keys] [--path $.a.b] [--lines 20-60] [--out F]");
        writer.WriteLine("               --path grammar: $.a.b[2] · [*] every element · ['a.b'] dotted key · .length() count — quote the path");
        writer.WriteLine("  note         <report> s3/d0 [/n12] [--out F]  what the HTML rendered, when it differs from the capture");
        writer.WriteLine("  diagram      <report> s3/d0 --out F          the raw PlantUML; never printed to stdout");
        writer.WriteLine();
        writer.WriteLine("Search and comparison");
        writer.WriteLine("  grep         <report> \"4173\" [--in bodies,headers,uris,steps,assertions,notes] [--values]");
        writer.WriteLine("               [--number [--tolerance 0.5|1%]]   numeric match across formatting — 4,173.00 ≈ 4173 ≈ 4.173,00");
        writer.WriteLine("  trace        <report> <id | prefix≥8hex | s3/i47>   follow a W3C trace id across the run, chronologically");
        writer.WriteLine("  compare      <report> s3 s7                  two scenarios in one run");
        writer.WriteLine("  diff         <report> s3/i47 s7/i47          two bodies in one report — only the differing paths (also b:hashes)");
        writer.WriteLine("  diff         <old.json> <new.json> [--body s3/i47]   two runs matched on stableId; --body diffs one call across them");
        writer.WriteLine("  diff         <report> --baseline               the same, against last-green: <reports>/baseline/TestRunReport.json,");
        writer.WriteLine("                                                 else $KRONIKOL_BASELINE (a report, or a directory holding one)");
        writer.WriteLine();
        writer.WriteLine("Everywhere");
        writer.WriteLine("  --max-bytes N   output budget, default 6000 (0 removes it)");
        writer.WriteLine("  --offset N      resume a truncated listing         --limit N   cap rows");
        writer.WriteLine("  --count         print only how many matched        --out FILE  write the answer to a file instead");
        writer.WriteLine("                  (--out lifts the byte budget: a file is not a context window)");
        writer.WriteLine("  --json          one envelope { formatVersion, command, report, kronikolVersion, notes, items, total,");
        writer.WriteLine("                  truncated, next } instead of text, on: " + string.Join(", ", JsonCommands));
        writer.WriteLine("                  Text is the default and is what to read in a terminal - JSON costs about twice the");
        writer.WriteLine("                  tokens. Errors stay plain text on stderr in both formats.");
    }
}
