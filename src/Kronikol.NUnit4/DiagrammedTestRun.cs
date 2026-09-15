using System.Collections.Concurrent;
using NUnit.Framework;
using Kronikol.Tracking;

namespace Kronikol.NUnit4;

/// <summary>
/// Tracks test run lifecycle for NUnit, collecting test contexts and timing information for report generation.
/// </summary>
public class DiagrammedTestRun
{
    public static ConcurrentQueue<TestContext> TestContexts { get; } = new();
    public static ConcurrentDictionary<string, TimeSpan> TestDurations { get; } = new();

    /// <summary>When each test finished, by NUnit test id, stamped in the tear-down that collects it.</summary>
    public static ConcurrentDictionary<string, DateTimeOffset> TestEnds { get; } = new();
    protected static DateTime StartRunTime { get; private set; }
    protected static DateTime EndRunTime { get; set; }

    protected static void Setup()
    {
        StartRunTime = DateTime.UtcNow;

        // Enable Track.That() assertions to resolve the current test ID.
        Track.TestIdResolver ??= () => TestContext.CurrentContext?.Test?.ID;
    }
}