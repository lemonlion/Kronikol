using System.Net;
using FluentAssertions;
using Kronikol.Tracking;
using Shouldly;

// The shapes the plan's audit found no woven case for (a framework enum, a const decimal, a nullable enum, an
// enum-typed const), and the chains F10 reads.
public static class Audit210
{
    public static void A01_framework_enum_member()
    {
        var result = Data.Result();
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public static void A02_framework_enum_member_failing()
    {
        var result = Data.Result();
        result.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    public static void A03_framework_enum_member_fluent()
    {
        var result = Data.Result();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public static void A04_const_decimal()
    {
        var result = Data.Result();
        result.SplitPercentage.ShouldBe(Prices.Split);
    }

    public static void A05_nullable_enum_member()
    {
        var result = Data.Result();
        result.MaybeCode.ShouldBe(CurrencyCode.GBP);
    }

    public static void A06_enum_typed_const()
    {
        var result = Data.Result();
        result.Code.ShouldBe(Defaults.Code);
    }

    public static void A07_fluent_chain_with_a_const_key()
    {
        var result = Data.Result();
        result.Map.Should().ContainKey(Keys.Primary).WhoseValue.Should().Be(1);
    }

    public static void A08_shouldly_throw_then_message()
    {
        Should.Throw<InvalidOperationException>(() => Data.Fail()).Message.ShouldContain("boom");
    }

    public static async Task A09_that_async_block_body()
    {
        var expected = CurrencyCode.GBP;
        var result = Data.Result();
        await Track.ThatAsync(async () =>
        {
            await Task.Yield();
            result.Code.ShouldBe(expected);
        });
    }
}

public static class Prices
{
    public const decimal Split = 12.5m;
}

public static class Defaults
{
    public const CurrencyCode Code = CurrencyCode.GBP;
}
