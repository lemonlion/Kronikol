using System.Collections.Concurrent;
using System.Reflection;
using Kronikol.Tracking;

namespace Kronikol.xUnit2;

/// <summary>
/// Provides the current test's identity via AsyncLocal for Kronikol.
/// In xUnit v2, there is no <c>TestContext.Current</c>, so this class uses
/// <see cref="AsyncLocal{T}"/> to store the current test's name and ID.
/// The <see cref="TestTrackingAttribute"/> sets and clears this context
/// before and after each test.
/// <para>
/// The identity covers what runs between the attribute's <c>Before</c> and <c>After</c>: the test method, and work it
/// starts that flows its execution context. xUnit v2 runs a test class's constructor and <c>IAsyncLifetime.InitializeAsync</c>
/// before <c>Before</c>, and <c>DisposeAsync</c> and <c>Dispose</c> after <c>After</c>, so they run outside the test, as
/// fixtures do.
/// </para>
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

    /// <summary>
    /// The name and id of the test running on this flow. Outside a test it answers <c>"Unknown Test"</c> and a new random
    /// id, a different one on every call, which no scenario owns, so a call logged under it appears in no report.
    /// <para>
    /// Kronikol's own readers do not use it: <see cref="CurrentTestInfo.Fetcher"/> throws outside a test, and
    /// <see cref="Track.TestIdResolver"/> and <see cref="TrackingDiagramOverride"/> answer no test, so the call goes to a
    /// <see cref="TestIdentityScope"/>, the global fallback or the background, as on the other frameworks. To log a call
    /// yourself, take its identity from <c>TestInfoResolver.Resolve(null, CurrentTestInfo.Fetcher)</c> and skip the call
    /// when that is null.
    /// </para>
    /// </summary>
    public static (string Name, string Id) GetCurrentTestInfo() =>
        CurrentTest.Value ?? ("Unknown Test", Guid.NewGuid().ToString());

    /// <summary>The test running on this flow, or null outside a test.</summary>
    internal static (string Name, string Id)? Current => CurrentTest.Value;

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
