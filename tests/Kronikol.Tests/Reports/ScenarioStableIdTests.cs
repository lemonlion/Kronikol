using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ScenarioStableIdTests
{
    [Fact]
    public void Same_feature_and_scenario_produce_same_id()
    {
        var id1 = ScenarioStableId.Compute("Orders", "Place order");
        var id2 = ScenarioStableId.Compute("Orders", "Place order");
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Different_scenario_names_produce_different_ids()
    {
        var id1 = ScenarioStableId.Compute("Orders", "Place order");
        var id2 = ScenarioStableId.Compute("Orders", "Cancel order");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Different_feature_names_produce_different_ids()
    {
        var id1 = ScenarioStableId.Compute("Orders", "Place order");
        var id2 = ScenarioStableId.Compute("Payments", "Place order");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Returns_16_character_lowercase_hex_string()
    {
        var id = ScenarioStableId.Compute("Orders", "Place order");
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public void Parameterized_scenarios_with_same_outlineId_but_different_display_names_produce_different_ids()
    {
        var id1 = ScenarioStableId.Compute("Orders", "Place order (visa)", outlineId: "Place order");
        var id2 = ScenarioStableId.Compute("Orders", "Place order (mastercard)", outlineId: "Place order");
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Example_rows_sharing_a_display_name_get_distinct_ids()
    {
        // The case the field exists for: an outline whose rows all render the same title. Without the
        // example values in the hash every row collapses onto one id and cross-run diffing cannot tell
        // row 1 from row 3.
        var row1 = ScenarioStableId.Compute("Muffins", "Different recipes produce the expected batch",
            outlineId: "Different recipes produce the expected batch",
            exampleValues: new Dictionary<string, string> { ["flour"] = "200g", ["eggs"] = "2" });
        var row2 = ScenarioStableId.Compute("Muffins", "Different recipes produce the expected batch",
            outlineId: "Different recipes produce the expected batch",
            exampleValues: new Dictionary<string, string> { ["flour"] = "400g", ["eggs"] = "4" });

        Assert.NotEqual(row1, row2);
    }

    [Fact]
    public void Example_value_order_does_not_change_the_id()
    {
        var a = ScenarioStableId.Compute("F", "S", "S", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        var b = ScenarioStableId.Compute("F", "S", "S", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

        Assert.Equal(a, b);
    }

    [Fact]
    public void Non_parameterised_scenarios_keep_the_ids_they_already_had()
    {
        // Folding example values in is a behavioural change; it must not reach scenarios that have none.
        var withEmpty = ScenarioStableId.Compute("Orders", "Place order", exampleValues: new Dictionary<string, string>());

        Assert.Equal(ScenarioStableId.Compute("Orders", "Place order"), withEmpty);
    }

    [Fact]
    public void Null_outlineId_same_as_no_outlineId()
    {
        var id1 = ScenarioStableId.Compute("Orders", "Place order");
        var id2 = ScenarioStableId.Compute("Orders", "Place order", outlineId: null);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Id_is_deterministic_across_calls()
    {
        var ids = Enumerable.Range(0, 100)
            .Select(_ => ScenarioStableId.Compute("Feature A", "Scenario B"))
            .Distinct()
            .ToArray();
        Assert.Single(ids);
    }

    [Fact]
    public void Handles_special_characters_in_names()
    {
        var id = ScenarioStableId.Compute("Feature: <special> & \"chars\"", "Scenario with 'quotes' & stuff");
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public void Handles_unicode_names()
    {
        var id = ScenarioStableId.Compute("注文機能", "注文を確定する");
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }
}
