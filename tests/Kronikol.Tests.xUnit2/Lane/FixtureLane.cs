using System.Diagnostics;
using System.Text;

namespace Kronikol.Tests.xUnit2.Lane;

/// <summary>One way to run a fixture: the project, and what the lane changes about the run.</summary>
/// <param name="Fixture">The project's folder and name under <c>tests/Kronikol.Tests.xUnit2.Fixtures</c>.</param>
/// <param name="Label">What the variant is, for the results folder and failure messages.</param>
/// <param name="Attempt">A variant run more than once gets a run of its own per attempt (T2 runs three).</param>
/// <param name="Environment">Variables for the child; <c>{results}</c> in a value is the run's results folder.</param>
/// <param name="RunSettings">A run setting for the adapter, after <c>--</c> (<c>xUnit.MethodDisplay=method</c>).</param>
/// <param name="ListTests">Lists the tests instead of running them.</param>
/// <param name="FilesToCreate">Empty files made before the run; <c>{results}</c> as in <paramref name="Environment"/>.</param>
public sealed record FixtureVariant(
    string Fixture,
    string Label = "default",
    int Attempt = 1,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? RunSettings = null,
    bool ListTests = false,
    IReadOnlyList<string>? FilesToCreate = null)
{
    public string Key => $"{Fixture}.{Label}.{Attempt}";

    public override string ToString() => Key;
}

/// <summary>What one child <c>dotnet test</c> of a fixture left behind, copied out before the next run.</summary>
public sealed class FixtureRun
{
    public required FixtureVariant Variant { get; init; }
    public required string ResultsDirectory { get; init; }
    public required int ExitCode { get; init; }
    public required string Output { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public TrxFile? Trx { get; init; }

    /// <summary>A copy of the run's reports directory, or null when the run wrote none.</summary>
    public string? ReportsDirectory { get; init; }

    /// <summary>The <c>kronikol-error.log</c> the run left beside its assembly, or null.</summary>
    public string? ErrorLog { get; init; }

    private ReportFile? _report;

    /// <summary>The run's TestRunReport.json, read field by field.</summary>
    public ReportFile Report =>
        _report ??= ReportsDirectory is { } directory && File.Exists(Path.Combine(directory, "TestRunReport.json"))
            ? ReportFile.Read(Path.Combine(directory, "TestRunReport.json"))
            : throw new InvalidOperationException($"{Variant} wrote no TestRunReport.json.{Environment.NewLine}{Describe()}");

    public string? ReadReportFile(string name) =>
        ReportsDirectory is { } directory && File.Exists(Path.Combine(directory, name))
            ? File.ReadAllText(Path.Combine(directory, name))
            : null;

    public string Describe() =>
        $"{Variant}: exit code {ExitCode}, {Elapsed.TotalSeconds:0.0} s, reports {(ReportsDirectory is null ? "none" : "written")}, " +
        $"error log {(ErrorLog is null ? "none" : "written")}.{Environment.NewLine}{Output}";
}

/// <summary>
/// The fixture assemblies under <c>tests/Kronikol.Tests.xUnit2.Fixtures</c>, each run by <c>dotnet test</c> in a
/// child process. A child process is not optional: <c>ReportLifecycle</c>'s once-per-process guard, the
/// collected scenarios and the process-wide call log would carry one run into the next. Builds and runs go
/// one at a time, and each run is made once per test process and shared by every fact that reads it.
/// </summary>
public static class FixtureLane
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, FixtureRun> Runs = [];
    private static readonly HashSet<string> Built = [];

    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string FixturesRoot => Path.Combine(RepositoryRoot, "tests", "Kronikol.Tests.xUnit2.Fixtures");

    /// <summary>Every fixture builds into one artifacts tree, so Kronikol and Kronikol.xUnit2 build once for all of them.</summary>
    public static string ArtifactsRoot { get; } = Path.Combine(AppContext.BaseDirectory, "xunit2-fixtures");

    public static string OutputDirectory(string fixture) => Path.Combine(ArtifactsRoot, "bin", fixture, "debug");

    public static FixtureRun Run(FixtureVariant variant)
    {
        lock (Gate)
        {
            if (Runs.TryGetValue(variant.Key, out var run))
                return run;

            Build(variant.Fixture);
            run = Execute(variant);
            Runs[variant.Key] = run;
            return run;
        }
    }

    private static void Build(string fixture)
    {
        if (Built.Contains(fixture))
            return;

        var (exitCode, output, _) = Dotnet(
            ["build", ProjectPath(fixture), "--artifacts-path", ArtifactsRoot, "--disable-build-servers", "-nodeReuse:false"],
            new Dictionary<string, string>(), BuildTimeout);
        if (exitCode != 0)
            throw new InvalidOperationException($"Building the {fixture} fixture failed with exit code {exitCode}.{Environment.NewLine}{output}");
        Built.Add(fixture);
    }

    private static FixtureRun Execute(FixtureVariant variant)
    {
        var outputDirectory = OutputDirectory(variant.Fixture);
        var reportsDirectory = Path.Combine(outputDirectory, "Reports");
        var errorLog = Path.Combine(outputDirectory, "kronikol-error.log");
        if (Directory.Exists(reportsDirectory))
            Directory.Delete(reportsDirectory, recursive: true);
        File.Delete(errorLog);

        var resultsDirectory = Path.Combine(ArtifactsRoot, "results", variant.Key);
        if (Directory.Exists(resultsDirectory))
            Directory.Delete(resultsDirectory, recursive: true);
        Directory.CreateDirectory(resultsDirectory);
        foreach (var file in variant.FilesToCreate ?? [])
            File.WriteAllText(file.Replace("{results}", resultsDirectory), "");

        var arguments = new List<string>
        {
            "test", ProjectPath(variant.Fixture), "--no-build", "--artifacts-path", ArtifactsRoot,
            "--logger", "trx;LogFileName=run.trx", "--results-directory", resultsDirectory,
        };
        if (variant.ListTests)
            arguments.Add("--list-tests");
        if (variant.RunSettings is not null)
        {
            arguments.Add("--");
            arguments.Add(variant.RunSettings);
        }

        var environment = new Dictionary<string, string>
        {
            // A run looks for its history ledger above its output, which here is the checkout itself.
            ["KRONIKOL_HISTORY"] = "off",
        };
        foreach (var (name, value) in variant.Environment ?? new Dictionary<string, string>())
            environment[name] = value.Replace("{results}", resultsDirectory);

        var (exitCode, output, elapsed) = Dotnet(arguments, environment, RunTimeout);

        string? reportsCopy = null;
        if (Directory.Exists(reportsDirectory))
        {
            reportsCopy = Path.Combine(resultsDirectory, "Reports");
            CopyDirectory(reportsDirectory, reportsCopy);
        }

        var trxPath = Path.Combine(resultsDirectory, "run.trx");
        return new FixtureRun
        {
            Variant = variant,
            ResultsDirectory = resultsDirectory,
            ExitCode = exitCode,
            Output = output,
            Elapsed = elapsed,
            Trx = File.Exists(trxPath) ? TrxFile.Read(trxPath) : null,
            ReportsDirectory = reportsCopy,
            ErrorLog = File.Exists(errorLog) ? File.ReadAllText(errorLog) : null,
        };
    }

    private static string ProjectPath(string fixture) => Path.Combine(FixturesRoot, fixture, fixture + ".csproj");

    private static (int ExitCode, string Output, TimeSpan Elapsed) Dotnet(
        IEnumerable<string> arguments, IReadOnlyDictionary<string, string> environment, TimeSpan timeout)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FixturesRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        // The child is a local run whatever runs this one: on GitHub Actions it would otherwise append its CI
        // summary to this job's, and read the job's branch as its own.
        foreach (var name in start.Environment.Keys.ToArray())
        {
            if (name.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CI", StringComparison.OrdinalIgnoreCase)
                || name.Equals("TF_BUILD", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("KRONIKOL_", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(name);
        }
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        var output = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };

        var clock = Stopwatch.StartNew();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} did not finish in {timeout}.{Environment.NewLine}{output}");
        }
        process.WaitForExit();
        clock.Stop();

        lock (output)
            return (process.ExitCode, output.ToString(), clock.Elapsed);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kronikol.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"No Kronikol.sln above {AppContext.BaseDirectory}.");
    }
}
