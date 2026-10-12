using Kronikol.Tracking;
using Shouldly;

// Track.That: the lambda's closure is read by ClosureValueResolver. The type is left out of the weave so each
// case draws Track.That's note alone.
[SuppressAssertionTracking]
public static class ThatCases
{
    public static void T01_property()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Track.That(() => actual.Id.ShouldBe(expected.Id));
    }

    public static void T03_ToString()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Track.That(() => actual.Colour.ToString().ShouldBe(expected.Colour.ToString()));
    }

    public static void T04_First()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Track.That(() => actual.Lines.First().ShouldBe(expected.Lines.First()));
    }

    public static void T08_Count_with_predicate()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Track.That(() => actual.Lines.Count(l => l == "x").ShouldBe(expected.Lines.Count(l => l == "x")));
    }

    public static void T09_indexer()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Track.That(() => actual.Lines[0].ShouldBe(expected.Lines[0]));
    }

    public static void T15_null_conditional_on_a_null()
    {
        var expected = Data.Expected() with { Inner = null };
        string? value = null;
        Track.That(() => value.ShouldBe(expected.Inner?.Value));
    }

    public static void T16_unprintable_property()
    {
        var expected = Data.Expected();
        var plain = expected.Plain;
        Track.That(() => plain.ShouldBeSameAs(expected.Plain));
    }

    public static void T17_four_segments()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Track.That(() => actual.Box.Inner.Value.Length.ShouldBe(expected.Box.Inner.Value.Length));
    }

    public static void T19_nameof()
    {
        var expected = Data.Expected();
        Track.That(() => "Colour".ShouldBe(nameof(expected.Colour)));
    }

    public static void T22_arithmetic()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        Track.That(() => actual.Code.ShouldBe(expected.Code + 0));
    }
}
