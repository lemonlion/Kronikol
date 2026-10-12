using Shouldly;

// One statement per line, as real tests are: the weave reads a statement's text from its sequence point.
public static class WovenShouldly
{
    private static readonly Order StaticExpected = Data.Expected();

    public static void S01_property()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.ShouldBe(expected.Id);
    }

    public static void S02_enum_property()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Colour.ShouldBe(expected.Colour);
    }

    public static void S03_ToString()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Colour.ToString().ShouldBe(expected.Colour.ToString());
    }

    public static void S04_First()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines.First().ShouldBe(expected.Lines.First());
    }

    public static void S05_two_calls()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Colour.ToString().ToLowerInvariant().ShouldBe(expected.Colour.ToString().ToLowerInvariant());
    }

    public static void S06_Count_property()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines.Count.ShouldBe(expected.Lines.Count);
    }

    public static void S07_Count_method()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines.Count().ShouldBe(expected.Lines.Count());
    }

    public static void S08_Count_with_predicate()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines.Count(l => l == "x").ShouldBe(expected.Lines.Count(l => l == "x"));
    }

    public static void S09_indexer()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines[0].ShouldBe(expected.Lines[0]);
    }

    public static void S10_call_then_property()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Lines.First().Length.ShouldBe(expected.Lines.First().Length);
    }

    public static void S11_call_on_a_one_segment_path()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.ShouldBe(expected.Id.ToString());
    }

    public static void S12_call_with_arguments()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Colour.ToString("G").ShouldBe(expected.Colour.ToString("G"));
    }

    public static void S13_generic_method()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Get<int>().ShouldBe(expected.Get<int>());
    }

    public static void S14_null_conditional()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        var value = actual.Inner?.Value;
        value.ShouldBe(expected.Inner?.Value);
    }

    public static void S15_null_conditional_on_a_null()
    {
        var expected = Data.Expected() with { Inner = null };
        string? value = null;
        value.ShouldBe(expected.Inner?.Value);
    }

    public static void S16_unprintable_property()
    {
        var expected = Data.Expected();
        var plain = expected.Plain;
        plain.ShouldBeSameAs(expected.Plain);
    }

    public static void S17_four_segments()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Box.Inner.Value.Length.ShouldBe(expected.Box.Inner.Value.Length);
    }

    public static void S18_null_forgiving()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Inner!.Value.ShouldBe(expected.Inner!.Value);
    }

    public static void S19_nameof()
    {
        var expected = Data.Expected();
        "Colour".ShouldBe(nameof(expected.Colour));
    }

    public static void S20_interpolated()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.ShouldBe($"{expected.Id}");
    }

    public static void S21_field()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Field.ShouldBe(expected.Field);
    }

    public static void S22_arithmetic()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Code.ShouldBe(expected.Code + 0);
    }

    public static void S23_null_property()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Missing.ShouldBe(expected.Missing);
    }

    public static void S24_issue_SplitId()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        var splits = new List<Order> { expected };
        actual.Id.ShouldBe(splits.First().Id);
    }

    public static void S25_failing_ToString()
    {
        var expected = Data.Expected();
        var actual = Data.Actual() with { Colour = Colour.Red };
        actual.Colour.ToString().ShouldBe(expected.Colour.ToString());
    }

    public static void S26_ShouldSatisfyAllConditions()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.ShouldSatisfyAllConditions(
            () => actual.Colour.ToString().ShouldBe(expected.Colour.ToString()),
            () => actual.Id.ShouldBe(expected.Id));
    }

    public static void S27_value_inside_a_call()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.ShouldBe(string.Concat(expected.Id));
    }

    public static void S28_static_field()
    {
        var actual = Data.Actual();
        actual.Colour.ToString().ShouldBe(StaticExpected.Colour.ToString());
    }

    public static void S29_local_holding_the_value()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        var colour = expected.Colour.ToString();
        actual.Colour.ToString().ShouldBe(colour);
    }

    public static void S30_ternary_argument()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        var flag = true;
        actual.Id.ShouldBe(flag ? expected.Id : "none");
    }

    public static void S31_coalesce_argument()
    {
        var expected = Data.Expected();
        var actual = Data.Actual();
        actual.Id.ShouldBe(expected.Missing ?? expected.Id);
    }
}
