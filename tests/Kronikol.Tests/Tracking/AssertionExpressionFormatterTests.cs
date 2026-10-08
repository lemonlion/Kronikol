using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

public class AssertionExpressionFormatterTests
{
    [Theory]
    [InlineData("() => foo.Should().Be(\"blah\")", "Foo should be \"blah\"")]
    [InlineData("() => response.StatusCode.Should().Be(HttpStatusCode.OK)", "Response status code should be OK")]
    [InlineData("() => order.Items.Should().HaveCount(3)", "Order items should have count 3")]
    [InlineData("() => order.Total.Should().BeGreaterThan(0)", "Order total should be greater than 0")]
    [InlineData("() => items.Should().NotBeEmpty()", "Items should not be empty")]
    [InlineData("() => result.Name.Should().StartWith(\"John\")", "Result name should start with \"John\"")]
    [InlineData("() => users.Should().ContainSingle(x => x.IsAdmin)", "Users should contain single [ x => x.IsAdmin ]")]
    [InlineData("() => result.Should().BeNull()", "Result should be null")]
    [InlineData("() => count.Should().Be(42)", "Count should be 42")]
    [InlineData("() => list.Should().BeEmpty()", "List should be empty")]
    [InlineData("() => name.Should().NotBeNullOrWhiteSpace()", "Name should not be null or white space")]
    [InlineData("() => response.Headers.ContentType.Should().Be(\"application/json\")", "Response headers content type should be \"application/json\"")]
    public void Format_parses_Should_pattern_into_readable_sentence(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_strips_lambda_prefix()
    {
        var result = AssertionExpressionFormatter.Format("() => x.Should().BeTrue()");
        Assert.Equal("X should be true", result);
    }

    [Fact]
    public void Format_returns_cleaned_expression_when_no_Should_pattern()
    {
        var result = AssertionExpressionFormatter.Format("() => Assert.True(value)");
        Assert.Equal("Assert.True(value)", result);
    }

    [Fact]
    public void Format_handles_expression_without_lambda_prefix()
    {
        var result = AssertionExpressionFormatter.Format("foo.Should().BeTrue()");
        Assert.Equal("Foo should be true", result);
    }

    [Fact]
    public void Format_handles_null_expression()
    {
        var result = AssertionExpressionFormatter.Format(null);
        Assert.Equal("", result);
    }

    [Fact]
    public void Format_handles_empty_expression()
    {
        var result = AssertionExpressionFormatter.Format("");
        Assert.Equal("", result);
    }

    [Theory]
    [InlineData("() => result.Should().BeOfType<string>()", "Result should be of type <string>")]
    [InlineData("() => items.Should().AllBeOfType<int>()", "Items should all be of type <int>")]
    public void Format_handles_generic_type_arguments(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_handles_chained_And_by_taking_first_assertion()
    {
        var result = AssertionExpressionFormatter.Format("() => result.Should().NotBeNull().And.NotBeEmpty()");
        Assert.Equal("Result should not be null", result);
    }

    [Theory]
    [InlineData("() => dto.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5))", "Dto created at should be close to DateTime.UtcNow, TimeSpan.FromSeconds(5)")]
    [InlineData("() => list.Should().HaveCountGreaterThan(0)", "List should have count greater than 0")]
    public void Format_handles_multiple_arguments(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_strips_enum_prefix()
    {
        var result = AssertionExpressionFormatter.Format("() => status.Should().Be(TaskStatus.RanToCompletion)");
        Assert.Equal("Status should be RanToCompletion", result);
    }

    [Fact]
    public void Format_removes_null_forgiving_operators()
    {
        var result = AssertionExpressionFormatter.Format("() => _auditLogResponse!.StatusCode.Should().Be(HttpStatusCode.OK)");
        Assert.Equal("Audit log response status code should be OK", result);
    }

    [Fact]
    public void Format_strips_underscore_prefix_from_subject()
    {
        var result = AssertionExpressionFormatter.Format("() => _pancakeSteps.ResponseMessage.Should().NotBeNull()");
        Assert.Equal("Pancake steps response message should not be null", result);
    }

    [Theory]
    [InlineData("() => myVariable.Should().OnlyContain(x => x.Foo == bar)", "My variable should only contain [ x => x.Foo == bar ]")]
    [InlineData("() => items.Should().AllSatisfy(x => x.IsValid)", "Items should all satisfy [ x => x.IsValid ]")]
    [InlineData("() => _auditLogs.Should().BeInDescendingOrder(l => l.Timestamp)", "Audit logs should be in descending order [ l => l.Timestamp ]")]
    public void Format_wraps_lambda_args_in_square_brackets(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_substitutes_resolved_variable_in_args()
    {
        var resolved = new Dictionary<string, string> { ["expected"] = "hello" };
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().Be(expected)", resolved);
        Assert.Equal("X should be 'hello'", result);
    }

    [Fact]
    public void Format_substitutes_guid_variable()
    {
        var resolved = new Dictionary<string, string>
            { ["confirmationId"] = "12345678-1234-1234-1234-123456789abc" };
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().Be(confirmationId)", resolved);
        Assert.Equal("X should be '12345678-1234-1234-1234-123456789abc'", result);
    }

    [Fact]
    public void Format_leaves_unresolved_variable_unchanged()
    {
        var resolved = new Dictionary<string, string>();
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().Be(expected)", resolved);
        Assert.Equal("X should be expected", result);
    }

    [Fact]
    public void Format_with_null_resolved_values_behaves_like_original()
    {
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().Be(expected)", null);
        Assert.Equal("X should be expected", result);
    }

    [Fact]
    public void Format_substitutes_only_in_args_not_subject()
    {
        var resolved = new Dictionary<string, string> { ["result"] = "some value" };
        var result = AssertionExpressionFormatter.Format(
            "() => result.Should().Be(expected)", resolved);
        // 'result' is the subject — should NOT be substituted; 'expected' is not resolved
        Assert.Equal("Result should be expected", result);
    }

    [Fact]
    public void Format_substitutes_multiple_resolved_values()
    {
        var resolved = new Dictionary<string, string>
        {
            ["min"] = "1",
            ["max"] = "100"
        };
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().BeInRange(min, max)", resolved);
        Assert.Equal("X should be in range '1', '100'", result);
    }

    [Fact]
    public void Format_does_not_substitute_inside_quoted_strings()
    {
        var resolved = new Dictionary<string, string> { ["expected"] = "world" };
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().Be(\"expected\")", resolved);
        // "expected" is a string literal, not a variable reference
        Assert.Equal("X should be \"expected\"", result);
    }

    [Fact]
    public void Format_does_not_substitute_partial_token_match()
    {
        var resolved = new Dictionary<string, string> { ["id"] = "123" };
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().Be(orderId)", resolved);
        // 'id' should NOT match inside 'orderId'
        Assert.Equal("X should be orderId", result);
    }

    [Fact]
    public void Format_substitutes_null_resolved_value()
    {
        var resolved = new Dictionary<string, string> { ["expected"] = "null" };
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().Be(expected)", resolved);
        Assert.Equal("X should be 'null'", result);
    }

    [Fact]
    public void Format_substitutes_dotted_property_chain_value()
    {
        var resolved = new Dictionary<string, string> { ["expected.ExpectedIngredientCount"] = "5" };
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().HaveCount(expected.ExpectedIngredientCount)", resolved);
        Assert.Equal("X should have count '5'", result);
    }

    [Fact]
    public void Format_prefers_longer_key_over_shorter_key()
    {
        // Both keys present — longer dotted key must match first
        var resolved = new Dictionary<string, string>
        {
            ["expected"] = "MuffinBatchExpectation { ... }",
            ["expected.ExpectedIngredientCount"] = "5"
        };
        var result = AssertionExpressionFormatter.Format(
            "() => x.Should().HaveCount(expected.ExpectedIngredientCount)", resolved);
        Assert.Equal("X should have count '5'", result);
    }

    [Fact]
    public void Format_strips_trailing_underscore_from_subject_segments()
    {
        var result = AssertionExpressionFormatter.Format("() => response_.Items.Should().NotBeEmpty()");
        Assert.Equal("Response items should not be empty", result);
    }

    [Fact]
    public void Format_simplifies_dotted_member_access_in_args()
    {
        var result = AssertionExpressionFormatter.Format(
            "() => _muffinStepsResponse.Ingredients.Should().Contain(_eggsSteps.EggsResponse.Eggs)");
        Assert.Equal("Muffin steps response ingredients should contain 'Eggs'", result);
    }

    [Fact]
    public void Format_simplifies_underscore_prefixed_two_segment_path_in_args()
    {
        var result = AssertionExpressionFormatter.Format(
            "() => result.Should().Be(_steps.ExpectedValue)");
        Assert.Equal("Result should be 'ExpectedValue'", result);
    }

    [Fact]
    public void Format_does_not_simplify_enum_like_two_segment_paths()
    {
        // Two PascalCase segments without underscore prefix — leave for enum stripping
        var result = AssertionExpressionFormatter.Format(
            "() => status.Should().Be(HttpStatusCode.OK)");
        Assert.Equal("Status should be OK", result);
    }

    [Fact]
    public void Format_strips_trailing_underscore_from_last_segment_in_args()
    {
        var result = AssertionExpressionFormatter.Format(
            "() => items.Should().Contain(_steps.Response.Eggs_)");
        Assert.Equal("Items should contain 'Eggs'", result);
    }

    [Fact]
    public void Format_substitutes_resolved_values_inside_lambda_args()
    {
        var resolved = new Dictionary<string, string> { ["orderId"] = "68AEEE84-B903-48E1-A01F-BE25C8193491" };
        var result = AssertionExpressionFormatter.Format(
            "() => auditLogs.Should().Contain(l => l.EntityId == orderId && l.Action == \"Created\")", resolved);
        Assert.Equal("Audit logs should contain [ l => l.EntityId == '68AEEE84-B903-48E1-A01F-BE25C8193491' && l.Action == \"Created\" ]", result);
    }

    [Theory]
    [InlineData("await _myVariable.Should().BeTrue()", "My variable should be true")]
    [InlineData("await response.StatusCode.Should().Be(HttpStatusCode.OK)", "Response status code should be OK")]
    public void Format_strips_await_prefix(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("var foo = bar.Should().BeTrue()", "Bar should be true")]
    [InlineData("var result = response.StatusCode.Should().Be(HttpStatusCode.OK)", "Response status code should be OK")]
    [InlineData("var bar = bar.Should().BeTrue()", "Bar should be true")]
    [InlineData("() => var foo = bar.Should().BeTrue()", "Bar should be true")]
    public void Format_strips_var_assignment_prefix(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("() => myCollection.First().Should().Be(42)", "First value of my collection should be 42")]
    [InlineData("() => myCollection.Last().Should().Be(42)", "Last value of my collection should be 42")]
    [InlineData("() => _items.First().Name.Should().Be(\"test\")", "First value of items name should be \"test\"")]
    [InlineData("() => _items.Last().Name.Should().Be(\"test\")", "Last value of items name should be \"test\"")]
    [InlineData("() => response.Orders.First().Should().NotBeNull()", "First value of response orders should not be null")]
    [InlineData("() => response.Orders.Last().Should().NotBeNull()", "Last value of response orders should not be null")]
    public void Format_transforms_First_and_Last_in_subject(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("() => foo?.Bar.Should().Be(\"blah\")", "Foo bar should be \"blah\"")]
    [InlineData("() => _response?.StatusCode.Should().Be(HttpStatusCode.OK)", "Response status code should be OK")]
    [InlineData("() => a?.B?.C.Should().NotBeNull()", "A b c should not be null")]
    public void Format_strips_null_conditional_operators(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("claim.Should()\n    .NotBeNull()\n    .And\n    .Be(value)", "Claim should not be null")]
    [InlineData("claim.Should() .NotBeNull() .And .Be(value)", "Claim should not be null")]
    [InlineData("claim.Should()\r\n    .NotBeNull()\r\n    .And\r\n    .HaveLength(length)", "Claim should not be null")]
    [InlineData("() => claim.Should()\n    .NotBeNull()", "Claim should not be null")]
    [InlineData("() => result.Should()\n    .Be(42)", "Result should be 42")]
    public void Format_handles_multiline_expressions_with_whitespace_around_dots(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("() => response.StatusCode.ToString().Should().Be(\"200\")", "Response status code should be \"200\"")]
    [InlineData("() => order.Id.ToString().Should().NotBeEmpty()", "Order id should not be empty")]
    [InlineData("() => item.Name.ToString().Should().StartWith(\"A\")", "Item name should start with \"A\"")]
    public void Format_removes_to_string_from_subject(string expression, string expected)
    {
        var result = AssertionExpressionFormatter.Format(expression);
        Assert.Equal(expected, result);
    }

    // SHOULDLY_ASSERTIONS_PLAN F13: the formatter removed every '!' to drop the null-forgiving operator, so a
    // literal lost its own ("Hi!" read "Hi"), "!=" read "=" and a negation read as its opposite. The cleanup
    // that turns "?." into "." and joins " ." had the same reach into literals.
    [Theory]
    [InlineData("greeting.Should().Be(\"Hi!\")", "Greeting should be \"Hi!\"")]
    [InlineData("mark.Should().Be('!')", "Mark should be '!'")]
    [InlineData("path.Should().Be(\"a?.b\")", "Path should be \"a?.b\"")]
    [InlineData("text.Should().Be(\"a .b\")", "Text should be \"a .b\"")]
    [InlineData("text.Should().Be(@\"C:\\x!\")", "Text should be @\"C:\\x!\"")]
    [InlineData("text.Should().Be(\"say \\\"hi!\\\"\")", "Text should be \"say \\\"hi!\\\"\"")]
    public void A_bang_in_a_string_literal_survives_the_label(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("(x != y).Should().BeTrue()", "(x != y) should be true")]
    [InlineData("(!flag).Should().BeTrue()", "(!flag) should be true")]
    public void An_operator_bang_survives_the_label(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("order!.Name.Should().Be(\"a\")", "Order name should be \"a\"")]
    [InlineData("result!.Should().BeTrue()", "Result should be true")]
    [InlineData("items.First()!.Name.Should().Be(\"a\")", "First value of items name should be \"a\"")]
    [InlineData("value.Should().Be(expected!)", "Value should be expected")]
    [InlineData("value.Should().Be(map[key]!.Id)", "Value should be 'Id'")]
    public void A_null_forgiving_bang_is_still_dropped(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }
}
