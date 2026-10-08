using System.Collections.Concurrent;
using System.Reflection;

namespace Kronikol.xUnit2;

/// <summary>
/// Provides the current test's identity via AsyncLocal for Kronikol.
/// In xUnit v2, there is no <c>TestContext.Current</c>, so this class uses
/// <see cref="AsyncLocal{T}"/> to store the current test's name and ID.
/// The <see cref="TestTrackingAttribute"/> sets and clears this context
/// before and after each test.
/// </summary>
public static class XUnit2TestTrackingContext
{
    private static readonly AsyncLocal<(string Name, string Id)?> CurrentTest = new();

    /// <summary>
    /// The scenario Kronikol's results sink made for the test running on this flow, set when xUnit sent its
    /// <c>ITestStarting</c>. Only a bus that delivers on the test's own flow (synchronous message reporting) gets it
    /// here, before the test's constructor; it is gone once the test's runner returns.
    /// </summary>
    private static readonly AsyncLocal<HandedOverScenario?> HandedOver = new();

    internal static readonly ConcurrentDictionary<string, ScenarioInfo> CollectedScenarios = new();

    private static long _sequence;

    internal static long NextSequence() => Interlocked.Increment(ref _sequence);

    /// <summary>The last sequence number given out, so a run can tell the scenarios made since it started.</summary>
    internal static long CurrentSequence => Interlocked.Read(ref _sequence);

    public static (string Name, string Id) GetCurrentTestInfo() =>
        CurrentTest.Value ?? ("Unknown Test", Guid.NewGuid().ToString());

    internal static void SetCurrentTest(string name, string id) =>
        CurrentTest.Value = (name, id);

    internal static void ClearCurrentTest() =>
        CurrentTest.Value = null;

    internal static void UpdateResult(string testId, Reports.ExecutionResult result, string? errorMessage = null, string? errorStackTrace = null)
    {
        if (CollectedScenarios.TryGetValue(testId, out var info))
        {
            info.Result = result;
            info.ErrorMessage = errorMessage;
            info.ErrorStackTrace = errorStackTrace;
        }
    }

    internal static ScenarioInfo[] GetAllScenarios() => CollectedScenarios.Values.ToArray();

    internal static void HandOver(ScenarioInfo scenario, MethodInfo method) =>
        HandedOver.Value = new HandedOverScenario(scenario, method);

    /// <summary>
    /// The scenario handed over for <paramref name="methodUnderTest"/>, or null when none was, or when the one on
    /// this flow is for another method. Compared by metadata token, so a generic method's constructed form and a
    /// method reflected through a derived class still match.
    /// </summary>
    internal static ScenarioInfo? HandedOverScenarioFor(MethodInfo methodUnderTest) =>
        HandedOver.Value is { } handedOver
        && handedOver.Method.MetadataToken == methodUnderTest.MetadataToken
        && handedOver.Method.Module == methodUnderTest.Module
            ? handedOver.Scenario
            : null;

    private sealed record HandedOverScenario(ScenarioInfo Scenario, MethodInfo Method);
}
