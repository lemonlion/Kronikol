using Kronikol.Tracking;

// Track.AssertionPassedWithValues / AssertionFailedWithValues called as the weave calls them: the statement's
// text, then the names and values it captured. For shapes a woven statement cannot reach in this probe.
public static class DirectCases
{
    public static void D01_getter_that_throws() =>
        Track.AssertionPassedWithValues("actual.Fine.ShouldBe(faulty.Throws)",
            ["faulty"], [new Faulty()]);

    public static void D02_null_root() =>
        Track.AssertionFailedWithValues("actual.Id.ShouldBe(expected.Id)",
            "Object reference not set to an instance of an object.", ["expected"], [null]);

    public static void D03_null_in_the_middle() =>
        Track.AssertionFailedWithValues("actual.Id.ShouldBe(expected.Inner.Value)",
            "Object reference not set to an instance of an object.", ["expected"], [Data.Expected() with { Inner = null }]);

    public static void D04_TUnit_property() =>
        Track.AssertionPassedWithValues("Assert.That(actual.Id).IsEqualTo(expected.Id)",
            ["expected"], [Data.Expected()]);

    public static void D05_TUnit_ToString() =>
        Track.AssertionPassedWithValues("Assert.That(actual.Colour.ToString()).IsEqualTo(expected.Colour.ToString())",
            ["expected"], [Data.Expected()]);

    public static void D06_root_then_indexer() =>
        Track.AssertionPassedWithValues("actual.Id.ShouldBe(ids[0])",
            ["ids"], [new[] { "A1" }]);

    public static void D07_root_then_call() =>
        Track.AssertionPassedWithValues("actual.Id.ShouldBe(id.Trim())",
            ["id"], ["A1"]);

    public static void D08_FluentAssertions_ToString() =>
        Track.AssertionPassedWithValues("actual.Colour.ToString().Should().Be(expected.Colour.ToString())",
            ["expected"], [Data.Expected()]);

    public static void D09_unprintable_root_then_call() =>
        Track.AssertionPassedWithValues("actual.Name.ShouldBe(plain.Name.ToUpper())",
            ["plain"], [new Plain()]);

    public static void D10_value_holding_to_string() =>
        Track.AssertionPassedWithValues("actual.Note.Should().Be(note)",
            ["note"], ["call to string() now"]);
}
