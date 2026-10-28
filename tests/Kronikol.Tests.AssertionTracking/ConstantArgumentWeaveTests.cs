using Microsoft.CodeAnalysis;

namespace Kronikol.Tests.AssertionTracking;

/// <summary>
/// #210's constants through the weave (ASSERTION_LABELS_PLAN facts 16 to 20, R2), in Debug and in Release. The weave
/// left out every argument the compiler folded into a constant, because "its text already says what it is", which is
/// true of a literal and of an enum's own members only: a <c>const</c> field or local, <c>nameof(…)</c>, constant
/// arithmetic and an enum-typed <c>const</c> name something else, and read as a bare name (<c>should be Gbp</c> for
/// "GBP"). They read as their values now; an enum's own members keep their names; and a statement whose constant would
/// widen the range the weave copies past what it can copy keeps the values it copied without it.
/// </summary>
public class ConstantArgumentWeaveTests
{
    private const string Source = """
        using System;
        using System.Net;
        using FluentAssertions;
        using Kronikol.Tracking;
        using Shouldly;

        [assembly: TrackAssertions]

        public enum CurrencyCode { GBP, USD }

        public enum ApplicationStage { Draft, Cancelled }

        [Flags]
        public enum Perm { None = 0, Read = 1, Write = 2 }

        public static class Codes
        {
            public enum Kind { Primary, Secondary }

            public const string Primary = "primary";
        }

        public static class Currencies
        {
            public const string Gbp = "GBP";
            public const string Usd = "USD";
        }

        public static class Limits
        {
            public const int MaxRetries = 3;
        }

        public static class Defaults
        {
            public const CurrencyCode Code = CurrencyCode.GBP;
        }

        public static class Prices
        {
            public const decimal Split = 12.5m;
        }

        public sealed class Result
        {
            public string Currency { get; init; } = "GBP";
            public int Count { get; init; } = 3;
            public int Big { get; init; } = int.MaxValue;
            public string Stage { get; init; } = "Cancelled";
            public CurrencyCode Code { get; init; } = CurrencyCode.GBP;
            public CurrencyCode? MaybeCode { get; init; } = CurrencyCode.GBP;
            public Perm Perm { get; init; } = Perm.Read | Perm.Write;
            public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
            public decimal Split { get; init; } = 12.5m;
        }

        [ShouldlyMethods]
        public static class SpanAssertions
        {
            public static void ShouldHaveCountAndPrefix(this string actual, int count, ReadOnlySpan<char> prefix, string expected)
            {
                if (actual.Length < count || !actual.AsSpan().StartsWith(prefix) || actual != expected)
                    throw new ShouldAssertException($"{actual} should have {count} and start with {prefix.ToString()}");
            }

            public static void ShouldHaveCountAndRef(this string actual, int count, ref int seen, string expected)
            {
                seen++;
                if (actual.Length < count || actual != expected)
                    throw new ShouldAssertException($"{actual} should have {count}");
            }

            public static void ShouldHaveKindAndName(this string actual, object kind, string name)
            {
                if (actual == null || kind == null || name == null)
                    throw new ShouldAssertException($"{actual} should have {kind} and {name}");
            }
        }

        public static class Constants
        {
            public static void Const_field()
            {
                var result = new Result();
                result.Currency.ShouldBe(Currencies.Gbp);
            }

            public static void Const_field_failing()
            {
                var result = new Result();
                result.Currency.ShouldBe(Currencies.Usd);
            }

            public static void Const_local()
            {
                const string gbp = "GBP";
                var result = new Result();
                result.Currency.ShouldBe(gbp);
            }

            public static void Const_int()
            {
                var result = new Result();
                result.Count.ShouldBe(Limits.MaxRetries);
            }

            public static void Framework_const()
            {
                var result = new Result();
                result.Big.ShouldBe(Int32.MaxValue);
            }

            public static void Nameof_concatenated()
            {
                var result = new Result();
                result.Stage.ShouldBe(nameof(ApplicationStage.Cancelled) + "");
            }

            public static void Enum_typed_const()
            {
                var result = new Result();
                result.Code.ShouldBe(Defaults.Code);
            }

            public static void Constant_arithmetic()
            {
                var result = new Result { Count = 4 };
                result.Count.ShouldBe(Limits.MaxRetries + 1);
            }

            public static void Fluent_const_field()
            {
                var result = new Result();
                result.Currency.Should().Be(Currencies.Gbp);
            }

            public static void Fluent_const_field_failing()
            {
                var result = new Result();
                result.Currency.Should().Be(Currencies.Usd);
            }

            public static void Enum_member()
            {
                var result = new Result();
                result.Code.ShouldBe(CurrencyCode.GBP);
            }

            public static void Enum_members_ored()
            {
                var result = new Result();
                result.Perm.ShouldBe(Perm.Read | Perm.Write);
            }

            public static void Enum_member_as_object()
            {
                var result = new Result();
                ((object)result.Code).ShouldBe(CurrencyCode.GBP);
            }

            public static void Framework_enum_member()
            {
                var result = new Result();
                result.Status.ShouldBe(HttpStatusCode.OK);
            }

            public static void Fluent_framework_enum_member()
            {
                var result = new Result();
                result.Status.Should().Be(HttpStatusCode.OK);
            }

            public static void Const_decimal()
            {
                var result = new Result();
                result.Split.ShouldBe(Prices.Split);
            }

            public static void Nullable_constant()
            {
                var result = new Result();
                result.MaybeCode.ShouldBe(CurrencyCode.GBP);
            }

            public static void Constant_below_a_span()
            {
                var actual = "GBP";
                var expected = "GBP";
                actual.ShouldHaveCountAndPrefix(Limits.MaxRetries, "G".AsSpan(), expected);
            }

            public static void Constant_below_a_ref()
            {
                var actual = "GBP";
                var seen = 0;
                actual.ShouldHaveCountAndRef(Limits.MaxRetries, ref seen, actual.ToUpperInvariant());
            }

            public static void Constant_above_an_enum_member()
            {
                var actual = "GBP";
                actual.ShouldHaveKindAndName(Codes.Kind.Primary, Codes.Primary);
            }

            public static void Comment_line()
            {
                var result = new Result();
                result.Count.ShouldBe(
                    // the retries the policy allows
                    3);
            }
        }
        """;

    public static TheoryData<string, string, bool> ConstantLabels => new()
    {
        // The issue's W04, W05, W07 to W09 and W12, and the plan's other two: each read its name, or nameof's code.
        { "Const_field", "Result currency should be 'GBP'", true },
        { "Const_field_failing", "Result currency should be 'USD'", false },
        { "Const_local", "Result currency should be 'GBP'", true },
        { "Const_int", "Result count should be '3'", true },
        { "Framework_const", "Result big should be '2147483647'", true },
        { "Nameof_concatenated", "Result stage should be 'Cancelled'", true },
        { "Enum_typed_const", "Result code should be 'GBP'", true },
        { "Constant_arithmetic", "Result count should be '4'", true },
        // The walk to an enum's box stops at the constant: the box below it is the other argument's, Codes.Kind's,
        // whose names would spell Codes.Primary.
        { "Constant_above_an_enum_member", "Actual should have kind and name Codes.Kind.Primary, 'primary'", true },
        // A span can be a local, so the weave copies the stack across it.
        { "Constant_below_a_span", "Actual should have count and prefix '3', \"G\".AsSpan(), 'GBP'", true },
        { "Fluent_const_field", "Result currency should be 'GBP'", true },
        { "Fluent_const_field_failing", "Result currency should be 'USD'", false },
    };

    [Theory]
    [MemberData(nameof(ConstantLabels))]
    public void A_constant_that_names_something_else_reads_as_its_value(string method, string label, bool passes)
    {
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), "Constants", method);

            Assert.True(passes == thrown is null, $"{optimization}: {thrown}");
            var note = Assert.Single(notes);
            Assert.Equal(passes, note.Passed);
            Assert.Equal(label, note.Label);
        }
    }

    [Theory]
    [InlineData("Enum_member", "Result code should be GBP")]
    [InlineData("Enum_members_ored", "Result perm should be Perm.Read | Perm.Write")]
    [InlineData("Enum_member_as_object", "((object)result.Code) should be GBP")]
    [InlineData("Framework_enum_member", "Result status should be OK")]
    [InlineData("Fluent_framework_enum_member", "Result status should be OK")]
    public void An_enum_member_keeps_its_name(string method, string label)
    {
        // Its text is the value's own spelling. A framework enum has to resolve while the weave runs, or the check would
        // fail and HttpStatusCode.OK read 'OK': 157 of BreakfastProvider's labels compare a status code.
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), "Constants", method);

            Assert.True(thrown is null, $"{optimization}: {thrown}");
            Assert.Equal(label, Assert.Single(notes).Label);
        }
    }

    [Theory]
    [InlineData("Const_decimal", "Result split should be '12.5'")]
    [InlineData("Nullable_constant", "Result maybe code should be 'GBP'")]
    public void A_const_decimal_and_a_nullable_constant_read_as_before(string method, string label)
    {
        // The compiler builds both with newobj, where the constant check stops, so 4.16.0's weave already passed them.
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), "Constants", method);

            Assert.True(thrown is null, $"{optimization}: {thrown}");
            Assert.Equal(label, Assert.Single(notes).Label);
        }
    }

    [Fact]
    public void A_constant_that_widens_the_copy_keeps_the_other_values()
    {
        // The weave copies the stack from the lowest argument it captures; a constant below a ref argument would take
        // the copy across the argument's address, which no local can hold, and the statement would lose every value:
        // actual.ToUpperInvariant(), above the address, is a call no variable's value can answer, and would read as code.
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), "Constants", "Constant_below_a_ref");

            Assert.True(thrown is null, $"{optimization}: {thrown}");
            Assert.Equal("Actual should have count and ref Limits.MaxRetries, ref seen, 'GBP'", Assert.Single(notes).Label);
        }
    }

    [Fact]
    public void A_comment_line_in_a_statement_is_not_read()
    {
        // The weave joins a statement's lines with a space, so a line's // comment took the rest of the statement with it
        // (#210, F12); the weave leaves it out before the join.
        foreach (var optimization in new[] { OptimizationLevel.Debug, OptimizationLevel.Release })
        {
            var (thrown, notes) = WovenRun.Run(Woven(optimization), "Constants", "Comment_line");

            Assert.True(thrown is null, $"{optimization}: {thrown}");
            Assert.Equal("Result count should be 3", Assert.Single(notes).Label);
        }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<OptimizationLevel, string> Built = new();

    internal static string Woven(OptimizationLevel optimization)
    {
        lock (Gate)
        {
            if (!Built.TryGetValue(optimization, out var path))
                Built[optimization] = path = WovenRun.BuildAndWeave($"ConstantArguments_{optimization}", Source, optimization);
            return path;
        }
    }
}
