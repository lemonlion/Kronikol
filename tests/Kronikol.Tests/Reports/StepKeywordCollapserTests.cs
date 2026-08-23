using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class StepKeywordCollapserTests
{
    private static ScenarioStep[] Steps(params string?[] keywords) =>
        [.. keywords.Select((k, i) => new ScenarioStep { Keyword = k, Text = $"step {i}" })];

    [Fact]
    public void Repeated_given_collapses_to_and()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("Given", "Given", "When"));
        Assert.Equal<string?>(["Given", "And", "When"], displayed);
    }

    [Fact]
    public void Each_primary_keyword_collapses_its_own_repeat()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("Given", "When", "When", "Then", "Then"));
        Assert.Equal<string?>(["Given", "When", "And", "Then", "And"], displayed);
    }

    [Fact]
    public void A_conjunction_does_not_reset_the_current_keyword()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("Given", "And", "Given"));
        Assert.Equal<string?>(["Given", "And", "And"], displayed);
    }

    [Fact]
    public void A_different_primary_in_between_stops_the_collapse()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("Given", "Then", "Given"));
        Assert.Equal<string?>(["Given", "Then", "Given"], displayed);
    }

    [Fact]
    public void But_star_null_and_empty_pass_through_and_do_not_reset()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("Given", "But", "*", null, "", "Given"));
        Assert.Equal<string?>(["Given", "But", "*", null, "", "And"], displayed);
    }

    [Fact]
    public void ButWhen_is_treated_as_a_primary()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("ButWhen", "ButWhen", "Then"));
        Assert.Equal<string?>(["ButWhen", "And", "Then"], displayed);
    }

    [Fact]
    public void An_unrecognised_keyword_passes_through_and_resets_the_current_keyword()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("Given", "Angenommen", "Given"));
        Assert.Equal<string?>(["Given", "Angenommen", "Given"], displayed);
    }

    [Fact]
    public void The_emitted_and_follows_the_casing_of_the_keyword_it_replaces()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("GIVEN", "GIVEN"));
        Assert.Equal<string?>(["GIVEN", "AND"], displayed);
    }

    [Fact]
    public void Lowercase_keywords_collapse_to_lowercase_and()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("given", "given"));
        Assert.Equal<string?>(["given", "and"], displayed);
    }

    [Fact]
    public void Keywords_are_matched_after_trimming_and_the_original_spacing_survives()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("Given ", " Given"));
        Assert.Equal<string?>(["Given ", "And"], displayed);
    }

    [Fact]
    public void Context_and_action_keyword_types_are_recognised_as_primaries()
    {
        var displayed = StepKeywordCollapser.DisplayKeywords(Steps("Context", "Context", "Action", "Action"));
        Assert.Equal<string?>(["Context", "And", "Action", "And"], displayed);
    }

    [Fact]
    public void The_input_steps_are_not_mutated()
    {
        var steps = Steps("Given", "Given");
        StepKeywordCollapser.DisplayKeywords(steps);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.Equal("Given", steps[1].Keyword);
    }

    [Fact]
    public void An_empty_list_yields_an_empty_result()
    {
        Assert.Empty(StepKeywordCollapser.DisplayKeywords([]));
    }
}
