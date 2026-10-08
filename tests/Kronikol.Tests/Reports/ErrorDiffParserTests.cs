using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ErrorDiffParserTests
{
    [Fact]
    public void Returns_null_when_error_message_is_null()
    {
        var result = ErrorDiffParser.TryParseExpectedActual(null);
        Assert.Null(result);
    }

    [Fact]
    public void Returns_null_when_error_message_is_empty()
    {
        var result = ErrorDiffParser.TryParseExpectedActual("");
        Assert.Null(result);
    }

    [Fact]
    public void Returns_null_when_error_message_has_no_diff_pattern()
    {
        var result = ErrorDiffParser.TryParseExpectedActual("Something went wrong: NullReferenceException");
        Assert.Null(result);
    }

    [Fact]
    public void Parses_xUnit_Assert_Equal_failure()
    {
        var message = """
            Assert.Equal() Failure: Values differ
            Expected: Hello World
            Actual:   Hello Mars
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("Hello World", result.Expected);
        Assert.Equal("Hello Mars", result.Actual);
    }

    [Fact]
    public void Parses_xUnit_Assert_Equal_with_strings()
    {
        var message = """
            Assert.Equal() Failure: Strings differ
                           ↓ (pos 6)
            Expected: "Hello World"
            Actual:   "Hello Mars"
                           ↑ (pos 6)
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("Hello World", result.Expected);
        Assert.Equal("Hello Mars", result.Actual);
    }

    [Fact]
    public void Parses_NUnit_expected_but_was()
    {
        var message = """
              Expected: "Hello World"
              But was:  "Hello Mars"
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("Hello World", result.Expected);
        Assert.Equal("Hello Mars", result.Actual);
    }

    [Fact]
    public void Parses_FluentAssertions_expected_to_be()
    {
        var message = """
            Expected string to be "Hello World" with a length of 11, but "Hello Mars" has a length of 10.
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("Hello World", result.Expected);
        Assert.Equal("Hello Mars", result.Actual);
    }

    [Fact]
    public void Parses_Shouldly_expected_but_was()
    {
        var message = """
            should be
                "Hello World"
            but was
                "Hello Mars"
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("Hello World", result.Expected);
        Assert.Equal("Hello Mars", result.Actual);
    }

    [Fact]
    public void Parses_xUnit_Assert_Equal_numeric()
    {
        var message = """
            Assert.Equal() Failure: Values differ
            Expected: 42
            Actual:   99
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("42", result.Expected);
        Assert.Equal("99", result.Actual);
    }

    [Fact]
    public void Parses_Expected_Actual_multiline_with_whitespace_variations()
    {
        var message = """
            Expected:    foo bar
            Actual:      foo baz
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("foo bar", result.Expected);
        Assert.Equal("foo baz", result.Actual);
    }

    [Fact]
    public void GenerateDiffHtml_highlights_differing_characters()
    {
        var html = ErrorDiffParser.GenerateDiffHtml("abc", "axc");
        Assert.Contains("diff-expected", html);
        Assert.Contains("diff-actual", html);
        Assert.Contains("diff-del", html);
        Assert.Contains("diff-ins", html);
    }

    [Fact]
    public void GenerateDiffHtml_marks_identical_text_without_diff_markers()
    {
        var html = ErrorDiffParser.GenerateDiffHtml("same", "same");
        Assert.DoesNotContain("diff-del", html);
        Assert.DoesNotContain("diff-ins", html);
    }

    [Fact]
    public void GenerateDiffHtml_handles_different_lengths()
    {
        var html = ErrorDiffParser.GenerateDiffHtml("Hello World", "Hello");
        Assert.Contains("diff-del", html);
    }

    [Fact]
    public void Parses_FluentAssertions_expected_to_be_equivalent()
    {
        var message = """
            Expected string to be equivalent to "HELLO WORLD" with a length of 11, but "hello mars" has a length of 10.
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("HELLO WORLD", result.Expected);
        Assert.Equal("hello mars", result.Actual);
    }

    [Fact]
    public void Strips_quotes_from_expected_and_actual()
    {
        var message = """
            Expected: "Hello World"
            Actual:   "Hello Mars"
            """;
        var result = ErrorDiffParser.TryParseExpectedActual(message);
        Assert.NotNull(result);
        Assert.Equal("Hello World", result.Expected);
        Assert.Equal("Hello Mars", result.Actual);
    }

    /// <summary>
    /// SHOULDLY_ASSERTIONS_PLAN F14: the messages as Shouldly 4.3.0 and FluentAssertions 8.9.0 write them
    /// (measured, plan harness), each read for its expected and actual values. Shouldly writes a value bare
    /// unless it is a string, and FluentAssertions names its subject, so only a quoted value with the subject
    /// called "string" was read before.
    /// </summary>
    public static TheoryData<string, string, string> UnquotedFailures() => new()
    {
        { "result\n    should be\n5\n    but was\n3", "5", "3" },
        { "flag\n    should be\nTrue\n    but was\nFalse", "True", "False" },
        { "colour\n    should be\nColour.Green\n    but was\nColour.Red", "Colour.Green", "Colour.Red" },
        { "price\n    should be\n2.25m\n    but was\n1.5m", "2.25m", "1.5m" },
        { "name\n    should be\nnull\n    but was\n\"a\"", "null", "a" },
        { "none\n    should be\n\"b\"\n    but was\nnull", "b", "null" },
        { "date\n    should be\n2026-10-09T00:00:00.0000000\n    but was\n2026-10-08T00:00:00.0000000", "2026-10-09T00:00:00.0000000", "2026-10-08T00:00:00.0000000" },
        { "items\n    should be\n[1, 3]\n    but was\n[1, 2]\n    difference\n[1, *2*]", "[1, 3]", "[1, 2]" },
        { "result\n    should be\n5\n    but was\n3\n\nAdditional Info:\n    custom message", "5", "3" },
        { "text\n    should be\n\"b\"\n    but was\n\"a\"\n    difference\nDifference     |  |   \n               | \\|/  \nIndex          | 0    \nExpected Value | b    \nActual Value   | a    ", "b", "a" },
        { "longText\n    should be\n\"line one\nline 2\"\n    but was\n\"line one\nline two\"\n    difference\nDifference     |    |\nIndex          | 14", "line one\nline 2", "line one\nline two" },
        { "result\r\n    should be\r\n5\r\n    but was\r\n3", "5", "3" },
        { "Expected result to be 5, but found 3.", "5", "3" },
        { "Expected flag to be True, but found False.", "True", "False" },
        { "Expected price to be 2.25M, but found 1.5M (difference of -0.75).", "2.25M", "1.5M" },
        { "Expected text to be \"b\", but \"a\" differs near \"a\" (index 0).", "b", "a" },
        { "Expected none to be \"b\", but found <null>.", "b", "<null>" },
        { "Expected text to be <null>, but found \"a\".", "<null>", "a" },
        { "Expected date to be <2026-10-09>, but found <2026-10-08>.", "<2026-10-09>", "<2026-10-08>" },
        { "Expected result to be 5 because the total must match, but found 3.", "5", "3" },
    };

    [Theory]
    [MemberData(nameof(UnquotedFailures))]
    public void An_unquoted_failure_has_an_expected_and_actual(string message, string expected, string actual)
    {
        var result = ErrorDiffParser.TryParseExpectedActual(message);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Expected);
        Assert.Equal(actual, result.Actual);
    }

    [Theory]
    // A comparison is not an equality: Shouldly's "should be greater than 10" is no expected value of 10.
    [InlineData("result\n    should be greater than\n10\n    but was\n3")]
    [InlineData("name\n    should not be null but was")]
    [InlineData("result\n    should not be\n3\n    but was\n3")]
    public void A_check_that_is_not_an_equality_has_no_expected_and_actual(string message)
    {
        Assert.Null(ErrorDiffParser.TryParseExpectedActual(message));
    }
}
