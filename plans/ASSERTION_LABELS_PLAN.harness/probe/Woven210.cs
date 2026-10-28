using System.Globalization;
using Shouldly;

// One statement per line, as real tests are: the weave reads a statement's text from its sequence point.
public static class Woven210
{
    public static void W01_issue_to_string_with_culture()
    {
        var result = Data.Result();
        result.SplitPercentage.ToString(CultureInfo.InvariantCulture).ShouldNotBeNullOrEmpty();
    }

    public static void W02_issue_nameof_enum_member()
    {
        var result = Data.Result();
        result.CurrencyIsoAlpha3.ShouldBe(nameof(CurrencyCode.GBP));
    }

    public static void W03_issue_nameof_failing()
    {
        var result = Data.Result();
        result.CurrencyIsoAlpha3.ShouldBe(nameof(CurrencyCode.USD));
    }

    public static void W04_const_field()
    {
        var result = Data.Result();
        result.CurrencyIsoAlpha3.ShouldBe(Currencies.Gbp);
    }

    public static void W05_const_field_failing()
    {
        var result = Data.Result();
        result.CurrencyIsoAlpha3.ShouldBe(Currencies.Usd);
    }

    public static void W06_static_readonly_field()
    {
        var result = Data.Result();
        result.CurrencyIsoAlpha3.ShouldBe(Currencies.GbpReadonly);
    }

    public static void W07_const_local()
    {
        const string gbp = "GBP";
        var result = Data.Result();
        result.CurrencyIsoAlpha3.ShouldBe(gbp);
    }

    public static void W08_const_int_field()
    {
        var result = Data.Result();
        result.Count.ShouldBe(Limits.MaxRetries);
    }

    public static void W09_int_max_value()
    {
        var result = Data.Result();
        result.Big.ShouldBe(Int32.MaxValue);
    }

    public static void W10_enum_member()
    {
        var result = Data.Result();
        result.Code.ShouldBe(CurrencyCode.GBP);
    }

    public static void W11_local_holding_nameof()
    {
        var expected = nameof(CurrencyCode.GBP);
        var result = Data.Result();
        result.CurrencyIsoAlpha3.ShouldBe(expected);
    }

    public static void W12_nameof_concatenated()
    {
        var result = Data.Result();
        result.Stage.ShouldBe(nameof(ApplicationStage.Cancelled) + "");
    }

    public static void W13_nameof_dictionary_key()
    {
        var result = Data.Result();
        result.Errors.ShouldContainKey(nameof(Request.Email));
    }

    public static void W14_to_string_with_format_and_culture()
    {
        var result = Data.Result();
        result.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).ShouldBe("2026-10-27");
    }

    public static void W15_lambda_in_subject()
    {
        var result = Data.Result();
        result.Items.Count(x => x.IsActive).ShouldBe(2);
    }

    public static void W16_generic_call_in_subject()
    {
        var result = Data.Result();
        result.Get<Item>().Name.ShouldBe("a");
    }

    public static void W17_new_in_subject()
    {
        var result = Data.Result();
        new Uri(result.Link).Host.ShouldBe("example.com");
    }

    public static void W18_const_index_in_subject()
    {
        var result = Data.Result();
        result.Map[Keys.Primary].ShouldBe(1);
    }

    public static void W19_to_string_with_culture_failing()
    {
        var result = Data.Result();
        result.SplitPercentage.ToString(CultureInfo.InvariantCulture).ShouldBe("12,5");
    }
}
