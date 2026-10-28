using System.Globalization;
using FluentAssertions;

// FluentAssertions 6.12.1, as the #163 probe.
public static class Fluent210
{
    public static void F01_to_string_with_culture()
    {
        var result = Data.Result();
        result.SplitPercentage.ToString(CultureInfo.InvariantCulture).Should().NotBeNullOrEmpty();
    }

    public static void F02_nameof_enum_member()
    {
        var result = Data.Result();
        result.CurrencyIsoAlpha3.Should().Be(nameof(CurrencyCode.GBP));
    }

    public static void F03_const_field()
    {
        var result = Data.Result();
        result.CurrencyIsoAlpha3.Should().Be(Currencies.Gbp);
    }
}
