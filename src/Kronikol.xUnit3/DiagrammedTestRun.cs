using System.Collections.Concurrent;
using Kronikol.Tracking;
using Xunit;

namespace Kronikol.xUnit3;
/// <summary>
/// Tracks test run lifecycle for xUnit v3, collecting test contexts and timing information for report generation.
/// </summary>
public class DiagrammedTestRun
{
    public static ConcurrentQueue<ITestContext> TestContexts { get; } = new();

    /// <summary>
    /// When each test finished, by unique id, stamped as its test class is disposed through <c>Dispose</c>. xunit.v3
    /// keeps the finish time on its result messages, not on the context the report is built from. A test whose class
    /// xunit.v3 disposes through <c>DisposeAsync</c> has none: that runs after the last hook Kronikol has, and a call made
    /// in it is still the test's.
    /// </summary>
    public static ConcurrentDictionary<string, DateTimeOffset> TestEnds { get; } = new();
    protected static DateTime StartRunTime { get; private set; }
    protected static DateTime EndRunTime { get; set; }

    public DiagrammedTestRun()
    {
        StartRunTime = DateTime.UtcNow;

        // Enable Track.That() assertions to resolve the current test ID.
        Track.TestIdResolver ??= () => TestContext.Current.Test?.UniqueID;
    }
}