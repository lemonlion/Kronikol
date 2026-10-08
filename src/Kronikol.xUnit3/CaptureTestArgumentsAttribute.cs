using System.Reflection;
using Xunit.v3;

namespace Kronikol.xUnit3;

/// <summary>
/// Captures TestMethodArguments before the test method runs.
/// xUnit3 clears these arguments after test execution, so they must be captured early
/// for report generation which happens after all tests complete.
/// </summary>
internal sealed class CaptureTestArgumentsAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        // For delay-enumerated theories ([MemberData]), args are on the Test (XunitTest).
        object[]? args = null;
        if (test is XunitTest xunitTest)
            args = xunitTest.TestMethodArguments!;

        // Fall back to TestCase for [InlineData] theories.
        if (args is null or { Length: 0 } && test.TestCase is XunitTestCase testCase)
            args = testCase.TestMethodArguments!;

        if (args is { Length: > 0 })
            DiagrammedComponentTest.CapturedTestMethodArguments[test.UniqueID] = args.ToArray();
    }

    /// <summary>
    /// Captures a test whose class xUnit will dispose through <c>DisposeAsync</c>: xUnit v3 disposes an
    /// <see cref="IAsyncDisposable"/> class that way and never calls its <see cref="IDisposable.Dispose"/>, so
    /// <see cref="DiagrammedComponentTest.Dispose"/> never ran for a class that implements <c>IAsyncLifetime</c>, and every
    /// one of its tests, passing ones included, was missing from the report. Any other class is still captured there.
    /// </summary>
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (Xunit.TestContext.Current.TestClassInstance is DiagrammedComponentTest and IAsyncDisposable)
            DiagrammedComponentTest.Capture(Xunit.TestContext.Current);
    }
}
