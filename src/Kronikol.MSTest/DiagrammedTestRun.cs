using System.Collections.Concurrent;
using Kronikol.Tracking;

namespace Kronikol.MSTest;

/// <summary>
/// Tracks test run lifecycle for MSTest, collecting test contexts and timing information for report generation.
/// </summary>
public class DiagrammedTestRun
{
    public static ConcurrentQueue<MSTestScenarioInfo> TestContexts { get; } = new();
    protected static DateTime StartRunTime { get; private set; }
    protected static DateTime EndRunTime { get; set; }

    protected static void Setup()
    {
        StartRunTime = DateTime.UtcNow;

        // Enable Track.That() assertions to resolve the current test ID.
        Track.TestIdResolver ??= DiagrammedComponentTest.GetCurrentTestId;
    }
}