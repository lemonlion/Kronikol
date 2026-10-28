using System.Text;
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
    public void Format_reads_each_assertion_of_a_chained_And()
    {
        // It read the first assertion only, so a failing second half was labelled with the half that passed (#210, Q8).
        var result = AssertionExpressionFormatter.Format("() => result.Should().NotBeNull().And.NotBeEmpty()");
        Assert.Equal("Result should not be null and not be empty", result);
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
    public void A_member_path_with_no_value_is_shown_as_written()
    {
        // A quoted word means a value: the last member's name in quotes ('Eggs') read as one (#163, Q2).
        var result = AssertionExpressionFormatter.Format(
            "() => _muffinStepsResponse.Ingredients.Should().Contain(_eggsSteps.EggsResponse.Eggs)");
        Assert.Equal("Muffin steps response ingredients should contain _eggsSteps.EggsResponse.Eggs", result);
    }

    [Fact]
    public void An_underscore_prefixed_two_segment_path_with_no_value_is_shown_as_written()
    {
        var result = AssertionExpressionFormatter.Format(
            "() => result.Should().Be(_steps.ExpectedValue)");
        Assert.Equal("Result should be _steps.ExpectedValue", result);
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
    public void A_member_path_ending_in_an_underscore_is_shown_as_written()
    {
        var result = AssertionExpressionFormatter.Format(
            "() => items.Should().Contain(_steps.Response.Eggs_)");
        Assert.Equal("Items should contain _steps.Response.Eggs_", result);
    }

    [Theory]
    [InlineData("value.ShouldBe(expected.Inner.Value)", "Value should be expected.Inner.Value")]
    [InlineData("value.Should().Be(expected.Inner.Value)", "Value should be expected.Inner.Value")]
    [InlineData("await Assert.That(value).IsEqualTo(expected.Inner.Value)", "Value is equal to expected.Inner.Value")]
    [InlineData("value.ShouldBe(expected.Inner?.Value)", "Value should be expected.Inner.Value")]
    public void A_member_name_is_never_quoted_as_a_value(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("x.Should().Be(expected.Lines[0])", "expected.Lines", "[ \"x\", \"y\" ]", "X should be expected.Lines[0]")]
    [InlineData("x.ShouldBeSameAs(expected.Plain)", "expected", "Order { Id = A1 }", "X should be same as expected.Plain")]
    [InlineData("x.ShouldBe(expected.Inner.Value)", "expected.Inner", "Inner(v)", "X should be expected.Inner.Value")]
    [InlineData("x.ShouldBe(expected.Inner!.Value)", "expected.Inner", "Inner(v)", "X should be expected.Inner.Value")]
    [InlineData("x.ShouldBe(ids[0])", "ids", "[ \"A1\" ]", "X should be ids[0]")]
    public void A_key_is_not_substituted_inside_a_longer_chain(string expression, string key, string value, string expected)
    {
        var label = AssertionExpressionFormatter.Format(expression, new Dictionary<string, string> { [key] = value });

        Assert.Equal(expected, label);
    }

    [Fact]
    public void A_plain_dictionary_key_does_not_stand_before_a_call()
    {
        // Only Kronikol's own readers know a value is a single value (a number, an enum, a string, ...), so only
        // they put a value before a call; a dictionary passed in replaces whole chains (#163, Q1).
        var label = AssertionExpressionFormatter.Format(
            "x.ShouldBe(expected.Colour.ToString())",
            new Dictionary<string, string> { ["expected.Colour"] = "Blue" });

        Assert.Equal("X should be expected.Colour.ToString()", label);
    }

    [Theory]
    [InlineData("\"Colour\".ShouldBe(nameof(expected.Colour))", "\"Colour\" should be 'Colour'")]
    [InlineData("\"Colour\".Should().Be(\"Colour\")", "\"Colour\" should be \"Colour\"")]
    [InlineData("x.ToString(\"G\").ShouldBe(\"Blue\")", "X to string(\"G\") should be \"Blue\"")]
    [InlineData("\"a.b\".Length.ShouldBe(3)", "\"a.b\" length should be 3")]
    [InlineData("await Assert.That(\"Colour\").IsEqualTo(\"Colour\")", "\"Colour\" is equal to \"Colour\"")]
    public void A_subject_literal_is_kept_as_written(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("actual.Colour.ToString().ShouldBe(\"Blue\")", "Actual colour should be \"Blue\"")]
    [InlineData("actual.Colour.ToString().Should().Be(\"Blue\")", "Actual colour should be \"Blue\"")]
    [InlineData("await Assert.That(actual.Colour.ToString()).IsEqualTo(\"Blue\")", "Actual colour is equal to \"Blue\"")]
    public void To_string_is_read_alike_in_every_library(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("x.Should().Be(note)")]
    [InlineData("x.ShouldBe(note)")]
    public void A_value_holding_to_string_is_not_rewritten(string expression)
    {
        var label = AssertionExpressionFormatter.Format(
            expression, new Dictionary<string, string> { ["note"] = "call to string() now" });

        Assert.Equal("X should be 'call to string() now'", label);
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
    [InlineData("claim.Should()\n    .NotBeNull()\n    .And\n    .Be(value)", "Claim should not be null and be value")]
    [InlineData("claim.Should() .NotBeNull() .And .Be(value)", "Claim should not be null and be value")]
    [InlineData("claim.Should()\r\n    .NotBeNull()\r\n    .And\r\n    .HaveLength(length)", "Claim should not be null and have length length")]
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
    [InlineData("value.Should().Be(map[key]!.Id)", "Value should be map[key].Id")]
    public void A_null_forgiving_bang_is_still_dropped(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("result.ShouldBe(5)", "Result should be 5")]
    [InlineData("name.ShouldNotBeNull()", "Name should not be null")]
    [InlineData("total.ShouldBeGreaterThan(3)", "Total should be greater than 3")]
    [InlineData("order.ShouldBeOfType<Order>()", "Order should be of type Order")]
    [InlineData("order.ShouldSatisfyAllConditions(() => order.Id.ShouldBe(2), () => order.Name.ShouldBe(\"b\"))", "Order should satisfy all conditions")]
    [InlineData("Should.Throw<InvalidOperationException>(() => Run())", "Should throw InvalidOperationException")]
    [InlineData("await Should.ThrowAsync<InvalidOperationException>(async () => await Run())", "Should throw InvalidOperationException")]
    [InlineData("Should.NotThrow(() => Run())", "Should not throw")]
    [InlineData("action.ShouldThrow<ArgumentException>()", "Action should throw ArgumentException")]
    [InlineData("result.ShouldBe(expected, customMessage: \"why\")", "Result should be expected")]
    [InlineData("items.ShouldAllBe(x => x > 0)", "Items should all be [ x => x > 0 ]")]
    [InlineData("flags.ShouldHaveFlag(Flags.A)", "Flags should have flag A")]
    [InlineData("order?.Name.ShouldBe(\"a\")", "Order name should be \"a\"")]
    [InlineData("greeting.ShouldBe(\"Hi!\")", "Greeting should be \"Hi!\"")]
    public void Shouldly_labels_read_as_sentences(string expression, string expected)
    {
        // By rule rather than by table (SHOULDLY_ASSERTIONS_PLAN section 3.5): the words after "Should", split on
        // case; no Async; a type argument without its brackets; an action Shouldly runs, and a custom message
        // passed by name, not shown.
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Fact]
    public void A_Shouldly_label_takes_resolved_values()
    {
        var label = AssertionExpressionFormatter.Format("result.ShouldBe(expected)", new Dictionary<string, string> { ["expected"] = "5" });

        Assert.Equal("Result should be '5'", label);
    }

    [Theory]
    [InlineData("Assert.That(result).IsEqualTo(5)", "Result is equal to 5")]
    [InlineData("await Assert.That(items).IsNotEmpty()", "Items is not empty")]
    [InlineData("Assert.That(order.Total).IsGreaterThan(3).And.IsLessThan(9)", "Order total is greater than 3 and is less than 9")]
    [InlineData("await Assert.That(text).Contains(\"(\")", "Text contains \"(\"")]
    public void TUnit_labels_read_as_sentences(string expression, string expected)
    {
        // It was drawn as the raw code (SHOULDLY_ASSERTIONS_PLAN section 8, item 2).
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Fact]
    public void A_TUnit_label_takes_resolved_values()
    {
        var label = AssertionExpressionFormatter.Format("await Assert.That(result).IsEqualTo(expected)", new Dictionary<string, string> { ["expected"] = "5" });

        Assert.Equal("Result is equal to '5'", label);
    }

    [Theory]
    [InlineData("result.SplitPercentage.ToString(CultureInfo.InvariantCulture).ShouldNotBeNullOrEmpty()", "Result split percentage to string(CultureInfo.InvariantCulture) should not be null or empty")]
    [InlineData("result.SplitPercentage.ToString(CultureInfo.InvariantCulture).Should().NotBeNullOrEmpty()", "Result split percentage to string(CultureInfo.InvariantCulture) should not be null or empty")]
    [InlineData("await Assert.That(result.SplitPercentage.ToString(CultureInfo.InvariantCulture)).IsNotNullOrEmpty()", "Result split percentage to string(CultureInfo.InvariantCulture) is not null or empty")]
    [InlineData("result.Date.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture).ShouldBe(\"2026-10-27\")", "Result date to string(\"yyyy-MM-dd\", CultureInfo.InvariantCulture) should be \"2026-10-27\"")]
    [InlineData("result.Amount.ToString(\"N2\").ShouldBe(\"1,234.00\")", "Result amount to string(\"N2\") should be \"1,234.00\"")]
    [InlineData("result.Items.Count(x => x.IsActive).ShouldBe(3)", "Result items count(x => x.IsActive) should be 3")]
    [InlineData("result.Items.Where(x => x.IsActive).Count().ShouldBe(3)", "Result items where(x => x.IsActive) count() should be 3")]
    [InlineData("result.Items.Single(x => x.Id == expected.Id).Name.ShouldBe(\"A\")", "Result items single(x => x.Id == expected.Id) name should be \"A\"")]
    [InlineData("result.Items.Select(i => i.Name).ShouldContain(\"A\")", "Result items select(i => i.Name) should contain \"A\"")]
    [InlineData("result.Get<Customer>().Name.ShouldBe(\"A\")", "Result get<Customer>() name should be \"A\"")]
    [InlineData("result.Get<(int, string)>().Name.ShouldBe(\"a\")", "Result get<(int, string)>() name should be \"a\"")]
    [InlineData("result.Get<List<int>>().Count.ShouldBe(2)", "Result get<List<int>>() count should be 2")]
    [InlineData("dict[Keys.Primary].ShouldBe(1)", "Dict[Keys.Primary] should be 1")]
    [InlineData("result.Lines[index].Total.ShouldBe(3)", "Result lines[index] total should be 3")]
    [InlineData("result.Items?[0].Name.ShouldBe(\"a\")", "Result items[0] name should be \"a\"")]
    [InlineData("result.Chars[')'].ShouldBe(1)", "Result chars[')'] should be 1")]
    [InlineData("result.Text.Substring(0, 1).ShouldBe(\")\")", "Result text substring(0, 1) should be \")\"")]
    [InlineData("Math.Round(result.Total, 2).ShouldBe(10.5m)", "Math round(result.Total, 2) should be 10.5m")]
    [InlineData("string.Join(\",\", result.Ids).ShouldBe(\"a,b\")", "String join(\",\", result.Ids) should be \"a,b\"")]
    [InlineData("DateTime.Parse(result.When).Year.ShouldBe(2026)", "Date time parse(result.When) year should be 2026")]
    [InlineData("JsonSerializer.Serialize(result.Body).ShouldContain(\"id\")", "Json serializer serialize(result.Body) should contain \"id\"")]
    [InlineData("doc.RootElement.GetProperty(Endpoints.Swagger.PancakesPath).ValueKind.ShouldBe(JsonValueKind.Object)", "Doc root element get property(Endpoints.Swagger.PancakesPath) value kind should be Object")]
    [InlineData("response.Headers.GetValues(\"X-Id\").First().ShouldBe(\"1\")", "First value of response headers get values(\"X-Id\") should be \"1\"")]
    [InlineData("result.Value.ToString(CultureInfo.InvariantCulture).ToUpperInvariant().ShouldBe(\"X\")", "Result value to string(CultureInfo.InvariantCulture) to upper invariant() should be \"X\"")]
    public void A_call_in_the_subject_keeps_its_arguments_as_written(string expression, string expected)
    {
        // The subject was cut at every dot, inside a call's brackets too, and each piece split on case: "to string(
        // culture info invariant culture)" (#210). A link's name reads as words; its brackets keep their code.
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("((int)response.StatusCode).ShouldBe(200)", "((int)response.StatusCode) should be 200")]
    [InlineData("(result.Total + result.Tax).ShouldBe(12)", "(result.Total + result.Tax) should be 12")]
    [InlineData("(await Fail(connection, \"SELEC 1\")).Message.ShouldContain(\"syntax\")", "(await Fail(connection, \"SELEC 1\")) message should contain \"syntax\"")]
    [InlineData("new Uri(result.Link).Host.ShouldBe(\"example.com\")", "New Uri(result.Link) host should be \"example.com\"")]
    [InlineData("(flag ? a : b).ShouldBe(1)", "(flag ? a : b) should be 1")]
    [InlineData("await Assert.That(result.Total > 0 && result.Tax > 0).IsTrue()", "result.Total > 0 && result.Tax > 0 is true")]
    public void A_subject_that_is_not_a_member_path_is_kept_as_written(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("Assert.That(() => _client.GetOrderAsync(r)).Throws<InvalidOperationException>()", "Client get order async(r) throws <InvalidOperationException>")]
    [InlineData("Assert.That(async () => await _client.GetOrderAsync(r)).Throws<InvalidOperationException>()", "Client get order async(r) throws <InvalidOperationException>")]
    public void A_lambda_subject_reads_as_its_body(string expression, string expected)
    {
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("result.Items.Count(x =>\n    x.IsActive).ShouldBe(2)", "Result items count(x => x.IsActive) should be 2")]
    [InlineData("result.Date.ToString(\n    \"yyyy-MM-dd\",\n    CultureInfo.InvariantCulture).ShouldBe(\"2026-10-27\")", "Result date to string(\"yyyy-MM-dd\", CultureInfo.InvariantCulture) should be \"2026-10-27\"")]
    [InlineData("result.Items\n    .Single(x => x.Id == expected.Id)\n    .Name.ShouldBe(\"A\")", "Result items single(x => x.Id == expected.Id) name should be \"A\"")]
    [InlineData("result.Name.ShouldBe(\n    nameof(CurrencyCode.GBP))", "Result name should be 'GBP'")]
    [InlineData("items.Should().Contain(x =>\n    x.Name == \"A\")", "Items should contain [ x => x.Name == \"A\" ]")]
    [InlineData("result.Value.Should().Be(\n    expected.Value)", "Result value should be expected.Value")]
    [InlineData("Assert.Equal(\n    expected,\n    actual)", "Assert.Equal(expected, actual)")]
    [InlineData("result.Date.ToString( \"yyyy-MM-dd\", CultureInfo.InvariantCulture).ShouldBe(\"2026-10-27\")", "Result date to string(\"yyyy-MM-dd\", CultureInfo.InvariantCulture) should be \"2026-10-27\"")]
    [InlineData("result.Items.Count(x => x.IsActive ).ShouldBe(2)", "Result items count(x => x.IsActive) should be 2")]
    [InlineData("items.Should().Contain(x => x.Name == \"A\" )", "Items should contain [ x => x.Name == \"A\" ]")]
    [InlineData("result.Name.ShouldBe(\"a  b\\n\")", "Result name should be \"a  b\\n\"")]
    public void A_statement_over_several_lines_reads_on_one_line(string expression, string expected)
    {
        // Track.That passes its code as written, line breaks and indentation included; the weave joins a statement's
        // lines with a space, which leaves one after a bracket that opened a line. A literal keeps its own spaces.
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("result.CurrencyIsoAlpha3.ShouldBe(nameof(CurrencyCode.GBP))", "Result currency iso alpha3 should be 'GBP'")]
    [InlineData("result.Name.ShouldBe(nameof(Customer))", "Result name should be 'Customer'")]
    [InlineData("result.Name.ShouldBe(nameof(List<int>))", "Result name should be 'List'")]
    [InlineData("result.Name.ShouldBe(nameof(Dictionary<string, List<int>>.KeyCollection))", "Result name should be 'KeyCollection'")]
    [InlineData("result.Name.ShouldBe(nameof(@class))", "Result name should be 'class'")]
    [InlineData("result.Name.ShouldBe(nameof(this.Name))", "Result name should be 'Name'")]
    [InlineData("result.Name.ShouldBe(nameof(global::System.String))", "Result name should be 'String'")]
    [InlineData("result.Name.ShouldBe(nameof(Customer) + \"s\")", "Result name should be 'Customer' + \"s\"")]
    [InlineData("result.Name.ShouldBe($\"{nameof(Customer)}-1\")", "Result name should be $\"{nameof(Customer)}-1\"")]
    [InlineData("result.Errors.ShouldContainKey(nameof(Request.Email))", "Result errors should contain key 'Email'")]
    [InlineData("result.Name.Should().Be(nameof(CurrencyCode.GBP))", "Result name should be 'GBP'")]
    [InlineData("await Assert.That(result.Name).IsEqualTo(nameof(CurrencyCode.GBP))", "Result name is equal to 'GBP'")]
    public void A_nameof_argument_reads_as_its_value(string expression, string expected)
    {
        // nameof(…) is the string the compiler makes of its operand's last name, so it reads as that value does
        // (#210): it was drawn as its code.
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Fact]
    public void A_value_the_weave_passed_wins_over_the_nameof_reading()
    {
        // With a method named nameof in scope, nameof(y) is a call, and the weave passes the value it returned.
        var label = AssertionExpressionFormatter.Format(
            "x.ShouldBe(nameof(y))", new Dictionary<string, string> { ["nameof(y)"] = "z" });

        Assert.Equal("X should be 'z'", label);
    }

    [Theory]
    [InlineData("(await act.Should().ThrowAsync<ClickHouseServerException>()).Which.Message.Should().StartWith(\"Code: 60.\")", "(await act.Should().ThrowAsync<ClickHouseServerException>()) which message should start with \"Code: 60.\"")]
    [InlineData("(await client.GetAsync(\"/x\")).StatusCode.Should().Be(HttpStatusCode.OK)", "(await client.GetAsync(\"/x\")) status code should be OK")]
    [InlineData("\"a.Should().b\".Should().Be(\"x\")", "\"a.Should().b\" should be \"x\"")]
    [InlineData("list.Select(x => x.Should().BeOfType<int>()).Should().HaveCount(3)", "List select(x => x.Should().BeOfType<int>()) should have count 3")]
    public void A_FluentAssertions_subject_ends_at_its_own_Should(string expression, string expected)
    {
        // The subject ended at the first .Should(). anywhere, inside brackets or a literal too (#210, F6).
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("x.Should().Be(\"Rock.And.Roll\")", "X should be \"Rock.And.Roll\"")]
    [InlineData("act.Should().Throw<InvalidOperationException>().WithMessage(\"*.And.*\")", "Act should throw <InvalidOperationException> with message \"*.And.*\"")]
    [InlineData("await Assert.That(text).IsEqualTo(\"Rock.And.Roll\")", "Text is equal to \"Rock.And.Roll\"")]
    [InlineData("await Assert.That(text).IsEqualTo(\"a.Or.b\")", "Text is equal to \"a.Or.b\"")]
    [InlineData("x.Should().BeEquivalentTo(expected, o => o.Excluding(e => e.Id).And.Excluding(e => e.At))", "X should be equivalent to [ expected, o => o.Excluding(e => e.Id).And.Excluding(e => e.At) ]")]
    [InlineData("x.Should().NotBeNull().And.HaveCount(2)", "X should not be null and have count 2")]
    [InlineData("await Assert.That(x).IsGreaterThan(3).And.IsLessThan(9)", "X is greater than 3 and is less than 9")]
    [InlineData("await Assert.That(x).IsLessThan(3).Or.IsGreaterThan(9)", "X is less than 3 or is greater than 9")]
    [InlineData("result.Should().NotBeNull().And.BeOfType<Order>().Which.Total.Should().Be(5)", "Result should not be null and be of type <Order>, which total should be 5")]
    public void And_and_Or_are_read_outside_literals_and_brackets(string expression, string expected)
    {
        // .And. and .Or. were cut wherever they stood, in a literal or a lambda too, and what followed them was not
        // read (#210, F9 and Q8).
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("await Assert.That(price).IsEqualTo(10.0).Within(0.5)", "Price is equal to 10.0 within 0.5")]
    [InlineData("await Assert.That(name).IsEqualTo(\"bob\").IgnoringCase()", "Name is equal to \"bob\" ignoring case")]
    [InlineData("await Assert.That(act).Throws<InvalidOperationException>().WithMessage(\"boom\")", "Act throws <InvalidOperationException> with message \"boom\"")]
    [InlineData("Should.Throw<InvalidOperationException>(() => service.Run()).Message.ShouldContain(\"boom\")", "Should throw InvalidOperationException, message should contain \"boom\"")]
    [InlineData("Should.Throw<ArgumentException>(act).ParamName.ShouldBe(\"id\")", "Should throw ArgumentException act, param name should be \"id\"")]
    [InlineData("Should.Throw<AggregateException>(() => run()).InnerExceptions.ShouldHaveSingleItem()", "Should throw AggregateException, inner exceptions should have single item")]
    [InlineData("Should.Throw<MappingException>(() => Map()).InnerException.ShouldBeOfType<NotSupportedException>()", "Should throw MappingException, inner exception should be of type NotSupportedException")]
    [InlineData("log.Warnings.ShouldHaveSingleItem().Exception.ShouldBeOfType<OutOfOrderException>().Name.ShouldBe(\"seven\")", "Log warnings should have single item, exception should be of type OutOfOrderException, name should be \"seven\"")]
    [InlineData("Should.Throw<ConfigurationException>(() => Validate()).Errors.Single().Names.Single().ShouldBe(\"Id\")", "Should throw ConfigurationException, errors single names single should be \"Id\"")]
    [InlineData("var ex = Should.Throw<InvalidOperationException>(() => service.Run())", "Should throw InvalidOperationException")]
    [InlineData("(await Should.ThrowAsync<HttpRequestException>(act)).StatusCode.ShouldBe(HttpStatusCode.NotFound)", "(await Should.ThrowAsync<HttpRequestException>(act)) status code should be NotFound")]
    [InlineData("dict.Should().ContainKey(\"a\").WhoseValue.Should().Be(1)", "Dict should contain key \"a\", whose value should be 1")]
    [InlineData("act.Should().Throw<ClickHouseEmulatorException>().Which.Code.Should().Be(456)", "Act should throw <ClickHouseEmulatorException>, which code should be 456")]
    [InlineData("act.Should().Throw<InvalidOperationException>().WithMessage(\"boom\")", "Act should throw <InvalidOperationException> with message \"boom\"")]
    [InlineData("x.Should().ContainSingle().Which.Name.Should().Be(\"a\")", "X should contain single, which name should be \"a\"")]
    [InlineData("var row = list.Rows.Should().ContainSingle().Subject", "List rows should contain single")]
    [InlineData("price.Should().BeApproximately(10.0, 0.5)", "Price should be approximately 10.0, 0.5")]
    [InlineData("result.Should().BeOfType<List<int>>()", "Result should be of type <List<int>>")]
    [InlineData("result.ShouldBeOfType<Dictionary<string, List<int>>>()", "Result should be of type Dictionary<string, List<int>>")]
    public void A_chain_reads_each_call_with_its_own_arguments(string expression, string expected)
    {
        // A chain was read as its first call's method with its last call's arguments, and a nested type argument list
        // was cut at its first '>' (#210, F10 and F11).
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("async () =>\n        {\n            await Task.Yield();\n            \"x\".Should().Be(expected);\n        }", "\"x\" should be expected")]
    [InlineData("() => { a.ShouldBe(1); b.Total.ShouldBe(2); }", "A should be 1; B total should be 2")]
    [InlineData("async () => await act.Should().ThrowAsync<InvalidOperationException>()", "Act should throw async <InvalidOperationException>")]
    [InlineData("() =>\n    x.ShouldBe(1)", "X should be 1")]
    [InlineData("() => { DoSomething(); }", "{ DoSomething(); }")]
    public void A_lambda_head_and_a_block_body_read_as_their_assertions(string expression, string expected)
    {
        // Only an exact "() => " was stripped, so Track.ThatAsync's async lambdas and block bodies read as code (#210, F16).
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("Console.WriteLine((await Should.ThrowAsync<InvalidOperationException>(() => _client.GetOrderAsync(r))).Message)", "Should throw InvalidOperationException")]
    [InlineData("results.ForEach(r => r.Should().BeTrue())", "results.ForEach(r => r.Should().BeTrue())")]
    [InlineData("items.ForEach(x => (x.Should().BeTrue()))", "items.ForEach(x => (x.Should().BeTrue()))")]
    [InlineData("() => results.ForEach(r => r.Should().BeTrue())", "results.ForEach(r => r.Should().BeTrue())")]
    public void An_assertion_inside_other_code_reads_as_that_assertion(string expression, string expected)
    {
        // An assertion in a lambda's body runs elsewhere (for each element, later), so that code stays as written.
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("ex.Message.ShouldContain(\"opts.Events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, \\\"enrollee\\\");\")", "Ex message should contain \"opts.Events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, \\\"enrollee\\\");\"")]
    [InlineData("Should.Throw<InvalidOperationException>(() => opts.Events.RegisterTagType<EnrolleeId, string>(x => x.Value.ToString(), \"enrollee\")).Message.ShouldContain(\"already registered\")", "Should throw InvalidOperationException, message should contain \"already registered\"")]
    public void Lambda_brackets_and_the_argument_split_count_code_not_literals(string expression, string expected)
    {
        // A "=>" in a literal put the arguments in lambda brackets, and Shouldly's argument split counted '<' and '>'
        // as brackets, the '>' of a lambda's arrow among them (#210, F14 and F15).
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("result.ShouldBe(5 /* five */)", "Result should be 5")]
    [InlineData("result.Total /* net */.ShouldBe(5)", "Result total should be 5")]
    [InlineData("result.Total.ShouldBe(\n    // the discount is applied first\n    expected.Total)", "Result total should be expected.Total")]
    [InlineData("result.Name.ShouldBe(\"a // b /* c */\")", "Result name should be \"a // b /* c */\"")]
    [InlineData("result.Count.ShouldBe(10 / 2)", "Result count should be 10 / 2")]
    public void A_comment_in_a_statement_is_not_read(string expression, string expected)
    {
        // A comment reached the label as code (#210, F12). A "//" comment ends at its line break: in text whose lines the
        // weave joined there is none, and the weave drops a line's comment before it joins the lines.
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("$\"{(flag ? \"a.b\" : \"c\")}x\".Length.ShouldBe(4)", "$\"{(flag ? \"a.b\" : \"c\")}x\" length should be 4")]
    [InlineData("result.Name.ShouldBe($\"{(flag ? \"a.b\" : \"c\")}.Should().\")", "Result name should be $\"{(flag ? \"a.b\" : \"c\")}.Should().\"")]
    [InlineData("$\"{{{x}\".Length.ShouldBe(2)", "$\"{{{x}\" length should be 2")]
    public void An_interpolated_string_ends_at_its_own_closing_quote(string expression, string expected)
    {
        // A quote inside a hole ended the string, so the rest was read as code (#210, F13).
        Assert.Equal(expected, AssertionExpressionFormatter.Format(expression));
    }

    [Theory]
    [InlineData("breakfastprovider.txt", false)]
    [InlineData("breakfastprovider-multiline.txt", true)]
    [InlineData("breakfastprovider-multiline-joined.txt", false)]
    [InlineData("kronikol-examples.txt", false)]
    public void No_label_holds_a_space_inside_a_bracket_or_two_spaces(string corpus, bool overLines)
    {
        // Every assertion statement of BreakfastProvider's suites and of the examples, one a line
        // (ASSERTION_LABELS_PLAN's corpus): the one-line ones, those spread over lines as Track.That passes them (a "\n"
        // standing for each line break), and those joined as the weave joins them. Outside its literals and the brackets
        // put round a lambda, no label opens or closes a bracket on a space, holds two spaces or holds a line break
        // (#210: 54 of BreakfastProvider's 1,016 one-line labels did, and each of its 49 over lines broke).
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "AssertionLabels", corpus);
        var statements = File.ReadAllLines(path).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();
        Assert.True(statements.Count > 40, $"{corpus} holds {statements.Count} statements");

        var stray = statements
            .Select(statement => (statement, label: AssertionExpressionFormatter.Format(overLines ? statement.Replace("\\n", "\n") : statement)))
            .Where(x => HasStraySpace(x.label))
            .Select(x => $"{x.statement}\n    => {x.label}")
            .ToList();

        Assert.True(stray.Count == 0, $"{stray.Count} of {statements.Count} labels:\n{string.Join("\n", stray)}");
    }

    private static bool HasStraySpace(string label)
    {
        if (label.Contains('\n') || label.Contains('\r'))
            return true;

        // The code outside literals, with the lambda brackets ("[ x => … ]") taken out.
        var literal = AssertionExpressionFormatter.LiteralMask(label);
        var code = new StringBuilder();
        for (var i = 0; i < label.Length; i++)
            code.Append(literal[i] ? '"' : label[i]);
        var bare = System.Text.RegularExpressions.Regex.Replace(code.ToString(), @" \[ ([^\[\]]*=>.*?) \](?=,|$| )", " <$1>");

        return bare.Contains("( ") || bare.Contains("[ ") || bare.Contains(" )") || bare.Contains(" ]") || bare.Contains("  ");
    }
}
