using System.Diagnostics;
using System.Text;

namespace Kronikol.Tests;

/// <summary>Runs small JavaScript drivers under the machine's <c>node</c> (tests skip when it is missing).</summary>
internal static class NodeProbe
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    });

    public static bool IsAvailable => Available.Value;

    /// <summary>Writes <paramref name="script"/> to a temp file and runs <c>node script args…</c>; returns stdout, throws on a non-zero exit.</summary>
    public static string Run(string script, params string[] args)
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-node-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var scriptPath = Path.Combine(dir, "driver.js");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
            var psi = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add(scriptPath);
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("node did not start");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(60_000)) { try { p.Kill(); } catch { } throw new TimeoutException("node driver timed out"); }
            if (p.ExitCode != 0) throw new InvalidOperationException($"node exited {p.ExitCode}: {stderr.Result}");
            return stdout.Result;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }
}
