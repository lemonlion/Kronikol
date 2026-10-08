using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

public class ClosureValueResolverTests
{
    [Fact]
    public void Resolves_simple_local_variable()
    {
        var expected = "hello";
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(expected)");

        Assert.Equal("hello", result.ResolvedValues["expected"]);
        Assert.Empty(result.Fallbacks);
    }

    [Fact]
    public void A_bracket_in_a_literal_does_not_cut_the_arguments_short()
    {
        // The argument scan counted the "(" in the literal as an open call and never found the list's end.
        var expected = "hello";
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(expected, \"the reason (and more\")");

        Assert.Equal("hello", result.ResolvedValues["expected"]);
    }

    [Theory]
    [InlineData("() => x.ShouldBe(expected)")]
    [InlineData("() => order.Name.ShouldContain(expected, \"why\")")]
    [InlineData("() => Should.Throw<InvalidOperationException>(() => Run(expected))")]
    public void A_Shouldly_call_has_its_arguments_read(string expression)
    {
        // The arguments were read only after FluentAssertions' .Should(). (SHOULDLY_ASSERTIONS_PLAN F10).
        var expected = "hello";
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(action, expression);

        Assert.Equal("hello", result.ResolvedValues["expected"]);
    }

    [Fact]
    public void Resolves_numeric_variable()
    {
        var count = 42;
        Action action = () => Dummy(count);

        var result = ClosureValueResolver.ResolveValues(action, "() => items.Should().HaveCount(count)");

        Assert.Equal("42", result.ResolvedValues["count"]);
    }

    [Fact]
    public void Resolves_null_value_as_null_string()
    {
        string? expected = null;
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(expected)");

        Assert.Equal("null", result.ResolvedValues["expected"]);
    }

    [Fact]
    public void Returns_empty_for_static_lambda_no_captures()
    {
        Action action = () => Dummy(42);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(42)");

        Assert.Empty(result.ResolvedValues);
        Assert.Empty(result.Fallbacks);
    }

    [Fact]
    public void Returns_empty_when_expression_is_null()
    {
        var expected = "hello";
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(action, null);

        Assert.Empty(result.ResolvedValues);
    }

    [Fact]
    public void Returns_empty_when_no_Should_pattern()
    {
        var value = "hello";
        Action action = () => Dummy(value);

        var result = ClosureValueResolver.ResolveValues(action, "() => Assert.True(value)");

        Assert.Empty(result.ResolvedValues);
    }

    [Fact]
    public void Falls_back_for_complex_object_where_ToString_returns_type_name()
    {
        var complex = new UnformattableObject();
        Action action = () => Dummy(complex);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(complex)");

        Assert.DoesNotContain("complex", result.ResolvedValues.Keys);
        Assert.Single(result.Fallbacks);
        Assert.Equal("complex", result.Fallbacks[0].FieldName);
        Assert.Equal("ToString returned type name", result.Fallbacks[0].Reason);
    }

    [Fact]
    public void Falls_back_for_computed_expression_with_subtraction()
    {
        var maxOrders = 5;
        Action action = () => Dummy(maxOrders);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(maxOrders - 1)");

        Assert.DoesNotContain("maxOrders", result.ResolvedValues.Keys);
        Assert.Single(result.Fallbacks);
        Assert.Equal("maxOrders", result.Fallbacks[0].FieldName);
        Assert.Equal("computed expression", result.Fallbacks[0].Reason);
    }

    [Fact]
    public void Falls_back_for_computed_expression_with_addition()
    {
        var baseCount = 10;
        Action action = () => Dummy(baseCount);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(baseCount + 1)");

        Assert.DoesNotContain("baseCount", result.ResolvedValues.Keys);
        Assert.Single(result.Fallbacks);
        Assert.Equal("computed expression", result.Fallbacks[0].Reason);
    }

    [Fact]
    public void Excludes_fields_not_in_args()
    {
        var subject = "ignored";
        var expected = "hello";
        Action action = () => { _ = subject; Dummy(expected); };

        var result = ClosureValueResolver.ResolveValues(action, "() => subject.Should().Be(expected)");

        // 'subject' is the subject (before .Should()), not in args
        Assert.Single(result.ResolvedValues);
        Assert.Equal("hello", result.ResolvedValues["expected"]);
    }

    [Fact]
    public void Resolves_multiple_args_partially()
    {
        var expected = "hello";
        var complex = new UnformattableObject();
        Action action = () => { Dummy(expected); Dummy(complex); };

        var result = ClosureValueResolver.ResolveValues(
            action, "() => x.Should().BeInRange(expected, complex)");

        Assert.Equal("hello", result.ResolvedValues["expected"]);
        Assert.DoesNotContain("complex", result.ResolvedValues.Keys);
        Assert.Single(result.Fallbacks);
    }

    [Fact]
    public void Truncates_long_string_values()
    {
        var longValue = new string('x', 100);
        Action action = () => Dummy(longValue);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(longValue)");

        Assert.Equal(new string('x', 50) + "...", result.ResolvedValues["longValue"]);
    }

    [Fact]
    public void Shows_collection_count()
    {
        var items = new List<int> { 1, 2, 3 };
        Action action = () => Dummy(items);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(items)");

        Assert.Equal("[ 1, 2, 3 ]", result.ResolvedValues["items"]);
    }

    [Fact]
    public void Does_not_match_token_as_substring_of_longer_identifier()
    {
        var id = "abc";
        var orderId = "xyz";
        Action action = () => { Dummy(id); Dummy(orderId); };

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(orderId)");

        // Only 'orderId' should match, not 'id' as a substring
        Assert.Single(result.ResolvedValues);
        Assert.Equal("xyz", result.ResolvedValues["orderId"]);
    }

    [Fact]
    public void Resolves_guid_value()
    {
        var confirmationId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        Action action = () => Dummy(confirmationId);

        var result = ClosureValueResolver.ResolveValues(
            action, "() => x.Should().Be(confirmationId)");

        Assert.Equal("12345678-1234-1234-1234-123456789abc", result.ResolvedValues["confirmationId"]);
    }

    [Fact]
    public void IsStandaloneToken_matches_at_start()
    {
        Assert.True(ClosureValueResolver.IsStandaloneToken("expected, other", "expected"));
    }

    [Fact]
    public void IsStandaloneToken_matches_at_end()
    {
        Assert.True(ClosureValueResolver.IsStandaloneToken("other, expected", "expected"));
    }

    [Fact]
    public void IsStandaloneToken_does_not_match_substring()
    {
        Assert.False(ClosureValueResolver.IsStandaloneToken("expectedValue", "expected"));
    }

    [Fact]
    public void IsStandaloneToken_does_not_match_prefix_continuation()
    {
        Assert.False(ClosureValueResolver.IsStandaloneToken("_expectedValue", "expected"));
    }

    [Fact]
    public void IsInComputedExpression_detects_subtraction()
    {
        Assert.True(ClosureValueResolver.IsInComputedExpression("maxOrders - 1", "maxOrders"));
    }

    [Fact]
    public void IsInComputedExpression_detects_addition()
    {
        Assert.True(ClosureValueResolver.IsInComputedExpression("baseCount + 1", "baseCount"));
    }

    [Fact]
    public void IsInComputedExpression_returns_false_for_simple_variable()
    {
        Assert.False(ClosureValueResolver.IsInComputedExpression("expected", "expected"));
    }

    [Fact]
    public void IsInComputedExpression_returns_false_when_variable_in_method_call()
    {
        Assert.False(ClosureValueResolver.IsInComputedExpression("SomeMethod(expected)", "expected"));
    }

    [Fact]
    public void Resolves_instance_field_via_this_capture()
    {
        var holder = new FieldHolder { _confirmationId = "test-id-123" };
        var action = holder.CreateClosureCapturingThis();

        var result = ClosureValueResolver.ResolveValues(
            action, "() => x.Should().Be(_confirmationId)");

        Assert.Equal("test-id-123", result.ResolvedValues["_confirmationId"]);
    }

    [Fact]
    public void Resolves_dotted_property_chain_on_complex_object()
    {
        var expected = new MuffinBatchExpectation { ExpectedIngredientCount = 5 };
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(
            action, "() => x.Should().HaveCount(expected.ExpectedIngredientCount)");

        Assert.Equal("5", result.ResolvedValues["expected.ExpectedIngredientCount"]);
        Assert.DoesNotContain("expected", result.ResolvedValues.Keys);
    }

    [Fact]
    public void Resolves_multi_level_dotted_property_chain()
    {
        var config = new OuterConfig { Inner = new InnerConfig { Value = 42 } };
        Action action = () => Dummy(config);

        var result = ClosureValueResolver.ResolveValues(
            action, "() => x.Should().Be(config.Inner.Value)");

        Assert.Equal("42", result.ResolvedValues["config.Inner.Value"]);
    }

    [Fact]
    public void Falls_back_when_property_does_not_exist_on_chain()
    {
        var expected = new MuffinBatchExpectation { ExpectedIngredientCount = 5 };
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(
            action, "() => x.Should().Be(expected.NonExistentProperty)");

        // Should not crash — falls back gracefully
        Assert.DoesNotContain("expected.NonExistentProperty", result.ResolvedValues.Keys);
        Assert.Single(result.Fallbacks);
    }

    [Fact]
    public void Falls_back_when_property_chain_returns_null()
    {
        var config = new OuterConfig { Inner = null! };
        Action action = () => Dummy(config);

        var result = ClosureValueResolver.ResolveValues(
            action, "() => x.Should().Be(config.Inner.Value)");

        Assert.Equal("null", result.ResolvedValues["config.Inner.Value"]);
    }

    [Fact]
    public void Simple_token_still_resolves_when_no_dot_follows()
    {
        var expected = "hello";
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(action, "() => x.Should().Be(expected)");

        Assert.Equal("hello", result.ResolvedValues["expected"]);
    }

    [Fact]
    public void Resolves_string_property_on_object()
    {
        var expected = new MuffinBatchExpectation { ExpectedName = "chocolate" };
        Action action = () => Dummy(expected);

        var result = ClosureValueResolver.ResolveValues(
            action, "() => x.Should().Be(expected.ExpectedName)");

        Assert.Equal("chocolate", result.ResolvedValues["expected.ExpectedName"]);
    }

    // --- Helpers ---

    private static void Dummy(object? _) { }

    private class UnformattableObject
    {
        // ToString() returns type name by default — exactly what we detect as "complex object"
    }

    private class MuffinBatchExpectation
    {
        public int ExpectedIngredientCount { get; set; }
        public string? ExpectedName { get; set; }
    }

    private class OuterConfig
    {
        public InnerConfig Inner { get; set; } = new();
    }

    private class InnerConfig
    {
        public int Value { get; set; }
    }

    public class FieldHolder
    {
        public string _confirmationId = "";

        public Action CreateClosureCapturingThis()
        {
            return () => Dummy(_confirmationId);
        }

        private static void Dummy(object? _) { }
    }
}
