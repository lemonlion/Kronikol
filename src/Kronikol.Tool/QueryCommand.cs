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
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error)
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

        var writer = new QueryWriter(@out, options.MaxBytes);
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
            "annotations" => Annotations(index, options, writer, error),
            "http" => Http(index, options, writer, error),
            "body" => Body(index, options, writer, error),
            "note" => Note(index, options, writer, error),
            "diagram" => Diagram(index, options, writer, error),
            "grep" => Grep(index, options, writer, error),
            "compare" => Compare(index, options, writer, error),
            "diff" => Diff(index, options, writer, error),
            _ => Unknown(command, error)
        };

        if (exit == 0)
            writer.Flush();

        return exit;
    }

    private static int Unknown(string command, TextWriter error)
    {
        error.WriteLine($"Unknown query command: {command}");
        error.WriteLine("Run 'kronikol query --help' for the list.");
        return 2;
    }

    /// <summary>
    /// One header line, and only when it changes how the answer should be read: an old report whose
    /// assertion detail and step attribution are absent, or a merged file. Silence means the answer came
    /// from the full data.
    /// </summary>
    private static void WriteProvenance(QueryWriter writer, ReportIndex index, string command)
    {
        if (command is "diff")
            return;

        if (!index.Enriched)
            writer.Line("! report predates step attribution and assertion detail — stepPath, assertion messages and source locations are absent. Re-run the suite on a current Kronikol to get them.");

        if (index.Mergeable)
            writer.Line("! mergeable report (a merge of several runs)");
    }

    /// <summary>
    /// Accepts a file or the directory holding one, because a solution with several test projects has
    /// several reports and an agent should not have to guess which. Ambiguity is reported, never resolved
    /// by picking one.
    /// </summary>
    private static string? ResolveReport(string path, TextWriter error)
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

        var found = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).EndsWith("TestRunReport.json", StringComparison.OrdinalIgnoreCase))
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
        writer.WriteLine("Payloads (never printed unless asked for)");
        writer.WriteLine("  interactions <report> s3 [--service X] [--status 5xx] [--method GET] [--grep T] [--group]");
        writer.WriteLine("  http         <report> s3/i47 [--headers] [--body] [--keys] [--path $.a.b] [--lines 20-60] [--out F]");
        writer.WriteLine("  body         <report> b:4bdea521 [--keys] [--path $.a.b] [--lines 20-60] [--out F]");
        writer.WriteLine("               --path grammar: $.a.b[2] · [*] every element · ['a.b'] dotted key · .length() count — quote the path");
        writer.WriteLine("  note         <report> s3/d0 [n12] [--out F]  what the HTML rendered, when it differs from the capture");
        writer.WriteLine("  diagram      <report> s3/d0 --out F          the raw PlantUML; never printed to stdout");
        writer.WriteLine();
        writer.WriteLine("Search and comparison");
        writer.WriteLine("  grep         <report> \"4173\" [--in bodies,headers,uris,steps,assertions,notes] [--values]");
        writer.WriteLine("  compare      <report> s3 s7                  two scenarios in one run");
        writer.WriteLine("  diff         <old.json> <new.json>           two runs, matched on stableId");
        writer.WriteLine();
        writer.WriteLine("Everywhere");
        writer.WriteLine("  --max-bytes N   output budget, default 6000 (0 removes it)");
        writer.WriteLine("  --offset N      resume a truncated listing         --limit N   cap rows");
        writer.WriteLine("  --count         print only how many matched        --out FILE  write the payload to a file instead");
    }
}
