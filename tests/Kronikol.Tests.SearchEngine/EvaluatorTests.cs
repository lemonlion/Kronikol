namespace Kronikol.Tests.SearchEngine;

public class EvaluatorTests : JintTestBase
{
    #region Text matching

    [Fact]
    public void Text_matches_when_search_text_contains_word()
    {
        var result = CallEvaluate(new AstText("error"), "an error occurred in the service", [], "Passed");
        Assert.True(result);
    }

    [Fact]
    public void Text_does_not_match_when_word_absent()
    {
        var result = CallEvaluate(new AstText("error"), "everything is fine", [], "Passed");
        Assert.False(result);
    }

    [Fact]
    public void Text_match_is_case_insensitive()
    {
        var result = CallEvaluate(new AstText("error"), "ERROR in service", [], "Passed");
        Assert.True(result);
    }

    [Fact]
    public void Phrase_matches_exact_substring()
    {
        var result = CallEvaluate(new AstPhrase("error message"), "the error message was clear", [], "Passed");
        Assert.True(result);
    }

    [Fact]
    public void Phrase_does_not_match_non_contiguous_words()
    {
        var result = CallEvaluate(new AstPhrase("error message"), "error and message", [], "Passed");
        Assert.False(result);
    }

    #endregion

    #region Tag matching

    [Fact]
    public void Tag_matches_when_present_in_set()
    {
        var result = CallEvaluate(new AstTag("smoke"), "some text", ["smoke", "api"], "Passed");
        Assert.True(result);
    }

    [Fact]
    public void Tag_does_not_match_when_absent()
    {
        var result = CallEvaluate(new AstTag("smoke"), "some text", ["regression"], "Passed");
        Assert.False(result);
    }

    [Fact]
    public void Tag_does_not_match_empty_set()
    {
        var result = CallEvaluate(new AstTag("smoke"), "some text", [], "Passed");
        Assert.False(result);
    }

    [Fact]
    public void Tag_match_is_case_insensitive()
    {
        var result = CallEvaluate(new AstTag("smoke"), "some text", ["SMOKE"], "Passed");
        Assert.True(result);
    }

    #endregion

    #region Status matching

    [Fact]
    public void Status_matches_case_insensitive()
    {
        var result = CallEvaluate(new AstStatus("failed"), "some text", [], "Failed");
        Assert.True(result);
    }

    [Fact]
    public void Status_does_not_match_different_status()
    {
        var result = CallEvaluate(new AstStatus("failed"), "some text", [], "Passed");
        Assert.False(result);
    }

    [Fact]
    public void Status_passed_matches()
    {
        var result = CallEvaluate(new AstStatus("passed"), "some text", [], "Passed");
        Assert.True(result);
    }

    [Fact]
    public void Status_skipped_matches()
    {
        var result = CallEvaluate(new AstStatus("skipped"), "some text", [], "Skipped");
        Assert.True(result);
    }

    #endregion

    #region AND expressions

    [Fact]
    public void And_both_true_returns_true()
    {
        var ast = new AstAnd(new AstText("hello"), new AstText("world"));
        Assert.True(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    [Fact]
    public void And_left_false_returns_false()
    {
        var ast = new AstAnd(new AstText("missing"), new AstText("world"));
        Assert.False(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    [Fact]
    public void And_right_false_returns_false()
    {
        var ast = new AstAnd(new AstText("hello"), new AstText("missing"));
        Assert.False(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    [Fact]
    public void And_both_false_returns_false()
    {
        var ast = new AstAnd(new AstText("missing"), new AstText("absent"));
        Assert.False(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    #endregion

    #region OR expressions

    [Fact]
    public void Or_both_true_returns_true()
    {
        var ast = new AstOr(new AstText("hello"), new AstText("world"));
        Assert.True(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    [Fact]
    public void Or_left_true_right_false_returns_true()
    {
        var ast = new AstOr(new AstText("hello"), new AstText("missing"));
        Assert.True(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    [Fact]
    public void Or_left_false_right_true_returns_true()
    {
        var ast = new AstOr(new AstText("missing"), new AstText("world"));
        Assert.True(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    [Fact]
    public void Or_both_false_returns_false()
    {
        var ast = new AstOr(new AstText("missing"), new AstText("absent"));
        Assert.False(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    #endregion

    #region NOT expressions

    [Fact]
    public void Not_true_returns_false()
    {
        var ast = new AstNot(new AstText("hello"));
        Assert.False(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    [Fact]
    public void Not_false_returns_true()
    {
        var ast = new AstNot(new AstText("missing"));
        Assert.True(CallEvaluate(ast, "hello world", [], "Passed"));
    }

    #endregion

    #region Complex evaluation

    [Fact]
    public void Text_or_tag_and_not_status()
    {
        // (text("error") || tag("smoke")) && !!status("passed")
        var ast = new AstAnd(
            new AstOr(new AstText("error"), new AstTag("smoke")),
            new AstNot(new AstStatus("passed")));

        // text matches, no tag, failed → true (text matches OR, NOT passed = true)
        Assert.True(CallEvaluate(ast, "an error occurred", [], "Failed"));

        // text matches, no tag, passed → false (NOT passed = false)
        Assert.False(CallEvaluate(ast, "an error occurred", [], "Passed"));
    }

    [Fact]
    public void All_three_conditions_must_match()
    {
        // text("login") && text("success") && tag("happy-path")
        var ast = new AstAnd(
            new AstAnd(new AstText("login"), new AstText("success")),
            new AstTag("happy-path"));

        Assert.True(CallEvaluate(ast, "user login success", ["happy-path"], "Passed"));
        Assert.False(CallEvaluate(ast, "user login failure", ["happy-path"], "Passed"));
        Assert.False(CallEvaluate(ast, "user login success", [], "Passed"));
    }

    #endregion

    // E3 (5.9.1): a scenario skipped after a failure is skipped, as the summary counts it; its own name still finds it alone
    [Fact]
    public void Status_skipped_matches_a_scenario_skipped_after_a_failure()
    {
        Assert.True(CallEvaluate(new AstStatus("skipped"), "some text", [], "SkippedAfterFailure"));
        Assert.True(CallEvaluate(new AstStatus("skippedafterfailure"), "some text", [], "SkippedAfterFailure"));
        Assert.False(CallEvaluate(new AstStatus("skippedafterfailure"), "some text", [], "Skipped"));
        Assert.False(CallEvaluate(new AstStatus("failed"), "some text", [], "SkippedAfterFailure"));
    }
}
