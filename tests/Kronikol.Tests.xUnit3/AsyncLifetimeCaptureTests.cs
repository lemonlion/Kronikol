using Kronikol.xUnit3;

namespace Kronikol.Tests.xUnit3;

/// <summary>
/// A test whose class xUnit v3 disposes asynchronously is captured (plans/ADAPTER_CAPTURE_GAPS_PLAN.md R1). xUnit v3
/// disposes an <see cref="IAsyncDisposable"/> class through <c>DisposeAsync</c> and never calls its <c>Dispose</c>, where
/// <see cref="DiagrammedComponentTest"/> captured its test, so measured at 4.11.0 every test of a class that implements
/// <see cref="IAsyncLifetime"/> was missing from the report, passing ones included.
/// </summary>
public class AsyncLifetimeCaptureTests : DiagrammedComponentTest, IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static int Captured(ITestContext context) => DiagrammedTestRun.TestContexts.Count(c => ReferenceEquals(c, context));

    [Fact]
    public void After_captures_a_test_whose_class_xunit_disposes_asynchronously()
    {
        var context = TestContext.Current;
        var before = Captured(context);

        new CaptureTestArgumentsAttribute().After(typeof(AsyncLifetimeCaptureTests).GetMethod(nameof(After_captures_a_test_whose_class_xunit_disposes_asynchronously))!, null!);

        Assert.Equal(before + 1, Captured(context));
    }
}

/// <summary>A class xUnit disposes through <c>Dispose</c> is still captured there, and only there.</summary>
public class SynchronousDisposeCaptureTests : DiagrammedComponentTest
{
    [Fact]
    public void After_leaves_a_test_whose_class_is_disposed_synchronously_to_dispose()
    {
        var context = TestContext.Current;
        var before = AsyncLifetimeCaptureTests.Captured(context);

        new CaptureTestArgumentsAttribute().After(typeof(SynchronousDisposeCaptureTests).GetMethod(nameof(After_leaves_a_test_whose_class_is_disposed_synchronously_to_dispose))!, null!);

        Assert.Equal(before, AsyncLifetimeCaptureTests.Captured(context));
    }
}
