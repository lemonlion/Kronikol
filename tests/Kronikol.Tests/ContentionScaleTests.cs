namespace Kronikol.Tests;

public class ContentionScaleTests
{
    [Fact]
    public void An_idle_machine_does_not_stretch_a_budget()
    {
        Assert.Equal(1.0, ContentionScale.Stretch([ContentionScale.ReferenceMs * 0.8, ContentionScale.ReferenceMs, ContentionScale.ReferenceMs * 0.9]));
    }

    [Fact]
    public void A_busy_machine_stretches_a_budget_by_the_probe_median_over_the_reference()
    {
        var reference = ContentionScale.ReferenceMs;

        Assert.Equal(3.0, ContentionScale.Stretch([reference, reference * 3, reference * 3, reference * 3, reference * 40]), 9);
    }

    [Fact]
    public void A_stretch_stops_at_the_cap()
    {
        Assert.Equal(ContentionScale.Cap, ContentionScale.Stretch([ContentionScale.ReferenceMs * 100]));
    }

    [Fact]
    public void No_samples_stretch_nothing()
    {
        Assert.Equal(1.0, ContentionScale.Stretch([]));
    }

    [Fact]
    public void Measuring_gives_a_stretch_between_one_and_the_cap()
    {
        Assert.InRange(ContentionScale.Measure(), 1.0, ContentionScale.Cap);
    }
}
