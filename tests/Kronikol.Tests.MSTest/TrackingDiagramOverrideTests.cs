using Kronikol.MSTest;
using Kronikol.Tracking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kronikol.Tests.MSTest;

// Outside a test (an assembly or class initializer, a thread the test's execution context did not reach) the override threw a
// NullReferenceException, inside a TestIdentityScope or with the global fallback set too. It now goes to the test the scope or
// the fallback names, and does nothing when neither names one, as the xUnit v3 override has since 2.31.6 (#49).
[TestClass]
[DoNotParallelize]
public class TrackingDiagramOverrideTests
{
    private static readonly string ClassInitializeMarker = "marker-" + Guid.NewGuid().ToString("N");
    private static Exception? _thrownInClassInitialize;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        try
        {
            TrackingDiagramOverride.InsertPlantUml(ClassInitializeMarker);
        }
        catch (Exception e)
        {
            _thrownInClassInitialize = e;
        }
    }

    [TestMethod]
    public void In_a_class_initializer_a_marker_is_dropped()
    {
        Assert.IsNull(_thrownInClassInitialize);
        Assert.IsEmpty(OutsideATest.MarkersHolding(ClassInitializeMarker));
    }

    [TestMethod]
    public void On_a_thread_the_test_did_not_reach_a_marker_goes_to_a_scope_begun_there()
    {
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        OutsideATest.OnThreadWithNoFlow(() =>
        {
            using (TestIdentityScope.Begin("Scoped", "scoped-id"))
                TrackingDiagramOverride.InsertPlantUml(marker);
        });

        CollectionAssert.AreEqual(new[] { "scoped-id" }, OutsideATest.MarkersHolding(marker).Select(l => l.TestId).Distinct().ToArray());
    }

    [TestMethod]
    public void On_a_thread_the_test_did_not_reach_a_marker_goes_to_the_global_fallback()
    {
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        TestIdentityScope.SetGlobalFallback("Fallback", "fallback-id");
        try
        {
            OutsideATest.OnThreadWithNoFlow(() => TrackingDiagramOverride.InsertPlantUml(marker));
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }

        CollectionAssert.AreEqual(new[] { "fallback-id" }, OutsideATest.MarkersHolding(marker).Select(l => l.TestId).Distinct().ToArray());
    }

    [TestMethod]
    public void On_a_thread_the_test_did_not_reach_with_nothing_else_a_marker_is_dropped()
    {
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        OutsideATest.OnThreadWithNoFlow(() => TrackingDiagramOverride.InsertPlantUml(marker));

        Assert.IsEmpty(OutsideATest.MarkersHolding(marker));
    }

    [TestMethod]
    public void Inside_the_test_a_marker_goes_to_the_test()
    {
        // Opens the test's identity as DiagrammedComponentTest's [TestInitialize] does, without its [TestCleanup], which
        // would queue a scenario that DiagrammedTestRunTests reads.
        new TrackedTest { TestContext = TestContext }.TestTrackingInitialize();
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        TrackingDiagramOverride.InsertPlantUml(marker);

        CollectionAssert.AreEqual(
            new[] { $"{TestContext.FullyQualifiedTestClassName}.{TestContext.TestName}" },
            OutsideATest.MarkersHolding(marker).Select(l => l.TestId).Distinct().ToArray());
    }

    private sealed class TrackedTest : DiagrammedComponentTest;
}

internal static class OutsideATest
{
    /// <summary>
    /// Runs <paramref name="action"/> on a new thread that inherits no execution context, so nothing the test set in an
    /// <see cref="AsyncLocal{T}"/> reaches it.
    /// </summary>
    public static void OnThreadWithNoFlow(Action action)
    {
        Exception? failure = null;
        Thread thread;
        using (ExecutionContext.SuppressFlow())
        {
            thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            thread.Start();
        }

        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The action failed on its thread.", failure);
    }

    public static RequestResponseLog[] MarkersHolding(string text) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.PlantUml?.Contains(text, StringComparison.Ordinal) == true).ToArray();
}
