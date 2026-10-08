using Kronikol.Tracking;

namespace Kronikol.NUnit4;

/// <summary>
/// Allows manual override of diagram generation settings for specific NUnit tests.
/// <para>
/// Outside a test (a set-up fixture, a fixture's constructor or one-time set-up or tear-down, or a thread the test's
/// execution context did not reach) each call goes to the test that a <see cref="TestIdentityScope"/> or the global
/// fallback names, and does nothing when neither names one.
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

    public static void InsertPlantUml(string plantUml)
    {
        var testId = GetTestId();
        if (testId is null) return;
        DefaultTrackingDiagramOverride.InsertPlantUml(testId, plantUml);
    }

    public static void InsertTestDelimiter(string testIdentifier)
    {
        var testId = GetTestId();
        if (testId is null) return;
        DefaultTrackingDiagramOverride.InsertTestDelimiter(testId, testIdentifier);
    }

    public static void StartAction()
    {
        var testId = GetTestId();
        if (testId is null) return;
        DefaultTrackingDiagramOverride.StartAction(testId);
    }

    public static void StartSetup()
    {
        var testId = GetTestId();
        if (testId is null) return;
        DefaultTrackingDiagramOverride.StartSetup(testId);
    }

    private static string? GetTestId() =>
        RunningTest.Current?.ID
        ?? (TestIdentityScope.Current ?? TestIdentityScope.GlobalFallback)?.Id;
}
