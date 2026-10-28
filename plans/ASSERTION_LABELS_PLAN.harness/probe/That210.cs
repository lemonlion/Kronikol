using System.Globalization;
using Kronikol.Tracking;
using Shouldly;

// Track.That: the lambda's closure is read by ClosureValueResolver. The type is left out of the weave.
[SuppressAssertionTracking]
public static class That210
{
    public static void T01_to_string_with_culture()
    {
        var result = Data.Result();
        Track.That(() => result.SplitPercentage.ToString(CultureInfo.InvariantCulture).ShouldNotBeNullOrEmpty());
    }

    public static void T02_nameof_enum_member()
    {
        var result = Data.Result();
        Track.That(() => result.CurrencyIsoAlpha3.ShouldBe(nameof(CurrencyCode.GBP)));
    }

    public static void T03_const_field()
    {
        var result = Data.Result();
        Track.That(() => result.CurrencyIsoAlpha3.ShouldBe(Currencies.Gbp));
    }
}
