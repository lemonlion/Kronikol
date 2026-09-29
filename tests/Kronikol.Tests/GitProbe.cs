using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Kronikol.Tests;

/// <summary>
/// The machine's <c>git</c> (tests skip where there is none). The history action's scripts pass git's settings through
/// <c>GIT_CONFIG_COUNT</c>, which git reads from 2.31, so an older git counts as none.
/// </summary>
internal static class GitProbe
{
    private static readonly Lazy<(string? Executable, Version? Version)> Found = new(Find);

    /// <summary>The full path of the git executable, or null.</summary>
    public static string? Executable => Found.Value.Executable;

    public static Version? Version => Found.Value.Version;

    public static bool IsAvailable => Found.Value.Executable is not null;

    private static (string?, Version?) Find()
    {
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (!File.Exists(candidate))
                continue;

            var version = VersionOf(candidate);
            return version is not null && version >= new Version(2, 31) ? (candidate, version) : (null, null);
        }

        return (null, null);
    }

    private static Version? VersionOf(string git)
    {
        try
        {
            var start = new ProcessStartInfo(git, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(start);
            if (process is null)
                return null;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            var match = Regex.Match(text, @"(\d+)\.(\d+)\.(\d+)");
            return process.ExitCode == 0 && match.Success
                ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
                : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
