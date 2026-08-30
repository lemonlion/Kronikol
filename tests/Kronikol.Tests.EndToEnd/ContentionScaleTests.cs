namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The arithmetic behind the render-perf guard's contention-scaled budgets (no browser involved).
/// A calibration worker spins a fixed CPU loop throughout the measurement; how much longer that loop
/// takes than its uncontended reference is exactly how much longer everything else on the machine is
/// taking, so the absolute budgets scale by it instead of false-failing on a saturated runner.
/// </summary>
public class ContentionScaleTests
{
    [Fact]
    public void Quiet_machine_leaves_budgets_unscaled()
    {
        // At or under the reference: never tighten budgets below their calibrated values.
        Assert.Equal(1.0, ContentionScale.Stretch([18.0, 19.0, 17.5]));
        Assert.Equal(1.0, ContentionScale.Stretch([5.0, 5.0, 5.0]));
    }

    [Fact]
    public void Saturated_machine_scales_budgets_by_the_measured_slowdown()
    {
        // Median 60 ms against the 20 ms reference: everything runs 3x slower, budgets get 3x.
        Assert.Equal(3.0, ContentionScale.Stretch([60.0, 60.0, 60.0]));
    }

    [Fact]
    public void Stretch_uses_the_median_not_the_mean()
    {
        // One monster outlier (a GC, a snapshot) must not scale the budgets on its own.
        Assert.Equal(1.0, ContentionScale.Stretch([18.0, 19.0, 2000.0]));
    }

    [Fact]
    public void Stretch_is_capped()
    {
        // Beyond the cap the numbers carry no signal either way; the cap keeps a genuine multi-second
        // regression from being scaled into passing on a wedged runner.
        Assert.Equal(ContentionScale.Cap, ContentionScale.Stretch([2000.0, 2000.0, 2000.0]));
    }

    [Fact]
    public void No_samples_means_no_scaling()
    {
        // Probe unavailable (worker or blob URL blocked): behave exactly as before the probe existed.
        Assert.Equal(1.0, ContentionScale.Stretch([]));
    }
}
