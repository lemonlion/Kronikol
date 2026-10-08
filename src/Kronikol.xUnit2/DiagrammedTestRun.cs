using Kronikol.Tracking;

namespace Kronikol.xUnit2;

/// <summary>
/// Base class for a collection fixture that captures the test run start time.
/// Create a subclass and register it with <c>ICollectionFixture&lt;YourTestRun&gt;</c>.
/// Generate reports in your subclass's <c>Dispose()</c> method using
/// <see cref="XUnit2ReportGenerator.CreateStandardReportsWithDiagrams(DateTime, DateTime, ReportConfigurationOptions)"/>.
/// <para>
/// <b>Note:</b> When using the <see cref="ReportingTestFramework"/> (recommended),
/// reports are generated automatically after all tests complete, and this class
/// is not strictly required. It remains useful for starting/stopping HTTP fakes
/// and other shared test resources.
/// </para>
/// <para>
/// Reports written from <c>Dispose</c> carry no test's result: xUnit v2 shows results only to the test
/// framework. Every scenario is reported as passed and marked as a default rather than a verdict, and the
/// specifications are written blank.
/// </para>
/// </summary>
public class DiagrammedTestRun
{
    protected static DateTime StartRunTime { get; private set; }
    protected static DateTime EndRunTime { get; set; }

    public DiagrammedTestRun()
    {
        StartRunTime = DateTime.UtcNow;

        // Enable Track.That() assertions to resolve the current test ID. Outside a test it answers null, and Track goes on
        // to the scope and the global fallback.
        Track.TestIdResolver ??= () => XUnit2TestTrackingContext.Current?.Id;
    }
}
