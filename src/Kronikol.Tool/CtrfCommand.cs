using System.Globalization;
using Kronikol.Reports;
using Kronikol.Tool.Query;

namespace Kronikol.Tool;

/// <summary>
/// Implements <c>kronikol ctrf</c>: turn a <c>TestRunReport.json</c> that has already been written into a
/// Common Test Report Format document, for the CI tooling that speaks CTRF and will never speak
/// Kronikol's own schema.
///
/// <para><b>Why a verb of its own.</b> A run can write <c>ctrf-report.json</c> itself
/// (<c>GenerateCtrfReport</c>), but by the time anyone wants CTRF the run is usually over: the report is
/// a downloaded CI artifact, or came from a colleague, or belongs to a suite whose options nobody wants
/// to change. This is not <c>kronikol export</c> — that reads NDJSON captures and POSTs spans to a
/// collector; every part of its contract is the wrong one here. Nor a <c>query</c> verb: those answer
/// questions under a byte budget, and a document that gets truncated is not a document.</para>
///
/// <para><b>One format, one writer.</b> The mapping from Kronikol's model to CTRF's lives in
/// <see cref="CtrfReportGenerator"/> and both producers go through it, so the envelope, the summary and
/// the key names cannot drift. What this file owns is only the other half of the mapping: reading the
/// same facts back out of a report that has already been serialised.</para>
///
/// <para>Exit codes follow the tool's convention: 0 success, 1 runtime failure, 2 usage error.</para>
/// </summary>
internal static class CtrfCommand
{
    public static int Run(IReadOnlyList<string> args, TextWriter @out, TextWriter error)
    {
        if (args.Count == 0)
        {
            PrintUsage(error);
            return 2;
        }

        string? input = null;
        string? outFile = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    PrintUsage(@out);
                    return 0;
                case "-o" or "--out":
                    if (++i >= args.Count) { error.WriteLine("Missing value for " + arg); return 2; }
                    outFile = args[i];
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error.WriteLine($"Unknown option: {arg}");
                        return 2;
                    }
                    if (input is not null)
                    {
                        error.WriteLine($"Convert one report at a time: '{input}' and '{arg}' were both given.");
                        return 2;
                    }
                    input = arg;
                    break;
            }
        }

        if (input is null)
        {
            error.WriteLine("No report given. Pass a TestRunReport.json, or a directory holding one.");
            return 2;
        }

        var resolved = QueryCommand.ResolveReport(input, error);
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

        var document = CtrfReportGenerator.Build(
            [.. index.Scenarios.Select(Map)],
            ParseTime(index.StartTime),
            ParseTime(index.EndTime),
            Environment(index),
            index.KronikolVersion ?? "");

        if (outFile is null)
        {
            @out.WriteLine(document);
            return 0;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outFile));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(outFile, document);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not write {outFile}: {exception.Message}");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// The half of the mapping this verb owns: a scanned scenario as a CTRF test. Every rule here has a
    /// twin in <see cref="CtrfReportGenerator.Generate"/> and the two are held together by
    /// <c>CtrfCommandTests.The_converted_document_is_the_one_the_run_would_have_written</c>.
    /// </summary>
    private static CtrfTest Map(ScenarioEntry scenario)
    {
        var result = Enum.TryParse<ExecutionResult>(scenario.Result, out var parsed) ? parsed : ExecutionResult.Bypassed;
        var retries = scenario.Attempt is { } attempt && attempt > 1 ? attempt - 1 : 0;
        var filePath = Trimmed(scenario.SourceFile) ?? Trimmed(scenario.FeatureSourceFile);

        return new CtrfTest
        {
            Name = scenario.Name,
            Status = CtrfReportGenerator.MapStatus(result),
            Duration = (long)Math.Round(scenario.DurationSeconds * 1000),
            Suite = scenario.FeatureName,
            Message = Trimmed(scenario.ErrorMessage),
            Trace = Trimmed(scenario.ErrorStackTrace),
            FilePath = filePath,
            Line = scenario.SourceFile is { Length: > 0 } ? scenario.SourceLine : null,
            Tags = [.. scenario.Labels.Where(l => !IsGeneratedRetryLabel(l))],
            Retries = retries,
            Flaky = retries > 0 && result == ExecutionResult.Passed,
            RawStatus = scenario.Result,
            Address = scenario.Address,
            StableId = Trimmed(scenario.StableId),
            Categories = [.. scenario.Categories]
        };
    }

    /// <summary>
    /// The environment block, only when the file says the run was actually on CI. A report written off CI
    /// still carries a <c>ciMetadata</c> object — provider <c>None</c> and nulls beneath it — and turning
    /// that into an environment block would claim a build named "None" that never existed.
    /// </summary>
    private static CtrfEnvironment? Environment(ReportIndex index)
    {
        if (!index.OnCi)
            return null;

        var environment = new CtrfEnvironment
        {
            BuildName = index.CiProvider,
            BuildNumber = index.CiBuildNumber,
            BuildUrl = index.CiPipelineUrl,
            RepositoryName = index.CiRepository,
            Commit = index.CiCommitSha,
            BranchName = index.CiBranch,
            RunId = index.CiRunId,
            RunAttempt = index.CiRunAttempt
        };
        return environment.IsEmpty ? null : environment;
    }

    /// <summary>Mirrors <c>CtrfReportGenerator.IsGeneratedRetryLabel</c>; see the note on tags there.</summary>
    private static bool IsGeneratedRetryLabel(string label) =>
        label.StartsWith("retry ", StringComparison.Ordinal)
        && label.Length > 6
        && label.AsSpan(6).ToString().All(char.IsAsciiDigit);

    private static string? Trimmed(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// The report's own <c>yyyy-MM-ddTHH:mm:ssZ</c> stamps. A file too old or too damaged to carry one
    /// gets the epoch rather than a fabricated window - CTRF requires the field.
    /// </summary>
    private static DateTime ParseTime(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTime.UnixEpoch;

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: kronikol ctrf <report> [--out <file>]");
        w.WriteLine();
        w.WriteLine("  Converts a TestRunReport.json into a Common Test Report Format document, so CI tooling that");
        w.WriteLine("  already speaks CTRF - annotation actions, PR comment bots, flaky-test dashboards - can read a");
        w.WriteLine("  Kronikol run. One CTRF test per scenario, with its status, duration, failure message and trace,");
        w.WriteLine("  plus the sN address that leads back into `kronikol query`.");
        w.WriteLine();
        w.WriteLine("  A run can write this file itself instead: set GenerateCtrfReport in ReportConfigurationOptions.");
        w.WriteLine();
        w.WriteLine("Arguments:");
        w.WriteLine("  <report>                 A TestRunReport.json, or the directory holding one.");
        w.WriteLine("Options:");
        w.WriteLine("  -o, --out <file>         Write the document to a file instead of stdout.");
        w.WriteLine("  -h, --help               Show this help.");
        w.WriteLine();
        w.WriteLine("Example:");
        w.WriteLine("  kronikol ctrf ./TestResults --out ctrf-report.json");
    }
}
