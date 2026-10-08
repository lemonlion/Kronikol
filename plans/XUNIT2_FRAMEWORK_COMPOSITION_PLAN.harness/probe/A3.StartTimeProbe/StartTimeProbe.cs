using System.Reflection;
using System.Runtime.CompilerServices;
using Kronikol.xUnit2;

namespace Probe;

/// <summary>
/// Reads ReportLifecycle's private StartTime with full precision once the run is over (ProcessExit, after
/// ReportingTestFrameworkExecutor has written the reports), so nothing touches ReportLifecycle earlier than A does.
/// Writes starttime-probe.txt in the test output: when this module was first used (the first test touching it),
/// the StartTime the report was given, and when the process exited.
/// </summary>
internal static class StartTimeProbe
{
    [ModuleInitializer]
    internal static void Init()
    {
        var firstUse = DateTime.UtcNow;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            var field = typeof(ReportLifecycle).GetField("StartTime", BindingFlags.NonPublic | BindingFlags.Static);
            var start = field?.GetValue(null) as DateTime?;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "starttime-probe.txt"),
                $"moduleFirstUsedUtc={firstUse:O}{Environment.NewLine}" +
                $"reportLifecycleStartTimeUtc={(start is { } s ? s.ToString("O") : "field not found")}{Environment.NewLine}" +
                $"processExitUtc={DateTime.UtcNow:O}{Environment.NewLine}");
        };
    }
}
