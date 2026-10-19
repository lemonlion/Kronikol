using System.Diagnostics;
using System.Text;

namespace Kronikol.Tests;

/// <summary>
/// Runs the skill's <c>scripts/query.py</c> fallback under whatever Python the machine has (tests skip
/// when there is none).
///
/// <para><b>Both names, deliberately.</b> On a Windows developer machine <c>python</c> is the real
/// interpreter and <c>python3</c> is the App Execution Alias stub, which exits non-zero; on the
/// ubuntu-latest CI runner only <c>python3</c> exists. A probe that knows one name skips the whole smoke
/// test on half the machines it runs on, and a skipped test that nobody notices is indistinguishable from
/// one that never worked.</para>
/// </summary>
internal static class PythonProbe
{
    private static readonly Lazy<string?> Interpreter = new(() =>
        new[] { "python3", "python" }.FirstOrDefault(candidate => Answers(candidate, "--version", TimeSpan.FromMinutes(1))));

    /// <summary>
    /// Whether <c>candidate arguments</c> exits with 0 within <paramref name="wait"/>; an interpreter still running then is
    /// stopped and does not count, nor does a name that is not on PATH. A loaded machine can take seconds to start one, so
    /// the run's probe waits a minute: waiting five seconds and then reading the exit code of a process still running
    /// threw, which read as "no python", and every script fact of a busy run skipped.
    /// </summary>
    internal static bool Answers(string candidate, string arguments, TimeSpan wait)
    {
        try
        {
            var psi = new ProcessStartInfo(candidate, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var probe = Process.Start(psi);
            if (probe is null)
                return false;
            _ = probe.StandardOutput.ReadToEndAsync();
            _ = probe.StandardError.ReadToEndAsync();
            if (!probe.WaitForExit(wait))
            {
                try { probe.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return probe.ExitCode == 0;
        }
        catch
        {
            // Not on PATH under this name.
            return false;
        }
    }

    /// <summary>The interpreter's name on this machine, <c>python3</c> or <c>python</c>, or null.</summary>
    internal static string? Name => Interpreter.Value;

    public static bool IsAvailable => Interpreter.Value is not null;

    /// <summary>Runs <c>python query.py args…</c> and returns stdout; throws on a non-zero exit.</summary>
    public static string Run(string scriptPath, params string[] args)
    {
        var (stdout, stderr, exit) = RunFull(scriptPath, args);
        if (exit != 0) throw new InvalidOperationException($"query.py exited {exit}: {stderr}");
        return stdout;
    }

    /// <summary>
    /// The same run, handing back the exit code instead of throwing on it - which is the only way to
    /// assert that the script REFUSES something, and refusing well is half of what it has to do.
    /// </summary>
    public static (string Output, string Error, int Exit) RunFull(string scriptPath, params string[] args) =>
        RunFull(scriptPath, utf8Override: true, args);

    /// <summary>
    /// The same run with the interpreter's own choice of stdout encoding, as a user's shell pipe gets it: on Windows the
    /// console's code page, which the override above hides.
    /// </summary>
    public static (string Output, string Error, int Exit) RunFullAsPiped(string scriptPath, params string[] args) =>
        RunFull(scriptPath, utf8Override: false, args);

    private static (string Output, string Error, int Exit) RunFull(string scriptPath, bool utf8Override, string[] args)
    {
        var psi = new ProcessStartInfo(Interpreter.Value ?? throw new InvalidOperationException("no python"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(scriptPath);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        // The script prints box-drawing and arrow characters; without this the child encodes them in the
        // console code page and the comparison fails for a reason that has nothing to do with the answer.
        if (utf8Override)
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
        else
            psi.Environment.Remove("PYTHONIOENCODING");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("python did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { try { process.Kill(); } catch { } throw new TimeoutException("query.py timed out"); }
        return (stdout.Result, stderr.Result, process.ExitCode);
    }
}
