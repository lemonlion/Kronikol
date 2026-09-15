using System.Diagnostics;
using NUnit.Framework;
using Kronikol.Tracking;

namespace Kronikol.NUnit4;

/// <summary>
/// Abstract base class for NUnit tests that integrates with the test tracking diagram system to capture test execution context and timing.
/// </summary>
public abstract class DiagrammedComponentTest
{
    private Stopwatch? _stopwatch;

    [SetUp]
    public void TestTrackingSetUp()
    {
        // Enable Track.That() assertions to resolve the current test ID.
        Track.TestIdResolver ??= () => TestContext.CurrentContext?.Test?.ID;
        _stopwatch = Stopwatch.StartNew();
    }

    [TearDown]
    public void TearDown()
    {
        _stopwatch?.Stop();
        if (_stopwatch is not null)
            DiagrammedTestRun.TestDurations[TestContext.CurrentContext.Test.ID] = _stopwatch.Elapsed;
        DiagrammedTestRun.TestEnds[TestContext.CurrentContext.Test.ID] = DateTimeOffset.UtcNow;
        DiagrammedTestRun.TestContexts.Enqueue(TestContext.CurrentContext);
    }
}