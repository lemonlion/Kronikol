using Kronikol.Tracking;
using TrackingDiagramOverride = Kronikol.TUnit.TrackingDiagramOverride;

namespace Kronikol.Tests.TUnit;

// These run under xUnit v3, where TUnit's TestContext.Current is null, as it is on a thread a TUnit test's execution context
// did not reach. The override threw a NullReferenceException there, inside a TestIdentityScope or with the global fallback set
// too. It now goes to the test the scope or the fallback names, and does nothing when neither names one, as the xUnit v3
// override has since 2.31.6 (#49). The facts share the global fallback, so they stay in one class, which xUnit runs in order.
public class TrackingDiagramOverrideTests
{
    [Fact]
    public void With_no_TUnit_test_a_marker_goes_to_a_scope_begun_there()
    {
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        using (TestIdentityScope.Begin("Scoped", "scoped-id"))
            TrackingDiagramOverride.InsertPlantUml(marker);

        Assert.Equal(["scoped-id"], MarkersHolding(marker).Select(l => l.TestId).Distinct());
    }

    [Fact]
    public void With_no_TUnit_test_a_marker_goes_to_the_global_fallback()
    {
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        TestIdentityScope.SetGlobalFallback("Fallback", "fallback-id");
        try
        {
            TrackingDiagramOverride.InsertPlantUml(marker);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }

        Assert.Equal(["fallback-id"], MarkersHolding(marker).Select(l => l.TestId).Distinct());
    }

    [Fact]
    public void With_no_TUnit_test_and_nothing_else_a_marker_is_dropped()
    {
        var marker = "marker-" + Guid.NewGuid().ToString("N");

        TrackingDiagramOverride.InsertPlantUml(marker);
        TrackingDiagramOverride.StartAction();

        Assert.Empty(MarkersHolding(marker));
    }

    private static RequestResponseLog[] MarkersHolding(string text) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.PlantUml?.Contains(text, StringComparison.Ordinal) == true).ToArray();
}
