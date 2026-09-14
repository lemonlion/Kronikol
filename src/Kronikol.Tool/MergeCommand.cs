using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol merge</c>: combine several mergeable <c>TestRunReport.json</c> files into a
/// single combined <c>TestRunReport.html</c> and, beside it, everything a run writes beside its report -
/// the merged data file, so a sharded run is something <c>kronikol query</c> can read and something a
/// later run can diff against; the failures digest, the instruction files and the schema, so it is
/// something an agent can debug from; and the pointer, so the job log says where it all went.
/// Returns a process exit code (0 = success).
/// </summary>
internal static class MergeCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error) =>
        Run(args, @out, error, Environment.GetEnvironmentVariable);

    /// <summary>
    /// <paramref name="getEnvironmentVariable"/> is the environment the CI detection and the CI writers
    /// read, so a test can drive the GitHub Actions branches without being on GitHub Actions.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error, Func<string, string?> getEnvironmentVariable)
    {
        var inputs = new List<string>();
        string output = "TestRunReport.html";
        string? title = null;
        var writeJson = true;
        var ciSummary = false;
        var publishArtifacts = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-o" or "--output":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    output = args[i];
                    break;
                case "-t" or "--title":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    title = args[i];
                    break;
                case "--no-json":
                    writeJson = false;
                    break;
                case "--ci-summary":
                    ciSummary = true;
                    break;
                case "--publish-artifacts":
                    publishArtifacts = true;
                    break;
                case "-h" or "--help":
                    PrintUsage(@out);
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error.WriteLine($"Unknown option: {arg}");
                        return 2;
                    }
                    inputs.Add(arg);
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            error.WriteLine("No inputs given. Specify one or more files, directories, or glob patterns containing mergeable TestRunReport.json files.");
            PrintUsage(error);
            return 2;
        }

        var files = ResolveInputFiles(inputs, error);
        if (files.Count == 0)
        {
            error.WriteLine("No matching report files found. Ensure the tests were run with GenerateMergeableData enabled.");
            return 1;
        }

        // Before anything is read or written. The check used to live below the render, where it could
        // only ever report the damage: the HTML destination was never compared against the inputs at all
        // - only the .json name derived from it - so `-o <a-shard>.json` overwrote a shard with half a
        // megabyte of HTML and then printed a message saying the shard had been protected.
        if (RefuseAnOutputThatIsAnInput(output, writeJson, files, error))
            return 2;

        @out.WriteLine($"Merging {files.Count} report file(s):");
        foreach (var f in files)
            @out.WriteLine("  " + f);

        string written;
        MergeableReport merged;
        try
        {
            merged = MergeableReportRenderer.MergeFiles(files);
            written = MergeableReportRenderer.Render(merged, output, title);
            @out.WriteLine($"Wrote combined report to {written}");
        }
        catch (FormatException ex)
        {
            error.WriteLine("Failed to read a report: " + ex.Message);
            return 1;
        }
        catch (System.Text.Json.JsonException ex)
        {
            error.WriteLine("A report file is not valid JSON: " + ex.Message);
            return 1;
        }
        catch (Exception ex) when (Query.QueryWriter.IsAWriteFailure(ex))
        {
            error.WriteLine($"Could not write {output}: {ex.Message}");
            return 1;
        }

        // `--no-json` is documented as "the HTML only", and means it: no data file, and none of the files
        // that exist to help somebody read the data file.
        if (!writeJson)
            return 0;

        // Only after the render succeeded: a data file beside a report that was never written is worse
        // than neither, because the next command finds it and believes the merge worked.
        var dataFile = WriteMergedData(merged, written, @out, error);
        if (dataFile is null)
            return 1;

        // The rest of what a run writes beside its report. A merge used to stop at the two files above,
        // so the one shape of run that is always on CI had no Failures.md to read first, no CLAUDE.md to
        // load, no schema, no pointer in the job log and nothing in the job summary.
        try
        {
            var options = new ReportConfigurationOptions
            {
                WriteCiSummary = ciSummary,
                PublishCiArtifacts = publishArtifacts
            };
            MergedRunOutputs.Write(merged, written, dataFile, options, @out, error, getEnvironmentVariable);
            return 0;
        }
        catch (Exception ex)
        {
            // The report and its data file are on disk and usable; what is missing is the tail. Say so and
            // fail, rather than let a CI step that asked for a merged report believe it got everything.
            error.WriteLine($"The merged report was written, but the files beside it were not: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Refuses, before a byte is written, an <c>-o</c> that names one of the files being merged.
    /// </summary>
    /// <remarks>
    /// <para><b>Both</b> destinations are checked, because <c>merge</c> writes two files from one
    /// argument. The guard used to compare only the <c>.json</c> name derived from <c>-o</c>, so the
    /// <c>.html</c> spelling was protected and the <c>.json</c> spelling destroyed the shard it was
    /// pointed at - while printing a refusal that described a protection which had not happened. Because
    /// a directory input is swept recursively for <c>*.json</c>, the next run of the same command would
    /// then merge its own output back in and double-count everything.</para>
    ///
    /// <para><c>--no-json</c> skipped the helper the old check lived inside, so the one invocation that
    /// produces no data file at all was also the one with no protection and nothing on stderr. It is now
    /// the reason the JSON destination is checked only when it will actually be written, and the reason
    /// the HTML destination is checked unconditionally.</para>
    ///
    /// <para>The files the tail writes - <c>Failures.md</c>, <c>CLAUDE.md</c>, the schema - need no
    /// check: an input is a <c>*.json</c> that is not a <c>*.schema.json</c>, and none of them is one.</para>
    /// </remarks>
    private static bool RefuseAnOutputThatIsAnInput(string output, bool writeJson, List<string> inputs, TextWriter error)
    {
        var html = Path.GetFullPath(output);
        var json = Path.GetFullPath(Path.ChangeExtension(output, ".json"));

        foreach (var (destination, what) in writeJson
                     ? new[] { (html, "combined report"), (json, "merged data file") }
                     : [(html, "combined report")])
        {
            if (!inputs.Any(f => string.Equals(f, destination, StringComparison.OrdinalIgnoreCase)))
                continue;

            error.WriteLine($"Not merging: the {what} would be written to {destination}, which is one of the inputs.");
            error.WriteLine("Choose a different -o — outside the directory being merged, or with a name no shard uses.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Writes the merged data file beside the merged HTML, taking its name from <c>-o</c> so the two
    /// always travel together. Returns the path written, or null when it could not be.
    /// </summary>
    /// <remarks>
    /// Unguarded until 3.5.0, and masked only by the HTML render running first: an unwritable -o
    /// reached this line as an unhandled throw after the merge had already succeeded. Since 3.6.0 the
    /// caller acts on the answer rather than discarding it - a merge that wrote a report and failed to
    /// write the data file beside it has not done what it was asked, and must not exit 0.
    /// </remarks>
    private static string? WriteMergedData(MergeableReport merged, string writtenHtml, TextWriter @out, TextWriter error)
    {
        var destination = Path.GetFullPath(Path.ChangeExtension(writtenHtml, ".json"));

        if (!Query.QueryWriter.TryWriteFile(destination, MergeableReportRenderer.Serialize(merged), error, "-o"))
            return null;

        @out.WriteLine($"Wrote merged data to {destination}");
        return destination;
    }

    /// <summary>
    /// Expands the supplied inputs into a deduplicated, ordered list of report files. Each input may be a
    /// file, a directory (searched recursively for <c>*.json</c>), or a glob pattern. Schema files
    /// (<c>*.schema.json</c>) are excluded.
    /// </summary>
    internal static List<string> ResolveInputFiles(IEnumerable<string> inputs, TextWriter error)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
                return;
            var full = Path.GetFullPath(path);
            if (seen.Add(full))
                result.Add(full);
        }

        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                // A `baseline/` folder is last-green, kept beside a run for `query diff --baseline`.
                // Sweeping it in as a shard would merge yesterday's results into today's.
                foreach (var f in Directory.EnumerateFiles(input, "*.json", SearchOption.AllDirectories)
                             .Where(f => !IsUnderBaselineFolder(input, f))
                             .OrderBy(x => x, StringComparer.Ordinal))
                    Add(f);
            }
            else if (File.Exists(input))
            {
                Add(input);
            }
            else
            {
                // Treat as a glob relative to the current directory (or a rooted base).
                var dir = Path.GetDirectoryName(input);
                var pattern = Path.GetFileName(input);
                var baseDir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
                if (string.IsNullOrEmpty(pattern) || !Directory.Exists(baseDir))
                {
                    error.WriteLine($"Input not found: {input}");
                    continue;
                }
                var matches = Directory.EnumerateFiles(baseDir, pattern, SearchOption.TopDirectoryOnly)
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                if (matches.Length == 0)
                    error.WriteLine($"No files matched: {input}");
                foreach (var f in matches)
                    Add(f);
            }
        }

        return result;
    }

    private static bool IsUnderBaselineFolder(string root, string file) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, "baseline", StringComparison.OrdinalIgnoreCase));

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: kronikol merge <inputs...> [-o <output.html>] [-t <title>] [--no-json] [--ci-summary] [--publish-artifacts]");
        w.WriteLine();
        w.WriteLine("  Combines several mergeable TestRunReport.json files (produced with");
        w.WriteLine("  ReportConfigurationOptions.GenerateMergeableData = true) into one combined HTML report, and");
        w.WriteLine("  writes beside it what a run writes beside its report: the merged data file - readable with");
        w.WriteLine("  `kronikol query`, and usable as the baseline a later run diffs against - plus Failures.md and");
        w.WriteLine("  Failures.jsonl, CLAUDE.md and AGENTS.md, and the data file's schema. Ends with the pointer");
        w.WriteLine("  a run prints: where the files are, how big the data file is, what failed, and the command");
        w.WriteLine("  that explains it. A shard given twice - the same artifact in two folders, a previous merge's");
        w.WriteLine("  output left in the directory - is counted once.");
        w.WriteLine();
        w.WriteLine("Arguments:");
        w.WriteLine("  <inputs...>          Files, directories (searched recursively), or glob patterns.");
        w.WriteLine("                       A `baseline` folder is skipped: it is last-green, not a shard.");
        w.WriteLine("Options:");
        w.WriteLine("  -o, --output <path>  Output HTML path (default: TestRunReport.html). The data file and its");
        w.WriteLine("                       schema take the same name with .json and .schema.json extensions.");
        w.WriteLine("  -t, --title <text>   Report title (default: \"Test Run Report\").");
        w.WriteLine("      --no-json        Write the HTML only - no data file, and none of the files beside it.");
        w.WriteLine("      --ci-summary     Also write CiSummary.md and post it to the CI job summary (GitHub Actions");
        w.WriteLine("                       step summary, Azure DevOps upload). Off, a failing merge on CI still posts");
        w.WriteLine("                       the short \"Debug this run\" section, as a failing run does.");
        w.WriteLine("      --publish-artifacts");
        w.WriteLine("                       Hand the output directory to the CI artifact upload (GitHub Actions");
        w.WriteLine("                       `reports-path` output, Azure DevOps artifact.upload).");
        w.WriteLine("  -h, --help           Show this help.");
        w.WriteLine();
        w.WriteLine("Example:");
        w.WriteLine("  kronikol merge ./artifacts -o TestRunReport.html -t \"Nightly\"");
    }
}
