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

    // The owner's report (2026-10-18): the diff compared characters, so "Gone" against "NotFound" kept the o and the n
    // the two words share and struck G and e, and "410" against "404" struck the 1 alone. A changed word or number is
    // marked whole.
    [Fact]
    public void GenerateDiffHtml_marks_a_changed_word_or_number_whole()
    {
        var html = ErrorDiffParser.GenerateDiffHtml("HttpStatusCode.Gone {value: 410}", "HttpStatusCode.NotFound {value: 404}");

        Assert.Contains("<code>HttpStatusCode.<span class=\"diff-del\">Gone</span> {value: <span class=\"diff-del\">410</span>}</code>",
            html, StringComparison.Ordinal);
        Assert.Contains("<code>HttpStatusCode.<span class=\"diff-ins\">NotFound</span> {value: <span class=\"diff-ins\">404</span>}</code>",
            html, StringComparison.Ordinal);
    }

    // Two changed words read as one change, not as two with a common space between them.
    [Fact]
    public void GenerateDiffHtml_marks_the_space_between_two_changed_words_with_them()
    {
        var html = ErrorDiffParser.GenerateDiffHtml("the quick fox", "a slow fox");

        Assert.Contains("<code><span class=\"diff-del\">the quick</span> fox</code>", html, StringComparison.Ordinal);
        Assert.Contains("<code><span class=\"diff-ins\">a slow</span> fox</code>", html, StringComparison.Ordinal);
    }

    // Punctuation is compared a character at a time, so a changed separator is marked alone.
    [Fact]
    public void GenerateDiffHtml_marks_a_changed_punctuation_mark_alone()
    {
        var html = ErrorDiffParser.GenerateDiffHtml("a,b", "a;b");

        Assert.Contains("<code>a<span class=\"diff-del\">,</span>b</code>", html, StringComparison.Ordinal);
        Assert.Contains("<code>a<span class=\"diff-ins\">;</span>b</code>", html, StringComparison.Ordinal);
    }

    // A word is its letters whatever the script, an accented letter written with a combining mark included, and a
    // digit run or an underscore belongs to the word it is in.
    [Theory]
    [InlineData("naïve plan", "naive plan", "<span class=\"diff-del\">na&#239;ve</span> plan")]
    [InlineData("nai\u0308ve plan", "naive plan", "<span class=\"diff-del\">nai\u0308ve</span> plan")]
    [InlineData("Some_Fresh_Milk now", "Some_Old_Milk now", "<span class=\"diff-del\">Some_Fresh_Milk</span> now")]
    [InlineData("Order42 placed", "Order43 placed", "<span class=\"diff-del\">Order42</span> placed")]
    public void GenerateDiffHtml_keeps_a_word_whole(string expected, string actual, string expectedLine)
    {
        var html = ErrorDiffParser.GenerateDiffHtml(expected, actual);

        Assert.Contains($"<code>{expectedLine}</code>", html, StringComparison.Ordinal);
    }

    // The audit of #165 (2026-10-15): the diff compared every pair in a table it built twice, with no bound, so two
    // 50,000-character values, as Shouldly writes a response body whole, threw OutOfMemoryException and the run had no
    // HTML report. Past the bound it keeps the common start and end and marks the middle.
    [Fact]
    public void GenerateDiffHtml_bounds_its_work_on_long_values()
    {
        var start = string.Join(" ", Enumerable.Repeat("a", 12_500));
        var end = string.Join(" ", Enumerable.Repeat("z", 12_500));
        Assert.True((long)ErrorDiffParser.Tokens(start).Length * ErrorDiffParser.Tokens(end).Length > ErrorDiffParser.MaxComparedPairs);

        var html = ErrorDiffParser.GenerateDiffHtml(start + " EXPECTED " + end, start + " ACTUAL " + end);

        Assert.Contains($"<code>{start} <span class=\"diff-del\">EXPECTED</span> {end}</code>", html, StringComparison.Ordinal);
        Assert.Contains($"<code>{start} <span class=\"diff-ins\">ACTUAL</span> {end}</code>", html, StringComparison.Ordinal);
    }

    // The bound is the product of the two values' token counts: just under it the values are still compared token by
    // token, so a word changed in the middle is marked alone and the common words around it are not.
    [Fact]
    public void GenerateDiffHtml_compares_each_token_up_to_its_bound()
    {
        var words = string.Join(" ", Enumerable.Repeat("a", 490));
        var expected = words + " b " + words + " x";
        var actual = words + " c " + words + " a";
        Assert.True((long)ErrorDiffParser.Tokens(expected).Length * ErrorDiffParser.Tokens(actual).Length <= ErrorDiffParser.MaxComparedPairs);

        var html = ErrorDiffParser.GenerateDiffHtml(expected, actual);

        Assert.Contains($"<code>{words} <span class=\"diff-del\">b</span> {words} <span class=\"diff-del\">x</span></code>", html, StringComparison.Ordinal);
    }

    // A value read as one word has no tokens to compare past its bound: the two are marked whole, and the work stays
    // one pair, however long they are.
    [Fact]
    public void GenerateDiffHtml_marks_two_long_single_words_whole()
    {
        var html = ErrorDiffParser.GenerateDiffHtml(new string('a', 50_000) + "b", new string('a', 50_000) + "c");

        Assert.Contains("<code><span class=\"diff-del\">" + new string('a', 50_000) + "b</span></code>", html, StringComparison.Ordinal);
    }

    // The diff wrote each UTF-16 unit on its own, and HTML encoding writes half of a surrogate pair as U+FFFD, so an
    // emoji in either value read as two replacement characters.
    [Fact]
    public void GenerateDiffHtml_keeps_a_character_outside_the_basic_plane_whole()
    {
        var html = ErrorDiffParser.GenerateDiffHtml("a\U0001F600b", "a\U0001F601b");

        Assert.Contains("<code>a<span class=\"diff-del\">&#128512;</span>b</code>", html, StringComparison.Ordinal);
        Assert.Contains("<code>a<span class=\"diff-ins\">&#128513;</span>b</code>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("�", html, StringComparison.Ordinal);
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
        // WEAVER_SOURCE_PATHS_PLAN fact 25: Shouldly 4.3.0 without the caller's source (its map not handed to the test
        // process, as when a CI build's DLL is run directly, or ShouldlyConfiguration.DisableSourceInErrors()) writes the
        // actual value where the code would be and ends "but was not" (measured, plan harness shouldly/forms.txt).
        { "4\r\n    should be\r\n5\r\n    but was not", "5", "4" },
        { "\"a\"\r\n    should be\r\n\"b\"\r\n    but was not\r\n    difference\r\nDifference     |  |   \r\n               | \\|/  \r\nIndex          | 0    \r\nExpected Value | b    \r\nActual Value   | a    \r\nExpected Code  | 98   \r\nActual Code    | 97   ", "b", "a" },
        { "4\r\n    should be\r\n5\r\n    but was not\r\n\r\nAdditional Info:\r\n    custom message", "5", "4" },
        { "[1, 2]\r\n    should be\r\n[1, 3]\r\n    but was not\r\n    difference\r\n[1, *2*]", "[1, 3]", "[1, 2]" },
        { "\"x\"\r\n    should be\r\nnull\r\n    but was not", "null", "x" },
        { "DayOfWeek.Monday\r\n    should be\r\nDayOfWeek.Friday\r\n    but was not", "DayOfWeek.Friday", "DayOfWeek.Monday" },
        { "False\r\n    should be\r\nTrue\r\n    but was not", "True", "False" },
        // The audit (2026-10-15), each measured with Shouldly 4.3.0: NUnit stores the exception's type before the text,
        // so the actual value read was "Shouldly.ShouldAssertException : 4".
        { "Shouldly.ShouldAssertException : 4\r\n    should be\r\n5\r\n    but was not", "5", "4" },
        // An AggregateException's text, as the xUnit adapters join it: its own message quotes the inner one, which
        // follows whole. The values were read across both; they are the inner message's.
        { "One or more errors occurred. (4\r\n    should be\r\n5\r\n    but was not)\r\n4\r\n    should be\r\n5\r\n    but was not", "5", "4" },
        { "One or more errors occurred. (result\r\n    should be\r\n5\r\n    but was\r\n4)\r\nresult\r\n    should be\r\n5\r\n    but was\r\n4", "5", "4" },
        // A string collection's "but was" names its comparison, and a string compared with options names them on the
        // check's line, with the source and without it.
        { "[\"a\", \"b\"]\r\n    should be\r\n[\"a\", \"z\"]\r\n    but was not (case sensitive comparison)\r\n    difference\r\n[\"a\", *\"b\"*]", "[\"a\", \"z\"]", "[\"a\", \"b\"]" },
        { "list\r\n    should be\r\n[\"a\", \"z\"]\r\n    but was (case sensitive comparison)\r\n[\"a\", \"b\"]\r\n    difference\r\n[\"a\", *\"b\"*]", "[\"a\", \"z\"]", "[\"a\", \"b\"]" },
        { "\"abc\"\r\n    should be with options: Ignoring case\r\n\"ABD\"\r\n    but was not\r\n    difference\r\nDifference     |  |   \r\nIndex          | 0    ", "ABD", "abc" },
        { "text\r\n    should be with options: Ignoring case\r\n\"ABD\"\r\n    but was\r\n\"abc\"\r\n    difference\r\nDifference     |  |   \r\nIndex          | 0    ", "ABD", "abc" },
        // A value over several lines, and the text with LF alone, as Shouldly writes it where the newline is LF.
        { "\"line1\nline2\"\r\n    should be\r\n\"line1\nline3\"\r\n    but was not\r\n    difference\r\nDifference     |  |   \r\nIndex          | 0    ", "line1\nline3", "line1\nline2" },
        { "4\n    should be\n5\n    but was not", "5", "4" },
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
    // The same checks without the caller's source (WEAVER_SOURCE_PATHS_PLAN fact 25): only "should be" is read.
    [InlineData("1\r\n    should be greater than\r\n2\r\n    but was not")]
    [InlineData("1d\r\n    should be within\r\n0.1d\r\n    of\r\n2d\r\n    but was not")]
    [InlineData("5\r\n    should not be\r\n5\r\n    but was")]
    public void A_check_that_is_not_an_equality_has_no_expected_and_actual(string message)
    {
        Assert.Null(ErrorDiffParser.TryParseExpectedActual(message));
    }
}
