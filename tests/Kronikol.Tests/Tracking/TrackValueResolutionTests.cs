using Kronikol.Tracking;
using Shouldly;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TrackValueResolutionTests : IDisposable
{
    private readonly string _testId = $"TrackValueResolutionTests.{Guid.NewGuid():N}";

    public void Dispose()
    {
        Track.TestIdResolver = null;
    }

    private List<RequestResponseLog> GetAssertionLogs() =>
        RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId && l.PlantUml is not null && l.PlantUml.Contains("<<assertionNote>>"))
            .ToList();

    // --- FormatValue tests ---

    [Fact]
    public void FormatValue_null_returns_null_string()
    {
        Assert.Equal("null", Track.FormatValue(null));
    }

    [Fact]
    public void FormatValue_short_string_returns_as_is()
    {
        Assert.Equal("hello", Track.FormatValue("hello"));
    }

    [Fact]
    public void FormatValue_long_string_truncates_at_100_chars()
    {
        var longString = new string('x', 120);
        var result = Track.FormatValue(longString);
        Assert.Equal(new string('x', 100) + "...", result);
    }

    [Fact]
    public void FormatValue_integer_returns_string_representation()
    {
        Assert.Equal("42", Track.FormatValue(42));
    }

    [Fact]
    public void FormatValue_enum_returns_name()
    {
        Assert.Equal("Wednesday", Track.FormatValue(DayOfWeek.Wednesday));
    }

    [Fact]
    public void FormatValue_small_int_collection_shows_inline_values()
    {
        var list = new List<int> { 1, 2, 3 };
        Assert.Equal("[ 1, 2, 3 ]", Track.FormatValue(list));
    }

    [Fact]
    public void FormatValue_small_string_collection_shows_quoted_values()
    {
        var list = new List<string> { "Milk", "Sugar", "Brandy" };
        Assert.Equal("""[ "Milk", "Sugar", "Brandy" ]""", Track.FormatValue(list));
    }

    [Fact]
    public void FormatValue_small_enum_collection_shows_names()
    {
        var list = new[] { DayOfWeek.Monday, DayOfWeek.Friday };
        Assert.Equal("[ Monday, Friday ]", Track.FormatValue(list));
    }

    [Fact]
    public void FormatValue_empty_collection_returns_zero_items()
    {
        Assert.Equal("[0 items]", Track.FormatValue(Array.Empty<string>()));
    }

    [Fact]
    public void FormatValue_large_collection_shows_count_only()
    {
        var list = Enumerable.Range(1, 11).ToList();
        Assert.Equal("[11 items]", Track.FormatValue(list));
    }

    [Fact]
    public void FormatValue_collection_with_complex_objects_shows_count()
    {
        var list = new List<object> { new ObjectWithBadToString(), new ObjectWithBadToString() };
        Assert.Equal("[2 items]", Track.FormatValue(list));
    }

    [Fact]
    public void FormatValue_collection_with_null_items_shows_inline()
    {
        var list = new List<string?> { "hello", null, "world" };
        Assert.Equal("""[ "hello", null, "world" ]""", Track.FormatValue(list));
    }

    [Fact]
    public void FormatValue_exactly_10_items_shows_inline()
    {
        var list = Enumerable.Range(1, 10).ToList();
        Assert.Equal("[ 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 ]", Track.FormatValue(list));
    }

    [Fact]
    public void FormatValue_object_with_tostring_returning_type_name_returns_null()
    {
        var obj = new ObjectWithBadToString();
        Assert.Null(Track.FormatValue(obj));
    }

    [Fact]
    public void FormatValue_object_with_meaningful_tostring_returns_value()
    {
        var obj = new ObjectWithGoodToString("blah");
        Assert.Equal("blah", Track.FormatValue(obj));
    }

    // --- AssertionPassedWithValues tests ---

    [Fact]
    public void AssertionPassedWithValues_substitutes_variable_value_in_expression()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        Track.AssertionPassedWithValues(
            "response.StatusCode.Should().Be(expected)",
            ["expected"],
            ["OK"],
            "Test.cs", 10);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("'OK'", logs[0].PlantUml!);
        Assert.DoesNotContain("expected", logs[0].PlantUml!);
    }

    [Fact]
    public void AssertionPassedWithValues_substitutes_multiple_variables()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        Track.AssertionPassedWithValues(
            "result.Should().BeInRange(min, max)",
            ["min", "max"],
            [1, 100],
            "Test.cs", 20);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("'1'", logs[0].PlantUml!);
        Assert.Contains("'100'", logs[0].PlantUml!);
    }

    [Fact]
    public void AssertionPassedWithValues_null_variable_shows_null()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        Track.AssertionPassedWithValues(
            "result.Should().Be(expected)",
            ["expected"],
            [null],
            "Test.cs", 30);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("'null'", logs[0].PlantUml!);
    }

    [Fact]
    public void AssertionFailedWithValues_substitutes_variable_and_shows_failure()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        Track.AssertionFailedWithValues(
            "result.Should().Be(expected)",
            "Expected 'OK' but found 'NotFound'",
            ["expected"],
            ["OK"],
            "Test.cs", 40);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("'OK'", logs[0].PlantUml!);
        Assert.Contains("#f8d7da", logs[0].PlantUml!); // fail color
        Assert.Contains("Expected 'OK' but found 'NotFound'", logs[0].PlantUml!);
    }

    [Fact]
    public void AssertionPassedWithValues_empty_arrays_works_without_substitution()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        Track.AssertionPassedWithValues(
            "result.Should().BeTrue()",
            [],
            [],
            "Test.cs", 50);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("be true", logs[0].PlantUml!);
    }

    // --- Dotted path resolution tests ---

    [Fact]
    public void AssertionPassedWithValues_resolves_dotted_property_path()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        var config = new TestConfig { MaxRetries = 5 };

        Track.AssertionPassedWithValues(
            "result.Should().Be(config.MaxRetries)",
            ["config"],
            [config],
            "Test.cs", 60);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("'5'", logs[0].PlantUml!);
    }

    [Fact]
    public void AssertionPassedWithValues_resolves_two_level_dotted_path()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        var options = new TestOptions { Inner = new TestConfig { MaxRetries = 3 } };

        Track.AssertionPassedWithValues(
            "result.Should().Be(options.Inner.MaxRetries)",
            ["options"],
            [options],
            "Test.cs", 70);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("'3'", logs[0].PlantUml!);
    }

    [Fact]
    public void AssertionPassedWithValues_null_intermediate_before_a_plain_dot_shows_the_chain_as_written()
    {
        // The code itself would have thrown at the null, so the chain has no value: it is not the value null (#163).
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        var options = new TestOptions { Inner = null };

        Track.AssertionPassedWithValues(
            "result.Should().Be(options.Inner.MaxRetries)",
            ["options"],
            [options],
            "Test.cs", 80);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("Result should be options.Inner.MaxRetries", logs[0].PlantUml!);
        Assert.DoesNotContain("'null'", logs[0].PlantUml!);
    }

    [Fact]
    public void AssertionPassedWithValues_null_intermediate_before_a_null_conditional_shows_null()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        var options = new TestOptions { Inner = null };

        Track.AssertionPassedWithValues(
            "result.Should().Be(options.Inner?.MaxRetries)",
            ["options"],
            [options],
            "Test.cs", 80);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        // The whole chain is null, not its first link: 4.14.6 read 'null'.MaxRetries.
        Assert.Contains($"{Track.PassSymbol} Result should be 'null'\n", logs[0].PlantUml!);
    }

    [Fact]
    public void AssertionPassedWithValues_also_resolves_root_when_dotted_path_found()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        var config = new TestConfig { MaxRetries = 7 };

        // Expression references both config.MaxRetries and just config won't match
        // because SubstituteResolvedValues does longest-first matching
        Track.AssertionPassedWithValues(
            "result.Should().BeInRange(config.MaxRetries, config.MaxRetries)",
            ["config"],
            [config],
            "Test.cs", 90);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        // Dotted path resolved to 7
        Assert.Contains("'7'", logs[0].PlantUml!);
    }

    // --- A quoted value is the value of exactly the code it replaces (ASSERTION_VALUE_SUBSTITUTION_PLAN, #163) ---

    private static readonly Order Expected = new("A1", Colour.Blue, ["x", "y"])
    {
        Inner = new Inner("v"),
        Box = new Box { Inner = new Inner("v") },
    };

    private string Label(string expression, string[] names, object?[] values)
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);
        var before = GetAssertionLogs().Count;
        Track.AssertionPassedWithValues(expression, names, values, "Test.cs", 1);
        var plantUml = GetAssertionLogs()[before].PlantUml!;
        var line = plantUml.Split('\n').Single(l => l.StartsWith(Track.PassSymbol + " ", StringComparison.Ordinal));
        return line[(Track.PassSymbol.Length + 1)..];
    }

    public static TheoryData<string, string, object?, string> CallsAfterAPath => new()
    {
        // The issue's five statements and the SplitId shape.
        { "actual.Colour.ToString().ShouldBe(expected.Colour.ToString())", "expected", Expected, "should be 'Blue'.ToString()" },
        { "actual.Lines.First().ShouldBe(expected.Lines.First())", "expected", Expected, "should be expected.Lines.First()" },
        { "actual.Colour.ToString().ToLowerInvariant().ShouldBe(expected.Colour.ToString().ToLowerInvariant())", "expected", Expected, "should be 'Blue'.ToString().ToLowerInvariant()" },
        { "actual.Colour.ToString().Should().Be(expected.Colour.ToString())", "expected", Expected, "should be 'Blue'.ToString()" },
        { "actual.Id.ShouldBe(splits.First().Id)", "splits", new List<Order> { Expected }, "should be splits.First().Id" },
        // Their neighbours: a call on a string, a call with arguments, type arguments, a one-segment path, TUnit.
        { "actual.Id.ShouldBe(expected.Id.ToString())", "expected", Expected, "should be 'A1'.ToString()" },
        { "actual.Colour.ToString(\"G\").ShouldBe(expected.Colour.ToString(\"G\"))", "expected", Expected, "should be 'Blue'.ToString(\"G\")" },
        { "actual.Code.ShouldBe(expected.Get<int>())", "expected", Expected, "should be expected.Get<int>()" },
        { "actual.Id.ShouldBe(id.Trim())", "id", "A1", "should be 'A1'.Trim()" },
        { "await Assert.That(actual.Colour.ToString()).IsEqualTo(expected.Colour.ToString())", "expected", Expected, "is equal to 'Blue'.ToString()" },
    };

    [Theory]
    [MemberData(nameof(CallsAfterAPath))]
    public void A_call_after_a_path_is_not_read_as_a_member(string expression, string name, object? value, string argumentPart)
    {
        var label = Label(expression, [name], [value]);

        // The note escapes the creole in its code (#210, Q9): Get<int>() is written Get~<int>(), and draws as written.
        Assert.DoesNotContain("'null'", label);
        Assert.EndsWith(" " + global::Kronikol.PlantUml.PlantUmlCreator.EscapeCreoleMarkup(argumentPart), label);
    }

    [Theory]
    [InlineData("actual.Lines.Count().ShouldBe(expected.Lines.Count())", "should be expected.Lines.Count()")]
    [InlineData("actual.Lines.Count(l => l == \"x\").ShouldBe(expected.Lines.Count(l => l == \"x\"))", "should be [ expected.Lines.Count(l => l == \"x\") ]")]
    [InlineData("actual.Lines.Count(l => l == \"x\").Should().Be(expected.Lines.Count(l => l == \"x\"))", "should be [ expected.Lines.Count(l => l == \"x\") ]")]
    public void A_call_named_like_a_property_is_not_read_as_the_property(string expression, string argumentPart)
    {
        // List<T>.Count is 2; Enumerable.Count(l => l == "x") returned 1. The property's value is not the call's.
        var label = Label(expression, ["expected"], [Expected]);

        Assert.DoesNotContain("'2'", label);
        Assert.EndsWith(" " + argumentPart, label);
    }

    public static TheoryData<string, string, object?, string> UnreadableChains => new()
    {
        { "actual.ShouldBe(faulty.Throws)", "faulty", new Faulty(), "should be faulty.Throws" },
        { "actual.ShouldBe(expected.NoSuchMember)", "expected", Expected, "should be expected.NoSuchMember" },
        { "actual.ShouldBe(expected.Inner.Value)", "expected", Expected with { Inner = null }, "should be expected.Inner.Value" },
        { "actual.ShouldBe(expected.Lines[0])", "expected", Expected with { Lines = null! }, "should be expected.Lines[0]" },
    };

    [Theory]
    [MemberData(nameof(UnreadableChains))]
    public void An_unreadable_chain_shows_no_value(string expression, string name, object? value, string argumentPart)
    {
        // A getter that throws, a member that is not there, and a null before a plain '.' (where the code itself
        // would have thrown) are not the value null: the chain is shown as written.
        var label = Label(expression, [name], [value]);

        Assert.DoesNotContain("'", label);
        Assert.EndsWith(" " + argumentPart, label);
    }

    [Theory]
    [InlineData("actual.ShouldBe(expected.Inner?.Value)", "should be 'null'")]
    [InlineData("actual.ShouldBe(expected.Inner?.Value!)", "should be 'null'")]
    [InlineData("actual.ShouldBe(expected.Inner?.Value.Length)", "should be 'null'")]
    [InlineData("actual.ShouldBe(expected.Inner?.Value.ToString())", "should be 'null'")]
    [InlineData("actual.ShouldBe(expected.Box!.Inner?.Value)", "should be 'null'")]
    public void A_null_before_a_null_conditional_reads_null(string expression, string argumentPart)
    {
        // C# gives the whole rest of the chain the value null when '?.' meets a null.
        var expected = Expected with { Inner = null, Box = new Box() };

        var label = Label(expression, ["expected"], [expected]);

        Assert.EndsWith(" " + argumentPart, label);
    }

    [Fact]
    public void A_null_conditional_over_a_value_reads_the_value()
    {
        var label = Label("actual.ShouldBe(expected.Inner?.Value)", ["expected"], [Expected]);

        Assert.EndsWith(" should be 'v'", label);
    }

    [Theory]
    [InlineData("actual.ShouldBe(expected.Lines[0])", "should be expected.Lines[0]")]
    [InlineData("plain.ShouldBeSameAs(expected.Plain)", "should be same as expected.Plain")]
    [InlineData("actual.ShouldBe(expected.Inner!.Value)", "should be 'v'")]
    [InlineData("actual.ShouldBe(expected.Box.Inner.Value.Length)", "should be '1'")]
    [InlineData("actual.ShouldBe(expected.Box.Inner.Value.ToString().Length)", "should be 'v'.ToString().Length")]
    [InlineData("actual.ShouldBe(expected\n    .Box\n    .Inner!\n    .Value)", "should be 'v'")]
    public void A_key_is_not_substituted_inside_a_longer_chain(string expression, string argumentPart)
    {
        // An index, a property that does not print under a root that does, a chain after '!', a chain longer than
        // three segments, and one written over several lines: each value stands for exactly the code it was read for.
        var label = Label(expression, ["expected"], [Expected]);

        Assert.DoesNotContain("Order {", label);
        Assert.DoesNotContain("[ \"x\", \"y\" ]", label);
        Assert.EndsWith(" " + argumentPart, label);
    }

    [Fact]
    public void A_root_is_quoted_only_where_it_stands_alone()
    {
        var label = Label("actual.ShouldBe(id, id.Length)", ["id"], ["A1"]);

        Assert.EndsWith(" should be 'A1', '2'", label);
    }

    [Fact]
    public void A_captured_field_written_after_this_reads_its_chain()
    {
        var label = Label("actual.ShouldBe(this._expected.Colour)", ["_expected"], [Expected]);

        Assert.EndsWith(" should be 'Blue'", label);
    }

    [Fact]
    public void A_chain_inside_nameof_is_not_read()
    {
        // nameof(…) reads as the string it makes (#210), never as the chain's value.
        var label = Label("actual.ShouldBe(nameof(expected.Colour))", ["expected"], [Expected]);

        Assert.EndsWith(" should be 'Colour'", label);
        Assert.DoesNotContain("Blue", label);
    }

    [Fact]
    public void An_assertion_note_escapes_the_creole_in_its_code()
    {
        // A label is code, not markup anyone wrote (#210, Q9): two "" on one line drew the text between them in
        // monospace, and a type argument <b> drew the rest bold. The note escapes them as a payload's are.
        var label = Label("actual.Get<b>().ShouldBe(\"\" + \"\")", ["actual"], [Expected]);

        Assert.Equal(global::Kronikol.PlantUml.PlantUmlCreator.EscapeCreoleMarkup("Actual get<b>() should be \"\" + \"\""), label);
        Assert.NotEqual("Actual get<b>() should be \"\" + \"\"", label);
    }

    [Fact]
    public void A_nameof_argument_reads_as_its_value_through_the_weave_and_Track_That()
    {
        // #210's W02: the weave passes no value for a constant, nor does Track.That find one in its closure, so the
        // label read nameof's code.
        var woven = Label("actual.ShouldBe(nameof(DayOfWeek.Monday))", ["actual"], ["Monday"]);

        using var scope = TestIdentityScope.Begin(_testId, _testId);
        var before = GetAssertionLogs().Count;
        var actual = "Monday";
        Track.That(() => actual.ShouldBe(nameof(DayOfWeek.Monday)));
        var tracked = GetAssertionLogs()[before].PlantUml!.Split('\n')
            .Single(l => l.StartsWith(Track.PassSymbol + " ", StringComparison.Ordinal));

        Assert.EndsWith(" should be 'Monday'", woven);
        Assert.EndsWith(" should be 'Monday'", tracked);
    }

    [Fact]
    public void A_value_inside_an_index_is_substituted_while_the_indexed_chain_stays_as_written()
    {
        var map = new Dictionary<string, Order> { ["k1"] = Expected };

        var label = Label("actual.ShouldBe(map[key].Id)", ["map", "key"], [map, "k1"]);

        Assert.EndsWith(" should be map['k1'].Id", label);
    }

    // --- An argument's own value, passed by the weave under the argument's text (ASSERTION_VALUE_SUBSTITUTION_PLAN Q3) ---

    [Fact]
    public void An_argument_value_replaces_the_whole_argument()
    {
        var label = Label("actual.Colour.ToString().ShouldBe(expected.Colour.ToString())",
            ["expected", "expected.Colour.ToString()"], [Expected, "Blue"]);

        Assert.EndsWith(" should be 'Blue'", label);
    }

    [Fact]
    public void An_argument_that_was_not_evaluated_falls_back_to_the_values_read()
    {
        // The weave starts an argument's value as a plain object, which prints as its type name: the statement threw
        // before the argument was evaluated, and the label reads the variable's chain as it would without the value.
        var label = Label("actual.Colour.ToString().ShouldBe(expected.Colour.ToString())",
            ["expected", "expected.Colour.ToString()"], [Expected, new object()]);

        Assert.EndsWith(" should be 'Blue'.ToString()", label);
    }

    [Fact]
    public void An_argument_value_wins_over_a_value_read_afterwards()
    {
        // The argument's value is what the assertion compared; the variable's member is read after the statement ran.
        var label = Label("actual.ShouldBe(expected.Code)", ["expected", "expected.Code"], [Expected, 6]);

        Assert.EndsWith(" should be '6'", label);
    }

    [Fact]
    public void An_argument_value_of_null_reads_null()
    {
        var label = Label("actual.ShouldBe(expected.Missing ?? other)",
            ["expected", "expected.Missing ?? other"], [Expected, null]);

        Assert.EndsWith(" should be 'null'", label);
    }

    [Theory]
    [InlineData("actual.ShouldBe(flag ? expected.Id : \"none\")", "flag ? expected.Id : \"none\"", "A1", " should be 'A1'")]
    [InlineData("actual.ShouldBe(expected.Inner?.Value)", "expected.Inner?.Value", "w", " should be 'w'")]
    [InlineData("actual.ShouldBe(expected\n    .Inner!\n    .Value)", "expected\n    .Inner!\n    .Value", "w", " should be 'w'")]
    [InlineData("actual.ShouldBe(expected.Lines.Count(l => l == \"x\"))", "expected.Lines.Count(l => l == \"x\")", 1, " should be '1'")]
    [InlineData("actual.Should().BeOneOf(expected.Id, flag ? other : \"none\")", "flag ? other : \"none\"", "B2", " should be one of 'A1', 'B2'")]
    public void An_argument_value_is_matched_to_its_argument_as_written(string expression, string argument, object value, string argumentPart)
    {
        // Each value differs from what reading the variable gives ('v', 2), so only the argument's own value passes.
        var label = Label(expression, ["expected", argument], [Expected, value]);

        Assert.EndsWith(argumentPart, label);
    }

    // --- Test helpers ---

    private enum Colour { Red, Blue }

    private sealed record Order(string Id, Colour Colour, List<string> Lines)
    {
        public Inner? Inner { get; init; }
        public Box? Box { get; init; }
        public Plain Plain { get; init; } = new();
        public int Code { get; init; } = 7;
        public T Get<T>() => default!;
    }

    private sealed class Inner(string value)
    {
        public string Value { get; } = value;
        public override string ToString() => $"Inner({Value})";
    }

    private sealed class Box
    {
        public Inner? Inner { get; init; }
    }

    private sealed class Plain
    {
        public string Name { get; init; } = "plain";
    }

    private sealed class Faulty
    {
        public string Throws => throw new InvalidOperationException("boom");
    }

    private class ObjectWithBadToString
    {
        public override string ToString() => nameof(ObjectWithBadToString);
    }

    private class ObjectWithGoodToString(string value)
    {
        public override string ToString() => value;
    }

    private class TestConfig
    {
        public int MaxRetries { get; set; }
    }

    private class TestOptions
    {
        public TestConfig? Inner { get; set; }
    }
}
