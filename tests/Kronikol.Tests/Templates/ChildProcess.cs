using System.Diagnostics;
using System.Text;

namespace Kronikol.Tests.Templates;

/// <summary>
/// One child process with an environment built for it, never inherited from the test process. The test process carries
/// <c>KRONIKOL_HISTORY=off</c> and <c>KRONIKOL_KEEP_RUNS=off</c> from <c>test.runsettings</c>, and the developer's own git
/// configuration and credentials sit behind <c>HOME</c>: a child that inherited either would test something else.
/// </summary>
internal static class ChildProcess
{
    public sealed record Result(int ExitCode, string Stdout, string Stderr);

    private static readonly Lazy<string?> Dotnet = new(() =>
    {
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host))
            return host;
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), name))
            .FirstOrDefault(File.Exists);
    });

    /// <summary>The <c>dotnet</c> host running these tests, which runs the tool the test output holds.</summary>
    public static string DotnetExecutable => Dotnet.Value ?? throw new InvalidOperationException("no dotnet host found");

    /// <summary>The Kronikol tool built with these tests (the test project references it).</summary>
    public static string ToolDll => Path.Combine(AppContext.BaseDirectory, "Kronikol.Tool.dll");

    /// <summary>A shell command line that runs the tool built with these tests, as a <c>tool-command</c> input takes it.</summary>
    public static string ToolCommand => $"\"{Slashes(DotnetExecutable)}\" \"{Slashes(ToolDll)}\"";

    /// <summary>A path with forward slashes, which bash and .NET both read on Windows.</summary>
    public static string Slashes(string path) => path.Replace('\\', '/');

    public static Result Run(string executable, IEnumerable<string> arguments, IReadOnlyDictionary<string, string> environment,
        string workingDirectory, string? stdin = null, TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start");
        if (stdin is not null)
        {
            using var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
            writer.Write(stdin);
        }
        else
        {
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout ?? TimeSpan.FromMinutes(5)))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{Path.GetFileName(executable)} {string.Join(' ', start.ArgumentList)} did not finish in {timeout ?? TimeSpan.FromMinutes(5)}");
        }
        process.WaitForExit();
        return new Result(process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// What a process needs from the operating system to start, and nothing of the test process's own: a home and a temp
    /// directory of the caller's choosing and, on Windows, the handful of variables Windows programs cannot run without.
    /// No git configuration from the machine (<c>GIT_CONFIG_NOSYSTEM</c>, and a global file of the caller's), because a
    /// hosted runner has none that matters here; <see cref="MachineConfig"/> stands in for one that does.
    /// </summary>
    public static Dictionary<string, string> BaseEnvironment(string home, string temp, string gitConfigGlobal)
    {
        var environment = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        {
            ["HOME"] = home,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = gitConfigGlobal,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
        };

        if (OperatingSystem.IsWindows())
        {
            foreach (var name in new[] { "SystemRoot", "windir", "ComSpec", "PATHEXT", "SystemDrive", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
                         "CommonProgramFiles", "ProgramData", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS" })
            {
                if (Environment.GetEnvironmentVariable(name) is { } value)
                    environment[name] = value;
            }
            environment["TEMP"] = temp;
            environment["TMP"] = temp;
            environment["USERPROFILE"] = home;
            environment["APPDATA"] = Path.Combine(home, "AppData", "Roaming");
            environment["LOCALAPPDATA"] = Path.Combine(home, "AppData", "Local");
        }
        else
        {
            environment["TMPDIR"] = temp;
        }

        return environment;
    }

    /// <summary>The directories every child's <c>PATH</c> ends with: git's, the dotnet host's and the system's.</summary>
    public static IEnumerable<string> SystemPath()
    {
        if (GitProbe.Executable is { } git)
            yield return Path.GetDirectoryName(git)!;
        if (Dotnet.Value is { } dotnet)
            yield return Path.GetDirectoryName(dotnet)!;

        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            yield return Path.Combine(windows, "System32");
            yield return windows;
        }
        else
        {
            foreach (var directory in new[] { "/usr/local/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin", "/opt/homebrew/bin" })
                yield return directory;
        }
    }
}
