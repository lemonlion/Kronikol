using FluentAssertions;

// FluentAssertions 6.12.1, the version in the issue's suite.
public static class WovenFluent
{
    public static void F01_property()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.Should().Be(expected.Id);
    }

    public static void F02_ToString()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Colour.ToString().Should().Be(expected.Colour.ToString());
    }

    public static void F03_First()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines.First().Should().Be(expected.Lines.First());
    }

    public static void F04_unprintable_property()
    {
        var expected = Data.Expected();
        var plain = expected.Plain;
        plain.Should().BeSameAs(expected.Plain);
    }

    public static void F05_Count_with_predicate()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines.Count(l => l == "x").Should().Be(expected.Lines.Count(l => l == "x"));
    }

    public static void F06_failing_ToString()
    {
        var expected = Data.Expected();
        var actual = Data.Actual() with { Colour = Colour.Red };
        actual.Colour.ToString().Should().Be(expected.Colour.ToString());
    }

    public static void F07_indexer()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines[0].Should().Be(expected.Lines[0]);
    }

    public static void F08_null_conditional_on_a_null()
    {
        var expected = Data.Expected() with { Inner = null };
        string? value = null;
        value.Should().Be(expected.Inner?.Value);
    }

    public static void F09_coalesce_argument()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.Should().Be(expected.Missing ?? expected.Id);
    }
}
