using Kronikol;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;

namespace Kronikol.Tests.BDDfy;

/// <summary>
/// The BDDfy lane's titles and ids (plans/TITLES_AS_WRITTEN_PLAN.md 4.4): a fallback title the rule now reads better
/// keeps the id 5.16.4 gave it, and so does the ` [n]` BDDfy's duplicate counter put in it, so the order of the story
/// and the counter both follow the titles 5.16.4 showed.
/// </summary>
public class BDDfyTitleTests
{
    /// <summary>A scenario whose title the processor resolved from its method, with the title 5.16.4 gave it, as the processor records it.</summary>
    private static BDDfyScenarioInfo Resolved(string testId, string method) => new()
    {
        TestId = testId,
        StoryTitle = "Gets",
        ScenarioTitle = ScenarioTitleResolver.ResolveScenarioTitle("GetsTests", "GetsTests", method),
        StableIdTitle = ScenarioTitleResolver.ResolveScenarioTitleAsOf5164("GetsTests", "GetsTests", method) is var frozen &&
                        frozen != ScenarioTitleResolver.ResolveScenarioTitle("GetsTests", "GetsTests", method) ? frozen : null,
        Tags = [],
        Steps = [],
        Result = TestStack.BDDfy.Result.Passed,
        TestMethodName = method,
    };

    private static string Id(Scenario scenario) => ScenarioStableId.For(null, "Gets", scenario);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_titles_that_read_alike_in_5_16_4_keep_their_numbered_ids_in_the_order_they_came(bool apiFirst)
    {
        // GetsApi and GetsAPI both read "Gets api" in 5.16.4, numbered [1] and [2] in the order the collector gave them
        var api = Resolved("t-api", "GetsApi");
        var caps = Resolved("t-caps", "GetsAPI");

        var scenarios = (apiFirst ? new[] { api, caps } : [caps, api]).ToFeatures().Single().Scenarios;

        var first = scenarios.Single(s => s.Id == (apiFirst ? "t-api" : "t-caps"));
        var second = scenarios.Single(s => s.Id == (apiFirst ? "t-caps" : "t-api"));
        Assert.Equal(ScenarioStableId.Compute(null, "Gets", "Gets api [1]"), Id(first));
        Assert.Equal(ScenarioStableId.Compute(null, "Gets", "Gets api [2]"), Id(second));
        // they read apart now, so neither shows a number it no longer needs
        Assert.Equal("Gets api", scenarios.Single(s => s.Id == "t-api").DisplayName);
        Assert.Equal("Gets API", scenarios.Single(s => s.Id == "t-caps").DisplayName);
        Assert.Equal([first.Id, second.Id], scenarios.Select(s => s.Id));
    }

    [Fact]
    public void Two_titles_the_rule_made_alike_show_alike_and_keep_their_ids_apart()
    {
        // 5.16.4 read "Returns404when" and "Returns404 when"; the rule reads both "Returns404 when"
        var glued = Resolved("t-glued", "Returns404When");
        var underscored = Resolved("t-underscored", "Returns404_When");

        var scenarios = new[] { glued, underscored }.ToFeatures().Single().Scenarios;

        Assert.All(scenarios, s => Assert.Equal("Returns404 when", s.DisplayName));
        Assert.Equal(ScenarioStableId.Compute(null, "Gets", "Returns404when"), Id(scenarios.Single(s => s.Id == "t-glued")));
        Assert.Equal(ScenarioStableId.Compute(null, "Gets", "Returns404 when"), Id(scenarios.Single(s => s.Id == "t-underscored")));
    }

    [Fact]
    public void A_title_that_still_repeats_keeps_its_number()
    {
        var one = Resolved("t-1", "GetsTheOrder");
        var two = Resolved("t-2", "GetsTheOrder");

        var scenarios = new[] { one, two }.ToFeatures().Single().Scenarios;

        Assert.Equal(["Gets the order [1]", "Gets the order [2]"], scenarios.Select(s => s.DisplayName));
        Assert.Equal(ScenarioStableId.Compute(null, "Gets", "Gets the order [2]"), Id(scenarios[1]));
    }

    [Fact]
    public void A_fallback_title_keeps_its_capitals_and_its_id()
    {
        var scenario = new[] { Resolved("t-vat", "Gets_the_UK_VAT_rate") }.ToFeatures().Single().Scenarios.Single();

        Assert.Equal("Gets the UK VAT rate", scenario.DisplayName);
        Assert.Equal(ScenarioStableId.Compute(null, "Gets", "Gets the uk vat rate"), Id(scenario));
    }
}
