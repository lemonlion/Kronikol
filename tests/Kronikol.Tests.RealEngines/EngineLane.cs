using System.Diagnostics;
using System.Text;

namespace Kronikol.Tests.RealEngines;

/// <summary>What one child run of a fixture left behind.</summary>
public sealed record EngineRun(string Fixture, int ExitCode, string Output, string? ReportsDirectory)
{
    private ReportRows? _rows;

    /// <summary>The run's report, read field by field. A fixture fails on purpose, so its exit code says nothing here.</summary>
    public ReportRows Rows => _rows ??= ReportsDirectory is { } directory && ReportRows.DataFile(directory) is { } file
        ? ReportRows.Read(file)
        : throw new InvalidOperationException($"{Fixture} wrote no report, exit code {ExitCode}.{Environment.NewLine}{Output}");
}

/// <summary>
/// The test assemblies under <c>tests/Kronikol.Tests.RealEngines.Fixtures</c>, each run in a child process, as a framework's
/// real engine decides the order of its hooks and steps and a process-wide report can be written only once. Builds and runs go
/// one at a time, and each run is made once per test process and shared by every fact that reads it.
/// </summary>
public static class EngineLane
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, EngineRun> Runs = [];

    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(5);

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string FixturesRoot => Path.Combine(RepositoryRoot, "tests", "Kronikol.Tests.RealEngines.Fixtures");

    /// <summary>Every fixture builds into one artifacts tree, so the core builds once for all of them.</summary>
    public static string ArtifactsRoot { get; } = Path.Combine(AppContext.BaseDirectory, "real-engines");

    public static EngineRun Run(string fixture, IReadOnlyDictionary<string, string>? environment = null, string? label = null)
    {
        lock (Gate)
        {
            var key = $"{fixture}.{label}";
            if (Runs.TryGetValue(key, out var run))
                return run;

            run = Execute(fixture, environment ?? new Dictionary<string, string>());
            Runs[key] = run;
            return run;
        }
    }

    private static EngineRun Execute(string fixture, IReadOnlyDictionary<string, string> environment)
    {
        var project = Path.Combine(FixturesRoot, fixture, fixture + ".csproj");
        if (fixture == "Engine.Reqnroll.Floor")
            CopyFeaturesBesideTheFloorLane();

        var (buildExit, buildOutput) = Dotnet(
            ["build", project, "--artifacts-path", ArtifactsRoot, "--disable-build-servers", "-nodeReuse:false"],
            new Dictionary<string, string>(), BuildTimeout);
        if (buildExit != 0)
            return new EngineRun(fixture, buildExit, buildOutput, null);

        var outputDirectory = Path.Combine(ArtifactsRoot, "bin", fixture, "debug");
        var reportsDirectory = Path.Combine(outputDirectory, "Reports");
        if (Directory.Exists(reportsDirectory))
            Directory.Delete(reportsDirectory, recursive: true);

        // A TUnit assembly runs itself: `dotnet test` refuses it on this SDK.
        string[] arguments = fixture.EndsWith(".TUnit", StringComparison.Ordinal)
            ? ["run", "--project", project, "--no-build", "--artifacts-path", ArtifactsRoot]
            : ["test", project, "--no-build", "--artifacts-path", ArtifactsRoot];

        var childEnvironment = new Dictionary<string, string>(environment)
        {
            // A run looks for its history ledger above its output, which here is the checkout itself.
            ["KRONIKOL_HISTORY"] = "off",
        };

        var (exitCode, output) = Dotnet(arguments, childEnvironment, RunTimeout);
        return new EngineRun(fixture, exitCode, output, Directory.Exists(reportsDirectory) ? reportsDirectory : null);
    }

    // Reqnroll 3.0.1 reads the features under a project and writes each one's code beside it, where every other lane shares one
    // folder of them and keeps the code in obj.
    private static void CopyFeaturesBesideTheFloorLane()
    {
        var copies = Path.Combine(FixturesRoot, "Engine.Reqnroll.Floor", "Features");
        Directory.CreateDirectory(copies);
        foreach (var feature in Directory.GetFiles(Path.Combine(FixturesRoot, "Reqnroll", "Features"), "*.feature"))
            File.Copy(feature, Path.Combine(copies, Path.GetFileName(feature)), overwrite: true);
    }

    private static (int ExitCode, string Output) Dotnet(
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

        // The child is a local run whatever runs this one: on GitHub Actions it would otherwise append its CI summary to this
        // job's, and read the job's branch as its own.
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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} did not finish in {timeout}.{Environment.NewLine}{output}");
        }
        process.WaitForExit();

        lock (output)
            return (process.ExitCode, output.ToString());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "Kronikol.Tests.RealEngines.Fixtures")))
                return directory.FullName;
        }
        throw new InvalidOperationException($"No repository root above {AppContext.BaseDirectory}.");
    }
}
