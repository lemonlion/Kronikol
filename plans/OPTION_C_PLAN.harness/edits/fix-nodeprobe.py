# The node probe: one correct probe per test project, every copy routed to it. usage: python fix-nodeprobe.py
R = 'C:/Code/Kronikol-v5/tests/'

def edit(path, old, new):
    s = open(R + path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (path, s.count(old))
    open(R + path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))

PROBE_OLD = '''    private static readonly Lazy<bool> Available = new(() =>
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
'''
PROBE_NEW = '''    private static readonly Lazy<bool> Available = new(() => Answers("--version", TimeSpan.FromMinutes(1)));

    /// <summary>
    /// Whether <c>node arguments</c> exits with 0 within <paramref name="wait"/>; a node still running then is stopped and
    /// does not count. A loaded machine can take seconds to start node, so the run's probe waits a minute: waiting five
    /// seconds and then reading the exit code of a process still running threw, which read as "no node", and every Node
    /// test of a busy run skipped.
    /// </summary>
    internal static bool Answers(string arguments, TimeSpan wait)
    {
        try
        {
            var psi = new ProcessStartInfo("node", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(wait))
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
    }
'''
edit('Kronikol.Tests/NodeProbe.cs', PROBE_OLD, PROBE_NEW)

edit('Kronikol.Tests/PlantUml/NodeJsPlantUmlRendererTests.cs', '''    internal static bool IsNodeAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
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
    }
''', '''    internal static bool IsNodeAvailable() => NodeProbe.IsAvailable;
''')

edit('Kronikol.Tests/PlantUml/CapturedTextEscapeTests.cs', '''    internal static bool NodeIsAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
''', '''    internal static bool NodeIsAvailable() => NodeProbe.IsAvailable;
''')

edit('Kronikol.Tests/Reports/OnDemandRenderingReportTests.cs', '''    private static readonly Lazy<bool> NodeAvailable = new(() =>
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    });
''', '''    private static readonly Lazy<bool> NodeAvailable = new(() => NodeProbe.IsAvailable);
''')

# the end-to-end project cannot see the core project's probe: one of its own, routed to by both of its copies
open(R + 'Kronikol.Tests.EndToEnd/NodeAvailability.cs', 'w', encoding='utf-8', newline='').write('''using System.Diagnostics;

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
''')
for path, old in (('Kronikol.Tests.EndToEnd/NodeJsSvgFidelityTests.cs', '''    private static readonly Lazy<bool> NodeAvailable = new(() =>
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    });
'''), ('Kronikol.Tests.EndToEnd/OnDemandRenderingTests.cs', '''    private static readonly Lazy<bool> NodeAvailable = new(() =>
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("node", "--version")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    });
''')):
    edit(path, old, '''    private static readonly Lazy<bool> NodeAvailable = new(() => NodeAvailability.IsAvailable);
''')
print('ok')
