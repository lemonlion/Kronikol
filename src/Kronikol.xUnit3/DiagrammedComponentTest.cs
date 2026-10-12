using System.Collections.Concurrent;
using Kronikol.Tracking;
using Xunit;
using Xunit.v3;

namespace Kronikol.xUnit3;

/// <summary>
/// Abstract base class for xUnit v3 tests that integrates with the test tracking diagram system to capture test execution context and timing.
/// </summary>
[Collection(DiagrammedTestCollectionName)]
[CaptureTestArguments]
public abstract class DiagrammedComponentTest : IDisposable
{
    public const string DiagrammedTestCollectionName = "Diagrammed Test Collection";

    /// <summary>
    /// Static store for test method arguments, keyed by test unique ID.
    /// xUnit3 clears TestMethodArguments after test execution,
    /// so we capture them in a BeforeAfterTestAttribute (runs after construction, before the test method).
    /// </summary>
    internal static readonly ConcurrentDictionary<string, object[]> CapturedTestMethodArguments = new();

    protected DiagrammedComponentTest()
    {
        // Enable Track.That() assertions to resolve the current test ID.
        Track.TestIdResolver ??= () => TestContext.Current.Test?.UniqueID;
    }

    public void Dispose() => Capture(TestContext.Current);

    /// <summary>Hands the test's context to the run's report; called once per test, from here or from
    /// <see cref="CaptureTestArgumentsAttribute.After"/> for a class xUnit disposes asynchronously.</summary>
    /// <param name="context">The test's context.</param>
    /// <param name="stampEnd">Whether the test ends now. Not before a <c>DisposeAsync</c> still to run: a call made there
    /// is the test's, and one after the stamped end would be read as having arrived after it.</param>
    internal static void Capture(ITestContext context, bool stampEnd = true)
    {
        if (stampEnd && context.Test?.UniqueID is { } id)
            DiagrammedTestRun.TestEnds[id] = DateTimeOffset.UtcNow;
        DiagrammedTestRun.TestContexts.Enqueue(context);
    }
}