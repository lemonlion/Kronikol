using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ScenarioStableIdTests
{
    [Fact]
    public void Same_feature_and_scenario_produce_same_id()
    {
        var id1 = ScenarioStableId.Compute(null, "Orders", "Place order");
        var id2 = ScenarioStableId.Compute(null, "Orders", "Place order");
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Different_scenario_names_produce_different_ids()
    {
        var id1 = ScenarioStableId.Compute(null, "Orders", "Place order");
        var id2 = ScenarioStableId.Compute(null, "Orders", "Cancel order");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Different_feature_names_produce_different_ids()
    {
        var id1 = ScenarioStableId.Compute(null, "Orders", "Place order");
        var id2 = ScenarioStableId.Compute(null, "Payments", "Place order");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Returns_16_character_lowercase_hex_string()
    {
        var id = ScenarioStableId.Compute(null, "Orders", "Place order");
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public void Parameterized_scenarios_with_same_outlineId_but_different_display_names_produce_different_ids()
    {
        var id1 = ScenarioStableId.Compute(null, "Orders", "Place order (visa)", outlineId: "Place order");
        var id2 = ScenarioStableId.Compute(null, "Orders", "Place order (mastercard)", outlineId: "Place order");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Example_rows_sharing_a_display_name_get_distinct_ids()
    {
        // The case the field exists for: an outline whose rows all render the same title. Without the
        // example values in the hash every row collapses onto one id and cross-run diffing cannot tell
        // row 1 from row 3.
        var row1 = ScenarioStableId.Compute(null, "Muffins", "Different recipes produce the expected batch",
            outlineId: "Different recipes produce the expected batch",
            exampleValues: new Dictionary<string, string> { ["flour"] = "200g", ["eggs"] = "2" });
        var row2 = ScenarioStableId.Compute(null, "Muffins", "Different recipes produce the expected batch",
            outlineId: "Different recipes produce the expected batch",
            exampleValues: new Dictionary<string, string> { ["flour"] = "400g", ["eggs"] = "4" });

        Assert.NotEqual(row1, row2);
    }

    [Fact]
    public void Example_value_order_does_not_change_the_id()
    {
        var a = ScenarioStableId.Compute(null, "F", "S", "S", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        var b = ScenarioStableId.Compute(null, "F", "S", "S", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

        Assert.Equal(a, b);
    }

    [Fact]
    public void Non_parameterised_scenarios_keep_the_ids_they_already_had()
    {
        // Folding example values in is a behavioural change; it must not reach scenarios that have none.
        var withEmpty = ScenarioStableId.Compute(null, "Orders", "Place order", exampleValues: new Dictionary<string, string>());

        Assert.Equal(ScenarioStableId.Compute(null, "Orders", "Place order"), withEmpty);
    }

    [Fact]
    public void Null_outlineId_same_as_no_outlineId()
    {
        var id1 = ScenarioStableId.Compute(null, "Orders", "Place order");
        var id2 = ScenarioStableId.Compute(null, "Orders", "Place order", outlineId: null);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Id_is_deterministic_across_calls()
    {
        var ids = Enumerable.Range(0, 100)
            .Select(_ => ScenarioStableId.Compute(null, "Feature A", "Scenario B"))
            .Distinct()
            .ToArray();
        Assert.Single(ids);
    }

    [Fact]
    public void Handles_special_characters_in_names()
    {
        var id = ScenarioStableId.Compute(null, "Feature: <special> & \"chars\"", "Scenario with 'quotes' & stuff");
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public void Handles_unicode_names()
    {
        var id = ScenarioStableId.Compute(null, "注文機能", "注文を確定する");
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    /// <summary>
    /// The property the suite scope exists for. Two projects with the same feature and scenario names is
    /// not hypothetical - it is what a shared component-test template produces - and before this the two
    /// minted one id, so merging them silently paired unrelated scenarios and a cross-run diff reported
    /// them as the same test changing verdict.
    /// </summary>
    [Fact]
    public void The_same_scenario_in_two_suites_gets_two_ids()
    {
        var inAlpha = ScenarioStableId.Compute("Alpha.Tests", "Orders", "Place order");
        var inBeta = ScenarioStableId.Compute("Beta.Tests", "Orders", "Place order");
        Assert.NotEqual(inAlpha, inBeta);
    }

    /// <summary>
    /// Absent means absent, not empty-string-prefixed. An ingest, a library caller and every report
    /// written before 3.1.0 have no suite, and they must all agree on one id - otherwise adding the
    /// parameter would have re-keyed the reports it was not meant to touch.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_suite_reproduces_the_unscoped_id(string? suite)
    {
        // The pre-3.1.0 value for ("Orders", "Place order"), pinned as a literal rather than computed,
        // so a change to the hash input cannot silently agree with itself.
        Assert.Equal(ScenarioStableId.Compute(null, "Orders", "Place order"),
                     ScenarioStableId.Compute(suite?.Trim(), "Orders", "Place order"));
    }

    [Fact]
    public void The_suite_scope_survives_outlines_and_example_values()
    {
        var values = new Dictionary<string, string> { ["size"] = "large" };
        var a = ScenarioStableId.Compute("Alpha.Tests", "Orders", "Place order", "outline-1", values);
        var b = ScenarioStableId.Compute("Beta.Tests", "Orders", "Place order", "outline-1", values);
        var unscoped = ScenarioStableId.Compute(null, "Orders", "Place order", "outline-1", values);

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, unscoped);
    }
}
