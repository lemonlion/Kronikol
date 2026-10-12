using Kronikol.Tracking;

namespace Kronikol.xUnit2;

/// <summary>
/// Allows manual override of diagram generation settings for specific xUnit v2 tests.
/// <para>
/// Outside a test (a test class's constructor, <c>InitializeAsync</c>, <c>DisposeAsync</c> or <c>Dispose</c>, a
/// fixture, or a thread the test's execution context did not reach) each marker goes to the test that a
/// <see cref="TestIdentityScope"/> or the global fallback names, and is dropped when neither names one.
/// <see cref="StartSetup"/> and <see cref="StartAction"/> set the phase there either way: the phase belongs to the
/// flow, not to a test, and the calls made later on that flow carry it. xUnit v2 runs a test on the flow its
/// constructor and <c>InitializeAsync</c> ran on, so a phase set there is the phase the test's calls carry.
/// </para>
/// </summary>
public static class TrackingDiagramOverride
{
    public static void StartOverride(string? plantUml = null)
    {
        var testId = GetTestId();
        if (testId is null) return;
        DefaultTrackingDiagramOverride.StartOverride(testId, plantUml);
    }

    public static void EndOverride(string? plantUml = null)
    {
        var testId = GetTestId();
        if (testId is null) return;
        DefaultTrackingDiagramOverride.EndOverride(testId, plantUml);
    }

    public static void InsertTestDelimiter(string testIdentifier)
    {
        var testId = GetTestId();
        if (testId is null) return;
        DefaultTrackingDiagramOverride.InsertTestDelimiter(testId, testIdentifier);
    }

    public static void InsertPlantUml(string plantUml)
    {
        var testId = GetTestId();
        if (testId is null) return;
        DefaultTrackingDiagramOverride.InsertPlantUml(testId, plantUml);
    }

    public static void StartAction()
    {
        var testId = GetTestId();
        if (testId is null)
        {
            // The phase belongs to the flow, not to a test, so it is set here too; only the marker needs a test.
            TestPhaseContext.Current = TestPhase.Action;
            return;
        }
        DefaultTrackingDiagramOverride.StartAction(testId);
    }

    public static void StartSetup()
    {
        var testId = GetTestId();
        if (testId is null)
        {
            // The phase belongs to the flow, not to a test, so it is set here too.
            TestPhaseContext.Current = TestPhase.Setup;
            return;
        }
        DefaultTrackingDiagramOverride.StartSetup(testId);
    }

    private static string? GetTestId() =>
        XUnit2TestTrackingContext.Current?.Id
        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;
}
