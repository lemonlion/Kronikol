using System.Diagnostics;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Whether the machine's <c>node</c> runs, decided once for the run. A loaded machine can take seconds to start node, so
/// the probe waits a minute and stops a node that never answers: waiting five seconds and then reading the exit code of
/// a process still running threw, which read as "no node", and the run's Node tests skipped (the core suite's
/// NodeProbe, 2026-10-19).
/// </summary>
internal static class NodeAvailability
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return false;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(TimeSpan.FromMinutes(1)))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });

    public static bool IsAvailable => Available.Value;
}
