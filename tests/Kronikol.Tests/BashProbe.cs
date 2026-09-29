using System.Diagnostics;

namespace Kronikol.Tests;

/// <summary>
/// The bash a GitHub runner runs a <c>shell: bash</c> step with (tests skip where there is none).
///
/// <para><b>On Windows that is Git for Windows' <c>bin\bash.exe</c>,</b> as the runner documents, and never the first
/// <c>bash</c> on <c>PATH</c>: <c>System32\bash.exe</c> is WSL's launcher, which would run the script in another
/// operating system. Git's launcher puts <c>/mingw64/bin</c> and <c>/usr/bin</c> ahead of the <c>PATH</c> it is given,
/// on a hosted runner too, so a stub named <c>git</c> or <c>curl</c> never shadows Git's own there (measured
/// 2026-09-29); a stub named <c>dotnet</c> does.</para>
/// </summary>
internal static class BashProbe
{
    private static readonly Lazy<string?> Found = new(Find);

    /// <summary>The full path of the bash to run, or null.</summary>
    public static string? Executable => Found.Value;

    public static bool IsAvailable => Found.Value is not null;

    private static string? Find()
    {
        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate) && Works(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach (var onPath in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                yield return Path.Combine(onPath, "bash");
            yield break;
        }

        // git.exe sits in <root>\cmd, <root>\bin or <root>\mingw64\bin; the launcher is <root>\bin\bash.exe.
        if (GitProbe.Executable is { } git && Path.GetDirectoryName(git) is { } gitDirectory)
        {
            yield return Path.GetFullPath(Path.Combine(gitDirectory, "..", "bin", "bash.exe"));
            yield return Path.GetFullPath(Path.Combine(gitDirectory, "..", "..", "bin", "bash.exe"));
            yield return Path.Combine(gitDirectory, "bash.exe");
        }

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
    }

    private static bool Works(string bash)
    {
        try
        {
            var start = new ProcessStartInfo(bash)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--noprofile");
            start.ArgumentList.Add("--norc");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("echo \"$BASH_VERSION\"");
            using var process = Process.Start(start);
            if (process is null)
                return false;
            process.StandardInput.Close();
            var version = process.StandardOutput.ReadToEnd();
            return process.WaitForExit(20_000) && process.ExitCode == 0 && version.Trim().Length > 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
