namespace Kronikol.Tests.SearchEngine;

/// <summary>
/// The forms SEARCH_PLAN 4.4 adds to the query, read by the page's own search over <see cref="SearchFixture"/>:
/// <c>tag:name</c> is <c>@name</c> and <c>status:name</c> is <c>$name</c>, each a token of the advanced grammar, so they
/// take part in <c>&amp;&amp;</c>, <c>||</c>, <c>!!</c> and brackets as the old forms do; a <c>-</c> right before a
/// qualifier excludes it as <c>!!</c> does; a status name that is neither an execution result nor one of this report's
/// history verdicts is read as the text it was and marks the field, as a qualifier the search cannot read does. A
/// <c>-</c> before any other word, and every new form inside quotes, stay text (guards). B4: a scenario skipped after
/// a failure is failed for <c>$failed</c> and <c>status:failed</c>, as the Status buttons count it.
/// </summary>
public class SearchQueryFormsTests
{
    private static readonly SearchPage Page = SearchFixture.Page();
    private static readonly SearchPage DefaultPage = SearchFixture.Page(verdicts: false);

    private static void Keeps(string ids, string query, SearchPage? page = null)
    {
        var outcome = (page ?? Page).Search(query);
        Assert.True(SearchFixture.Named(ids).SequenceEqual(outcome.Kept),
            $"{query} kept [{string.Join(", ", outcome.Kept)}], not [{string.Join(", ", SearchFixture.Named(ids))}]");
        Assert.False(outcome.Bad, $"{query} marked the field: {outcome.BadText}");
    }

    // L1
    [Theory]
    [InlineData("tag:smoke", "@smoke", "S1,S3,S7,S11")]
    [InlineData("TAG:Smoke", "@smoke", "S1,S3,S7,S11")]
    [InlineData("tag:\"two words\"", "@\"two words\"", "S9")]
    [InlineData("tag:\"Happy Path\"", "@\"Happy Path\"", "S1,S3,S5")]
    [InlineData("tag:api && tag:smoke", "@api && @smoke", "S1,S3,S7")]
    [InlineData("tag:ui || tag:slow", "@ui || @slow", "S3,S5,S6,S8,S10,S11")]
    [InlineData("!!tag:slow", "!!@slow", "S1,S2,S4,S5,S7,S8,S9,S10")]
    [InlineData("(tag:ui || tag:api) && !!tag:slow", "(@ui || @api) && !!@slow", "S1,S2,S4,S5,S7,S8,S10")]
    [InlineData("order tag:regression", "order && @regression", "S2,S4")]
    [InlineData("tag:smoke $passed", "@smoke $passed", "S1,S3")]
    public void Tag_colon_is_at_tag(string query, string sameAs, string ids)
    {
        Keeps(ids, sameAs);
        Keeps(ids, query);
    }

    // L2
    [Theory]
    [InlineData("status:passed", "$passed", "S1,S3,S5,S9,S10")]
    [InlineData("Status:Passed", "$passed", "S1,S3,S5,S9,S10")]
    [InlineData("status:\"passed\"", "$passed", "S1,S3,S5,S9,S10")]
    [InlineData("status:skipped", "$skipped", "S4,S6")]
    [InlineData("status:bypassed", "$bypassed", "S8")]
    [InlineData("status:skippedafterfailure", "$skippedafterfailure", "S4")]
    [InlineData("status:flaky", "$flaky", "S3,S7")]
    [InlineData("status:always-failing", "$always-failing", "S11")]
    [InlineData("status:broke && status:passed", "$broke && $passed", "")]
    [InlineData("status:passed || status:skipped", "$passed || $skipped", "S1,S3,S4,S5,S6,S9,S10")]
    [InlineData("status:passed && !!status:flaky", "$passed && !!$flaky", "S1,S5,S9,S10")]
    [InlineData("pancake status:passed", "pancake $passed", "S9,S10")]
    [InlineData("(status:bypassed || tag:kitchen) && !!status:failed", "($bypassed || @kitchen) && !!$failed", "S8,S9,S10")]
    public void Status_colon_is_dollar_status_for_results_and_verdicts(string query, string sameAs, string ids)
    {
        Keeps(ids, sameAs);
        Keeps(ids, query);
    }

    // L3: the name is read as the text it was, and the field says it could not be read
    [Theory]
    [InlineData("status:400", "S9", "status:400")]
    [InlineData("status:nonsense", "", "status:nonsense")]
    [InlineData("-status:nonsense", "", "-status:nonsense")]
    [InlineData("api status:400", "S9", "status:400")]
    [InlineData("status:400 @kitchen or @ui", "S9", "status:400")]
    [InlineData("status:400 && !!$failed", "S9", "status:400")]
    // a verdict no scenario of this report carries
    [InlineData("status:unstable-shape", "", "status:unstable-shape")]
    // in quotes too, read as it was: the text status: and the phrase
    [InlineData("status:\"400\"", "S9", "status:\"400\"")]
    // a tag needs a name: the text tag:, as before
    [InlineData("tag:\"\"", "", "tag:\"\"")]
    public void An_unknown_status_name_is_text_and_marks_the_input(string query, string ids, string bad)
    {
        var outcome = Page.Search(query);
        Assert.Equal(SearchFixture.Named(ids), outcome.Kept);
        Assert.True(outcome.Bad, $"{query} left the field unmarked");
        Assert.Equal(bad, outcome.BadText);
    }

    [Fact]
    public void A_verdict_in_a_report_drawn_without_verdicts_is_text_and_marks_the_input()
    {
        var outcome = DefaultPage.Search("status:flaky");
        Assert.Empty(outcome.Kept);
        Assert.True(outcome.Bad);
        Assert.Equal("status:flaky", outcome.BadText);
        Keeps("S1,S3,S5,S9,S10", "status:passed", DefaultPage);
    }

    // L4: tag and status here; every qualifier through the page
    [Theory]
    [InlineData("-tag:slow", "!!@slow", "S1,S2,S4,S5,S7,S8,S9,S10")]
    [InlineData("-tag:\"Happy Path\"", "!!@\"Happy Path\"", "S2,S4,S6,S7,S8,S9,S10,S11")]
    [InlineData("-status:passed", "!!$passed", "S2,S4,S6,S7,S8,S11")]
    [InlineData("-status:flaky", "!!$flaky", "S1,S2,S4,S5,S6,S8,S9,S10,S11")]
    [InlineData("order -tag:smoke", "order && !!@smoke", "S2,S4")]
    [InlineData("pancake -status:failed", "pancake && !!$failed", "S9,S10")]
    [InlineData("(-tag:api) && $passed", "(!!@api) && $passed", "S5,S9,S10")]
    [InlineData("-dep:payment", "!!dep:payment", "S1,S2,S3,S8,S9,S10,S11")]
    [InlineData("-is:happy", "!!is:happy", "S2,S4,S6,S7,S8,S9,S10,S11")]
    [InlineData("-duration:>2s", "!!duration:>2s", "S1,S3,S4,S5,S6,S7,S8,S9,S10")]
    [InlineData("-feature:payment", "!!feature:payment", "S1,S2,S3,S4,S9,S10,S11")]
    [InlineData("-dep:payment -tag:kitchen", "!!dep:payment && !!@kitchen", "S1,S2,S3,S8")]
    public void Minus_before_a_qualifier_excludes_it(string query, string sameAs, string ids)
    {
        Keeps(ids, sameAs);
        Keeps(ids, query);
    }

    // L5 (guard): as GitHub's, a - before a word that is no qualifier is part of the word
    [Theory]
    [InlineData("-existent", "S2")]
    [InlineData("order -existent", "S2")]
    [InlineData("non-existent", "S2")]
    [InlineData("-pancake", "")]
    [InlineData("-@slow", "")]
    [InlineData("a-tag:smoke", "")]
    [InlineData("--tag:smoke", "")]
    [InlineData("-milk:", "")]
    [InlineData("-status", "")]
    [InlineData("-tag:", "")]
    public void Minus_before_a_word_is_text(string query, string ids)
    {
        Keeps(ids, query);
    }

    // L7 (guard)
    [Theory]
    [InlineData("\"status:400\"", "S9")]
    [InlineData("\"the api answers status:400\"", "S9")]
    [InlineData("\"tag:smoke\"", "")]
    [InlineData("\"status:failed\"", "")]
    [InlineData("\"feature:pancake\"", "")]
    [InlineData("\"-tag:slow\"", "")]
    [InlineData("\"order -dep:payment\"", "")]
    [InlineData("@smoke \"tag:slow\"", "")]
    public void New_forms_inside_quotes_are_text(string query, string ids)
    {
        Keeps(ids, query);
    }

    // B4 (Q11): the search and the Status filter it stands beside agree; outside the verdict fences, so both variants
    [Theory]
    [InlineData("$failed", "S2,S7,S11")]
    [InlineData("status:failed", "S2,S7,S11")]
    [InlineData("$failed && order", "S2,S11")]
    [InlineData("$skippedafterfailure", "S4")]
    [InlineData("$skipped", "S4,S6")]
    [InlineData("status:skipped", "S4,S6")]
    [InlineData("!!$failed && api", "S1,S3,S4,S9")]
    public void Status_skipped_finds_a_scenario_skipped_after_a_failure(string query, string ids)
    {
        Keeps(ids, query);
        Keeps(ids, query, DefaultPage);
    }
}
