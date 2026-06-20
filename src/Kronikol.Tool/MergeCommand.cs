using Kronikol.Reports.Merge;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol merge</c>: combine several mergeable <c>TestRunReport.json</c> files into a
/// single combined <c>TestRunReport.html</c>. Returns a process exit code (0 = success).
/// </summary>
internal static class MergeCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error)
    {
        var inputs = new List<string>();
        string output = "TestRunReport.html";
        string? title = null;

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

        @out.WriteLine($"Merging {files.Count} report file(s):");
        foreach (var f in files)
            @out.WriteLine("  " + f);

        try
        {
            var written = MergeableReportRenderer.MergeFilesToHtml(files, output, title);
            @out.WriteLine($"Wrote combined report to {written}");
            return 0;
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
                foreach (var f in Directory.EnumerateFiles(input, "*.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
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

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: kronikol merge <inputs...> [-o <output.html>] [-t <title>]");
        w.WriteLine();
        w.WriteLine("  Combines several mergeable TestRunReport.json files (produced with");
        w.WriteLine("  ReportConfigurationOptions.GenerateMergeableData = true) into one combined HTML report.");
        w.WriteLine();
        w.WriteLine("Arguments:");
        w.WriteLine("  <inputs...>          Files, directories (searched recursively), or glob patterns.");
        w.WriteLine("Options:");
        w.WriteLine("  -o, --output <path>  Output HTML path (default: TestRunReport.html).");
        w.WriteLine("  -t, --title <text>   Report title (default: \"Test Run Report\").");
        w.WriteLine("  -h, --help           Show this help.");
        w.WriteLine();
        w.WriteLine("Example:");
        w.WriteLine("  kronikol merge ./artifacts -o TestRunReport.html -t \"Nightly\"");
    }
}
