using Kronikol.Tracking;

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
    public void AssertionPassedWithValues_null_intermediate_in_chain_shows_null()
    {
        using var scope = TestIdentityScope.Begin(_testId, _testId);

        var options = new TestOptions { Inner = null };

        Track.AssertionPassedWithValues(
            "result.Should().Be(options.Inner.MaxRetries)",
            ["options"],
            [options],
            "Test.cs", 80);

        var logs = GetAssertionLogs();
        Assert.NotEmpty(logs);
        Assert.Contains("'null'", logs[0].PlantUml!);
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

    // --- Test helpers ---

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
