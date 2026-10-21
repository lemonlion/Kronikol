using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Example.Api.Tests.Integration.Helpers;

public record TestProjectRunResult(
    bool Success,
    string StandardOutput,
    string StandardError,
    string ReportsFolderPath,
    int ExitCode);

public static class TestProjectRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(30);

    private static readonly string ArchivedReportsRoot =
        Path.Combine(TestProjects.SolutionRoot, "TestResults", "ArchivedReports");

    private static readonly string NestedResultsRoot =
        Path.Combine(TestProjects.SolutionRoot, "TestResults", "NestedRuns");

    // A component project is built once per test process, and each run starts it with --no-build. Each run used to build
    // it first: measured on 2026-10-08 (the TUnit example, Windows, other builds on the machine), a build with nothing to
    // do took 76 to 84 s against 2 s for the run itself, so a third of the TUnit rows hit the 120 s timeout. A failed build
    // is the result of every run of that project, with the build's own output, where "dotnet run" said only that the build
    // failed.
    private static readonly ConcurrentDictionary<string, Lazy<Task<TestProjectRunResult?>>> Builds = new();

    public static async Task<TestProjectRunResult> RunAsync(
        string projectName,
        Dictionary<string, string>? environmentVariables = null,
        TimeSpan? timeout = null,
        [CallerMemberName] string runLabel = "")
    {
        var projectPath = TestProjects.GetProjectPath(projectName);
        var reportsFolderPath = TestProjects.GetReportsFolderPath(projectName);

        var buildFailure = await Builds
            .GetOrAdd(projectPath, path => new Lazy<Task<TestProjectRunResult?>>(() => BuildAsync(path, reportsFolderPath)))
            .Value;
        if (buildFailure is not null)
            return buildFailure;

        // Clean previous reports so we only see fresh output
        if (Directory.Exists(reportsFolderPath))
            Directory.Delete(reportsFolderPath, recursive: true);

        var environment = new Dictionary<string, string>
        {
            // Always enable integration mode
            ["KRONIKOL_INTEGRATION_MODE"] = "true"
        };
        if (environmentVariables is not null)
        {
            foreach (var (key, value) in environmentVariables)
                environment[key] = value;
        }

        // TUnit / Microsoft.Testing.Platform projects require "dotnet run" on .NET 10+. A "dotnet test" run writes a results
        // file, read if the run fails: a test assembly's cleanup failure reaches the console as one line naming the
        // exception's type, and the file carries its message and stack trace (TrxFailures).
        var microsoftTestingPlatform = TestProjects.MicrosoftTestingPlatformProjects.Contains(projectName);
        var resultsFile = $"{projectName.Replace("Example.Api.Tests.Component.", "")}.{runLabel}.{Guid.NewGuid():N}.trx";
        var arguments = microsoftTestingPlatform
            ? "run --no-restore --no-build"
            : $"test --no-restore --no-build --verbosity quiet --logger \"trx;LogFileName={resultsFile}\" --results-directory \"{NestedResultsRoot}\"";

        var effectiveTimeout = timeout ?? DefaultTimeout;
        var (completed, exitCode, stdout, stderr) = await RunDotnetAsync(arguments, projectPath, environment, effectiveTimeout);

        if (!completed)
            return new TestProjectRunResult(false, stdout, $"Process timed out after {effectiveTimeout.TotalSeconds}s", reportsFolderPath, -1);

        ArchiveReports(reportsFolderPath, projectName, runLabel);

        var failures = exitCode == 0 || microsoftTestingPlatform ? "" : TrxFailures.Read(Path.Combine(NestedResultsRoot, resultsFile));
        if (failures.Length > 0)
            stderr += $"\n--- the run's results file, {resultsFile} ---\n{failures}\n";
        return new TestProjectRunResult(exitCode == 0, stdout, stderr, reportsFolderPath, exitCode);
    }

    private static async Task<TestProjectRunResult?> BuildAsync(string projectPath, string reportsFolderPath)
    {
        var (completed, exitCode, stdout, stderr) =
            await RunDotnetAsync("build --no-restore -nologo", projectPath, new Dictionary<string, string>(), BuildTimeout);

        if (!completed)
            return new TestProjectRunResult(false, stdout, $"The build timed out after {BuildTimeout.TotalMinutes} minutes", reportsFolderPath, -1);

        return exitCode == 0
            ? null
            : new TestProjectRunResult(false, stdout, $"The build failed (exit {exitCode}).\n{stderr}", reportsFolderPath, exitCode);
    }

    private static async Task<(bool Completed, int ExitCode, string StandardOutput, string StandardError)> RunDotnetAsync(
        string arguments, string workingDirectory, Dictionary<string, string> environment, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var (key, value) in environment)
            psi.Environment[key] = value;
        // A build leaves MSBuild nodes running for the next build to reuse, and a node started here inherits the
        // redirected output, so the read below never saw its end: on Windows a run hung for good after a component's
        // build (2026-10-12). The children reuse no node.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));

        if (!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return (false, -1, stdout.ToString(), stderr.ToString());
        }

        // The timed WaitForExit can return before the redirected output is read to its end; this waits for the end, for
        // a while, since a process the child left behind could hold the output open for ever.
        using var drain = new CancellationTokenSource(OutputDrainTimeout);
        try { await process.WaitForExitAsync(drain.Token); }
        catch (OperationCanceledException) { /* the output read so far is the run's */ }

        return (true, process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void ArchiveReports(string reportsFolderPath, string projectName, string runLabel)
    {
        if (!Directory.Exists(reportsFolderPath))
            return;

        // Short project name for the folder: strip the common prefix
        var shortName = projectName.Replace("Example.Api.Tests.Component.", "");
        var archiveDir = Path.Combine(ArchivedReportsRoot, shortName);
        Directory.CreateDirectory(archiveDir);

        foreach (var sourceFile in Directory.GetFiles(reportsFolderPath))
        {
            var fileName = Path.GetFileNameWithoutExtension(sourceFile);
            var ext = Path.GetExtension(sourceFile);
            var label = string.IsNullOrEmpty(runLabel) ? "" : $".{runLabel}";
            var destFile = Path.Combine(archiveDir, $"{fileName}{label}{ext}");
            File.Copy(sourceFile, destFile, overwrite: true);
        }
    }
}