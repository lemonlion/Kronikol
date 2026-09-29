namespace Kronikol.Tests.Templates;

/// <summary>
/// The Kronikol tool the history action's <c>record</c> and <c>gate</c> run (plans/HISTORY_ACTION_PLAN.md §4.7, F12):
/// the version in the folder's <c>VERSION</c>, installed with <c>dotnet tool install</c> into a path of its own under
/// <c>RUNNER_TEMP</c>, never <c>--global</c>, and reused by a later phase of the same job; nothing added to the job's
/// <c>PATH</c> or environment; <c>tool-command</c> in its place when given.
///
/// <para><b>A .NET 10 SDK is required, not a runtime.</b> The plan meant to install a private .NET 10 runtime where a
/// machine had none. Measured 2026-09-29 in <c>mcr.microsoft.com/dotnet/sdk:8.0</c>: an SDK below 10 cannot install
/// the tool at all ("Settings file 'DotnetToolSettings.xml' was not found in the package", the package being
/// <c>net10.0</c> only), so that runtime would never have been reached. The phase says what to add instead.</para>
///
/// <para>A stub <c>dotnet</c> stands in for the SDK: on Windows, Git's bash puts its own directories first on
/// <c>PATH</c>, but it ships no <c>dotnet</c>, so the stub is found before the real one there too.</para>
/// </summary>
public class HistoryActionToolTests
{
    private static string Version => File.ReadAllText(Path.Combine(HistoryWorld.Folder, "VERSION")).Trim();

    /// <summary>
    /// A stub <c>dotnet</c> that lists <paramref name="sdk"/> and logs every call. Its <c>tool install</c> writes a
    /// <c>kronikol</c> that logs its <c>DOTNET_ROOT</c> and runs the tool built with these tests, or fails when
    /// <paramref name="installFails"/>.
    /// </summary>
    private static (string Bin, string Log, string Root) StubDotnet(HistoryWorld world, string sdk, bool installFails = false)
    {
        var bin = Path.Combine(world.Root, "stub-bin");
        var root = ChildProcess.Slashes(Path.Combine(world.Root, "stub-dotnet-root"));
        var log = ChildProcess.Slashes(Path.Combine(world.Root, "dotnet-calls.log"));
        Directory.CreateDirectory(bin);
        var tool = $"#!/usr/bin/env bash\\necho \"kronikol DOTNET_ROOT=$DOTNET_ROOT\" >> \"{log}\"\\nexec {ChildProcess.ToolCommand.Replace("\"", "\\\"", StringComparison.Ordinal)} \"$@\"\\n";
        var script = $$"""
            #!/usr/bin/env bash
            echo "dotnet $*" >> "{{log}}"
            case "$1" in
              --list-sdks) echo "{{sdk}} [{{root}}/sdk]" ;;
              tool)
                {{(installFails ? "echo \"Version $5 of package kronikol.tool is not found in NuGet feeds https://api.nuget.org/v3/index.json.\" >&2; exit 1" : "")}}
                path=${7:?no --tool-path}
                mkdir -p "$path"
                printf '{{tool}}' > "$path/kronikol"
                chmod +x "$path/kronikol"
                echo "Tool 'kronikol.tool' (version '$5') was successfully installed." ;;
              *) echo "the stub dotnet does not do: $*" >&2; exit 64 ;;
            esac
            """;
        var path = Path.Combine(bin, "dotnet");
        File.WriteAllText(path, script.ReplaceLineEndings("\n"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return (bin, log, root);
    }

    private static RunnerJob JobWithFragment(HistoryWorld world, string bin)
    {
        var job = world.Job(1);
        job.BinDirectories.Add(bin);
        HistoryFixtures.Fragment(Path.Combine(job.Workspace, "Reports"), "A", "gh:1:1", "PP");
        return job;
    }

    private static readonly Dictionary<string, string> Installed = new() { ["path"] = ".", ["tool-command"] = "" };

    [Fact]
    public void The_tool_is_installed_to_a_path_of_its_own_and_the_jobs_PATH_is_unchanged()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var (bin, log, root) = StubDotnet(world, "10.0.100");
        var job = JobWithFragment(world, bin);

        var record = HistoryWorld.Record(job, Installed);

        Assert.True(record.Succeeded, record.ToString());
        Assert.Equal(["gh:1:1 A"], world.Ledger().RunKeys);
        var calls = File.ReadAllLines(log);
        var install = Assert.Single(calls, c => c.StartsWith("dotnet tool install", StringComparison.Ordinal));
        Assert.StartsWith($"dotnet tool install Kronikol.Tool --version {Version} --tool-path ", install, StringComparison.Ordinal);
        Assert.EndsWith(ChildProcess.Slashes(Path.Combine("kronikol-tool", Version)), ChildProcess.Slashes(install), StringComparison.Ordinal);
        Assert.StartsWith(ChildProcess.Slashes(job.Temp), ChildProcess.Slashes(install["dotnet tool install Kronikol.Tool --version ".Length..].Split(" --tool-path ")[1]), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--global", install, StringComparison.Ordinal);
        // The tool ran on the SDK's own .NET, named for its calls only.
        Assert.Contains(calls, c => c.StartsWith("kronikol DOTNET_ROOT=", StringComparison.Ordinal)
                                    && string.Equals(ChildProcess.Slashes(c["kronikol DOTNET_ROOT=".Length..]), root, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(job.AddedPath);
        Assert.DoesNotContain(job.Env.Keys, k => k is "PATH" or "DOTNET_ROOT");

        // A later phase of the same job reuses it.
        var ledger = Path.Combine(job.Temp, "gate-ledger", "history.jsonl");
        HistoryFixtures.Ledger(ledger, "A", "PP", "PP");
        job.Env["KRONIKOL_HISTORY"] = ledger;
        HistoryFixtures.Report(Path.Combine(job.Workspace, "Reports"), "A", 3, "PP");
        var gate = HistoryWorld.Gate(job, new Dictionary<string, string> { ["reports"] = "Reports", ["tool-command"] = "" });

        Assert.True(gate.Succeeded, gate.ToString());
        Assert.Single(File.ReadAllLines(log), c => c.StartsWith("dotnet tool install", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tool_command_replaces_the_install()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var (bin, log, _) = StubDotnet(world, "10.0.100");

        var record = HistoryWorld.Record(JobWithFragment(world, bin), new Dictionary<string, string> { ["path"] = "." });

        Assert.True(record.Succeeded, record.ToString());
        Assert.DoesNotContain(File.Exists(log) ? File.ReadAllLines(log) : [], c => c.StartsWith("dotnet tool install", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_NET_10_SDK_the_phase_fails_naming_what_to_add()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var (bin, log, _) = StubDotnet(world, "8.0.400");

        var record = HistoryWorld.Record(JobWithFragment(world, bin), Installed);

        Assert.False(record.Succeeded);
        Assert.Contains(record.Errors, e => e.Contains("actions/setup-dotnet", StringComparison.Ordinal) && e.Contains("tool-command", StringComparison.Ordinal));
        Assert.DoesNotContain(File.ReadAllLines(log), c => c.StartsWith("dotnet tool install", StringComparison.Ordinal));
        Assert.Null(world.Origin.Tip(HistoryWorld.Branch));
    }

    [Fact]
    public void An_install_that_fails_names_the_version_it_tried()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var (bin, _, _) = StubDotnet(world, "10.0.100", installFails: true);

        var record = HistoryWorld.Record(JobWithFragment(world, bin), Installed);

        Assert.False(record.Succeeded);
        Assert.Contains(record.Errors, e => e.Contains($"Kronikol.Tool {Version}", StringComparison.Ordinal));
        Assert.Null(world.Origin.Tip(HistoryWorld.Branch));
    }
}
