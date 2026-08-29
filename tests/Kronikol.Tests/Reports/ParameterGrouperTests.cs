using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ParameterGrouperTests
{
    [Fact]
    public void Single_scenario_no_params_returns_ungrouped()
    {
        var scenario = MakeScenario("s1", "SomeTest");
        var (groups, ungrouped) = ParameterGrouper.Analyze([scenario]);
        Assert.Empty(groups);
        Assert.Single(ungrouped);
    }

    [Fact]
    public void Two_scenarios_with_same_OutlineId_returns_one_group()
    {
        var s1 = MakeScenario("s1", "Test (UK)", outlineId: "Test", exampleValues: new() { ["region"] = "UK" });
        var s2 = MakeScenario("s2", "Test (US)", outlineId: "Test", exampleValues: new() { ["region"] = "US" });
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Empty(ungrouped);
        Assert.Equal("Test", groups[0].GroupDisplayName);
        Assert.Equal(2, groups[0].Scenarios.Length);
        Assert.Equal(ParameterDisplayRule.ScalarColumns, groups[0].Rule);
        Assert.Equal(["region"], groups[0].ParameterNames);
    }

    [Fact]
    public void OutlineId_with_underscores_is_humanized_in_group_display_name()
    {
        var s1 = MakeScenario("s1", "My test scenario name [region: UK]", outlineId: "My_test_scenario_name", exampleValues: new() { ["region"] = "UK" });
        var s2 = MakeScenario("s2", "My test scenario name [region: US]", outlineId: "My_test_scenario_name", exampleValues: new() { ["region"] = "US" });
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal("My test scenario name", groups[0].GroupDisplayName);
    }

    [Fact]
    public void OutlineId_with_PascalCase_is_humanized_in_group_display_name()
    {
        var s1 = MakeScenario("s1", "My test scenario name [region: UK]", outlineId: "MyTestScenarioName", exampleValues: new() { ["region"] = "UK" });
        var s2 = MakeScenario("s2", "My test scenario name [region: US]", outlineId: "MyTestScenarioName", exampleValues: new() { ["region"] = "US" });
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal("My test scenario name", groups[0].GroupDisplayName);
    }

    [Fact]
    public void OutlineId_already_humanized_stays_unchanged()
    {
        var s1 = MakeScenario("s1", "Calling create cake endpoint [flavour: Chocolate]", outlineId: "Calling create cake endpoint", exampleValues: new() { ["flavour"] = "Chocolate" });
        var s2 = MakeScenario("s2", "Calling create cake endpoint [flavour: Vanilla]", outlineId: "Calling create cake endpoint", exampleValues: new() { ["flavour"] = "Vanilla" });
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal("Calling create cake endpoint", groups[0].GroupDisplayName);
    }

    [Fact]
    public void Three_scenarios_grouped_by_display_name_prefix()
    {
        var s1 = MakeScenario("s1", "Ns.Class.Method(a: 1)");
        var s2 = MakeScenario("s2", "Ns.Class.Method(a: 2)");
        var s3 = MakeScenario("s3", "Ns.Class.Method(a: 3)");
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1, s2, s3]);
        Assert.Single(groups);
        Assert.Empty(ungrouped);
        Assert.Equal(3, groups[0].Scenarios.Length);
    }

    [Fact]
    public void Group_with_ExampleDisplayName_uses_fallback_rule()
    {
        var s1 = MakeScenario("s1", "Happy path UK", outlineId: "Test", exampleDisplayName: "Happy path UK");
        var s2 = MakeScenario("s2", "Sad path US", outlineId: "Test", exampleDisplayName: "Sad path US");
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal(ParameterDisplayRule.Fallback, groups[0].Rule);
    }

    [Fact]
    public void Mixed_grouped_and_ungrouped()
    {
        var s1 = MakeScenario("s1", "Ns.Class.Method(a: 1)");
        var s2 = MakeScenario("s2", "Ns.Class.Method(a: 2)");
        var s3 = MakeScenario("s3", "StandaloneTest");
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1, s2, s3]);
        Assert.Single(groups);
        Assert.Single(ungrouped);
        Assert.Equal("StandaloneTest", ungrouped[0].DisplayName);
    }

    [Fact]
    public void Single_member_OutlineId_with_params_treated_as_group()
    {
        var s1 = MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" });
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1]);
        Assert.Single(groups);
        Assert.Empty(ungrouped);
    }

    [Fact]
    public void Single_member_OutlineId_without_params_treated_as_ungrouped()
    {
        var s1 = MakeScenario("s1", "Test variant A", outlineId: "Test");
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1]);
        Assert.Empty(groups);
        Assert.Single(ungrouped);
    }

    [Fact]
    public void Single_member_display_name_with_params_treated_as_group()
    {
        var s1 = MakeScenario("s1", "Calculate(x: 10, y: 20)");
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1]);
        Assert.Single(groups);
        Assert.Empty(ungrouped);
    }

    [Fact]
    public void Different_classes_same_method_name_each_become_single_member_groups()
    {
        var s1 = MakeScenario("s1", "Ns.ClassA.Method(a: 1)");
        var s2 = MakeScenario("s2", "Ns.ClassB.Method(a: 2)");
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1, s2]);
        // Different prefixes means they don't merge, but each has params so each becomes its own group
        Assert.Equal(2, groups.Length);
        Assert.Empty(ungrouped);
    }

    [Fact]
    public void Identical_diagrams_not_detected_by_default_when_no_diagram_comparer()
    {
        var s1 = MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" });
        var s2 = MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" });
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.False(groups[0].AllDiagramsIdentical);
    }

    [Fact]
    public void Identical_diagrams_detected_when_comparer_says_identical()
    {
        var s1 = MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" });
        var s2 = MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" });
        var (groups, _) = ParameterGrouper.Analyze([s1, s2], diagramComparer: _ => true);
        Assert.True(groups[0].AllDiagramsIdentical);
    }

    [Fact]
    public void Non_identical_diagrams_when_comparer_says_different()
    {
        var s1 = MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" });
        var s2 = MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" });
        var (groups, _) = ParameterGrouper.Analyze([s1, s2], diagramComparer: _ => false);
        Assert.False(groups[0].AllDiagramsIdentical);
    }

    [Fact]
    public void Grouping_disabled_still_groups_by_OutlineId()
    {
        var s1 = MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" });
        var s2 = MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" });
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1, s2], enabled: false);
        Assert.Single(groups);
        Assert.Empty(ungrouped);
    }

    [Fact]
    public void Grouping_disabled_does_not_group_by_display_name_prefix()
    {
        var s1 = MakeScenario("s1", "Test(a: 1)");
        var s2 = MakeScenario("s2", "Test(a: 2)");
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1, s2], enabled: false);
        Assert.Empty(groups);
        Assert.Equal(2, ungrouped.Length);
    }

    [Fact]
    public void Extracts_parameter_names_from_ExampleValues_keys()
    {
        var s1 = MakeScenario("s1", "Test(a: 1, b: 2)", outlineId: "Test",
            exampleValues: new() { ["region"] = "UK", ["amount"] = "100" });
        var s2 = MakeScenario("s2", "Test(a: 3, b: 4)", outlineId: "Test",
            exampleValues: new() { ["region"] = "US", ["amount"] = "200" });
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Contains("region", groups[0].ParameterNames);
        Assert.Contains("amount", groups[0].ParameterNames);
    }

    [Fact]
    public void Falls_back_to_parsed_params_when_no_ExampleValues()
    {
        var s1 = MakeScenario("s1", "Ns.Class.Method(region: \"UK\", amount: 100)");
        var s2 = MakeScenario("s2", "Ns.Class.Method(region: \"US\", amount: 200)");
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Contains("region", groups[0].ParameterNames);
        Assert.Contains("amount", groups[0].ParameterNames);
    }

    [Fact]
    public void OutlineId_groups_take_precedence_over_display_name_grouping()
    {
        // s1+s2 share OutlineId "GroupA", s3 has different OutlineId "GroupB" (single member with params → still a group)
        var s1 = MakeScenario("s1", "Test variant A", outlineId: "GroupA", exampleValues: new() { ["x"] = "1" });
        var s2 = MakeScenario("s2", "Test variant B", outlineId: "GroupA", exampleValues: new() { ["x"] = "2" });
        var s3 = MakeScenario("s3", "Test variant C", outlineId: "GroupB", exampleValues: new() { ["x"] = "3" });
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1, s2, s3]);
        Assert.Equal(2, groups.Length); // GroupA (2 members) and GroupB (1 member with params)
        Assert.Empty(ungrouped);
    }

    [Fact]
    public void Bracketed_display_names_grouped_correctly()
    {
        var s1 = MakeScenario("s1", "Scenario name [count: 5]");
        var s2 = MakeScenario("s2", "Scenario name [count: 10]");
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal("Scenario name", groups[0].GroupDisplayName);
    }

    [Fact]
    public void Multiple_separate_brackets_grouped_correctly()
    {
        var s1 = MakeScenario("s1", "Scenario name [version: \"V1\"] [claimName: \"LivePersonSdes\"]");
        var s2 = MakeScenario("s2", "Scenario name [version: \"V2\"] [claimName: \"OtherClaim\"]");
        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal("Scenario name", groups[0].GroupDisplayName);
        Assert.Contains("version", groups[0].ParameterNames);
        Assert.Contains("claimName", groups[0].ParameterNames);
    }

    [Fact]
    public void Single_scenario_with_multiple_brackets_becomes_group()
    {
        var s1 = MakeScenario("s1", "Scenario name [version: \"V1\"] [claimName: \"LivePersonSdes\"]");
        var (groups, ungrouped) = ParameterGrouper.Analyze([s1]);
        Assert.Single(groups);
        Assert.Empty(ungrouped);
        Assert.Equal("Scenario name", groups[0].GroupDisplayName);
        Assert.Contains("version", groups[0].ParameterNames);
        Assert.Contains("claimName", groups[0].ParameterNames);
    }

    [Fact]
    public void Max_columns_exceeded_uses_fallback()
    {
        var vals = new Dictionary<string, string>();
        for (var i = 0; i < 12; i++) vals[$"col{i}"] = $"val{i}";
        var s1 = MakeScenario("s1", "Test(many params)", outlineId: "Test", exampleValues: new(vals));
        vals = new Dictionary<string, string>();
        for (var i = 0; i < 12; i++) vals[$"col{i}"] = $"val{i + 10}";
        var s2 = MakeScenario("s2", "Test(many params)", outlineId: "Test", exampleValues: new(vals));
        var (groups, _) = ParameterGrouper.Analyze([s1, s2], maxColumns: 10);
        Assert.Single(groups);
        Assert.Equal(ParameterDisplayRule.Fallback, groups[0].Rule);
    }

    private record TestRequest(string Region, int Amount, string Currency);
    private record NestedRequest(string Name, TestRequest Inner);
    private record RecordWithArray(string Name, string[] Items);

    [Fact]
    public void R2_single_complex_param_with_all_scalar_props_uses_FlattenedObject()
    {
        var obj1 = new TestRequest("UK", 100, "GBP");
        var obj2 = new TestRequest("US", 200, "USD");
        var s1 = MakeScenario("s1", "Test(request)", outlineId: "Test",
            exampleValues: new() { ["request"] = obj1.ToString()! },
            exampleRawValues: new() { ["request"] = obj1 });
        var s2 = MakeScenario("s2", "Test(request)", outlineId: "Test",
            exampleValues: new() { ["request"] = obj2.ToString()! },
            exampleRawValues: new() { ["request"] = obj2 });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal(ParameterDisplayRule.FlattenedObject, groups[0].Rule);
        Assert.Contains("Region", groups[0].ParameterNames);
        Assert.Contains("Amount", groups[0].ParameterNames);
        Assert.Contains("Currency", groups[0].ParameterNames);
        // ExampleValues should be flattened on the group's cloned scenarios
        Assert.Equal("UK", groups[0].Scenarios[0].ExampleValues!["Region"]);
        Assert.Equal("100", groups[0].Scenarios[0].ExampleValues!["Amount"]);
    }

    [Fact]
    public void R2_nested_complex_flattens_via_string_based_when_reflection_rejects()
    {
        var obj1 = new NestedRequest("Test1", new TestRequest("UK", 100, "GBP"));
        var obj2 = new NestedRequest("Test2", new TestRequest("US", 200, "USD"));
        var s1 = MakeScenario("s1", "Test(request)", outlineId: "Test",
            exampleValues: new() { ["request"] = obj1.ToString()! },
            exampleRawValues: new() { ["request"] = obj1 });
        var s2 = MakeScenario("s2", "Test(request)", outlineId: "Test",
            exampleValues: new() { ["request"] = obj2.ToString()! },
            exampleRawValues: new() { ["request"] = obj2 });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        // Reflection-based R2 rejects nested complex, but string-based R2 flattens at top level
        Assert.Equal(ParameterDisplayRule.FlattenedObject, groups[0].Rule);
        Assert.Equal(["Name", "Inner"], groups[0].ParameterNames);
        Assert.Equal("Test1", groups[0].Scenarios[0].ExampleValues!["Name"]);  // Cloned scenario
    }

    [Fact]
    public void R2_string_based_applied_when_no_ExampleRawValues_but_record_ToString_matches()
    {
        var s1 = MakeScenario("s1", "Test(request)", outlineId: "Test",
            exampleValues: new() { ["request"] = "TestRequest { Region = UK }" });
        var s2 = MakeScenario("s2", "Test(request)", outlineId: "Test",
            exampleValues: new() { ["request"] = "TestRequest { Region = US }" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        // String-based R2 now parses record ToString() and flattens into columns
        Assert.Equal(ParameterDisplayRule.FlattenedObject, groups[0].Rule);
        Assert.Equal(["Region"], groups[0].ParameterNames);
        Assert.Equal("UK", groups[0].Scenarios[0].ExampleValues!["Region"]);
        Assert.Equal("US", groups[0].Scenarios[1].ExampleValues!["Region"]);
    }

    [Fact]
    public void R2_not_applied_when_multiple_params()
    {
        var obj1 = new TestRequest("UK", 100, "GBP");
        var s1 = MakeScenario("s1", "Test(a, b)", outlineId: "Test",
            exampleValues: new() { ["a"] = "foo", ["b"] = "bar" },
            exampleRawValues: new() { ["a"] = "foo", ["b"] = obj1 });
        var s2 = MakeScenario("s2", "Test(a, b)", outlineId: "Test",
            exampleValues: new() { ["a"] = "baz", ["b"] = "qux" },
            exampleRawValues: new() { ["a"] = "baz", ["b"] = obj1 });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal(ParameterDisplayRule.ScalarColumns, groups[0].Rule);
    }

    [Fact]
    public void R2_not_applied_when_properties_exceed_maxColumns()
    {
        var obj1 = new TestRequest("UK", 100, "GBP");
        var s1 = MakeScenario("s1", "Test(request)", outlineId: "Test",
            exampleValues: new() { ["request"] = obj1.ToString()! },
            exampleRawValues: new() { ["request"] = obj1 });
        var s2 = MakeScenario("s2", "Test(request)", outlineId: "Test",
            exampleValues: new() { ["request"] = obj1.ToString()! },
            exampleRawValues: new() { ["request"] = obj1 });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2], maxColumns: 2);
        Assert.Single(groups);
        // 3 properties > maxColumns 2, so falls back to ScalarColumns with the single key
        Assert.Equal(ParameterDisplayRule.ScalarColumns, groups[0].Rule);
    }

    [Fact]
    public void R2_string_based_flattens_record_ToString_when_no_raw_values()
    {
        var s1 = MakeScenario("s1", "Test(scenario)", outlineId: "Test",
            exampleValues: new() { ["scenario"] = "ScoreScenario { Age = 89, Score = 320, Band = E }" });
        var s2 = MakeScenario("s2", "Test(scenario)", outlineId: "Test",
            exampleValues: new() { ["scenario"] = "ScoreScenario { Age = 25, Score = 500, Band = A }" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal(ParameterDisplayRule.FlattenedObject, groups[0].Rule);
        Assert.Equal(["Age", "Score", "Band"], groups[0].ParameterNames);
        Assert.Equal("89", groups[0].Scenarios[0].ExampleValues!["Age"]);
        Assert.Equal("500", groups[0].Scenarios[1].ExampleValues!["Score"]);
    }

    [Fact]
    public void R2_string_based_not_applied_when_value_is_not_record_format()
    {
        var s1 = MakeScenario("s1", "Test(name: hello)", outlineId: "Test",
            exampleValues: new() { ["name"] = "hello" });
        var s2 = MakeScenario("s2", "Test(name: world)", outlineId: "Test",
            exampleValues: new() { ["name"] = "world" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal(ParameterDisplayRule.ScalarColumns, groups[0].Rule);
        Assert.Equal(["name"], groups[0].ParameterNames);
    }

    [Fact]
    public void R2_string_based_not_applied_when_properties_exceed_maxColumns()
    {
        var s1 = MakeScenario("s1", "Test(scenario)", outlineId: "Test",
            exampleValues: new() { ["scenario"] = "Type { A = 1, B = 2, C = 3 }" });
        var s2 = MakeScenario("s2", "Test(scenario)", outlineId: "Test",
            exampleValues: new() { ["scenario"] = "Type { A = 4, B = 5, C = 6 }" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2], maxColumns: 2);
        Assert.Single(groups);
        // 3 properties > maxColumns 2, falls back to ScalarColumns
        Assert.Equal(ParameterDisplayRule.ScalarColumns, groups[0].Rule);
    }

    [Fact]
    public void R2_string_based_not_applied_when_members_have_different_property_names()
    {
        var s1 = MakeScenario("s1", "Test(scenario)", outlineId: "Test",
            exampleValues: new() { ["scenario"] = "Type { A = 1, B = 2 }" });
        var s2 = MakeScenario("s2", "Test(scenario)", outlineId: "Test",
            exampleValues: new() { ["scenario"] = "Type { X = 3, Y = 4 }" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        // Different properties → can't flatten
        Assert.Equal(ParameterDisplayRule.ScalarColumns, groups[0].Rule);
    }

    [Fact]
    public void R2_string_based_handles_null_values_in_record()
    {
        var s1 = MakeScenario("s1", "Test(scenario)", outlineId: "Test",
            exampleValues: new() { ["scenario"] = "Risk { Score = 320, Band = null }" });
        var s2 = MakeScenario("s2", "Test(scenario)", outlineId: "Test",
            exampleValues: new() { ["scenario"] = "Risk { Score = 500, Band = A }" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal(ParameterDisplayRule.FlattenedObject, groups[0].Rule);
        Assert.Equal("null", groups[0].Scenarios[0].ExampleValues!["Band"]);
        Assert.Equal("A", groups[0].Scenarios[1].ExampleValues!["Band"]);
    }

    [Fact]
    public void FlatParameterNames_populated_when_scenarios_have_ExampleFlatValues()
    {
        var s1 = MakeScenario("s1", "Test(recipe)", outlineId: "Test",
            exampleValues: new() { ["Recipe"] = "{ Flour = Plain }" },
            exampleFlatValues: new() { ["RecipeName"] = "Classic", ["Flour"] = "Plain", ["Sugar"] = "100g" });
        var s2 = MakeScenario("s2", "Test(recipe)", outlineId: "Test",
            exampleValues: new() { ["Recipe"] = "{ Flour = Whole }" },
            exampleFlatValues: new() { ["RecipeName"] = "Rustic", ["Flour"] = "Whole", ["Sugar"] = "80g" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.NotNull(groups[0].FlatParameterNames);
        Assert.Equal(["RecipeName", "Flour", "Sugar"], groups[0].FlatParameterNames);
    }

    [Fact]
    public void FlatParameterNames_null_when_scenarios_have_no_ExampleFlatValues()
    {
        var s1 = MakeScenario("s1", "Test(region: UK)", outlineId: "Test",
            exampleValues: new() { ["region"] = "UK" });
        var s2 = MakeScenario("s2", "Test(region: US)", outlineId: "Test",
            exampleValues: new() { ["region"] = "US" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Null(groups[0].FlatParameterNames);
    }

    [Fact]
    public void FlatParameterNames_null_when_only_some_scenarios_have_ExampleFlatValues()
    {
        var s1 = MakeScenario("s1", "Test(recipe)", outlineId: "Test",
            exampleValues: new() { ["Recipe"] = "{ Flour = Plain }" },
            exampleFlatValues: new() { ["Flour"] = "Plain" });
        var s2 = MakeScenario("s2", "Test(recipe)", outlineId: "Test",
            exampleValues: new() { ["Recipe"] = "{ Flour = Whole }" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Null(groups[0].FlatParameterNames);
    }

    [Fact]
    public void ExampleFlatValues_deep_cloned_during_Analyze()
    {
        var originalFlat = new Dictionary<string, string> { ["Flour"] = "Plain" };
        var s1 = MakeScenario("s1", "Test(recipe)", outlineId: "Test",
            exampleValues: new() { ["Recipe"] = "{ Flour = Plain }" },
            exampleFlatValues: originalFlat);
        var s2 = MakeScenario("s2", "Test(recipe)", outlineId: "Test",
            exampleValues: new() { ["Recipe"] = "{ Flour = Whole }" },
            exampleFlatValues: new() { ["Flour"] = "Whole" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        // Modify original — should not affect cloned values
        originalFlat["Flour"] = "MUTATED";
        Assert.Equal("Plain", groups[0].Scenarios[0].ExampleFlatValues!["Flour"]);
    }

    [Fact]
    public void Members_are_stable_sorted_by_examples_block_index()
    {
        // Interleaved input: block 1 row, block 0 row, block 1 row.
        var s1 = MakeScenario("s1", "T (B1a)", outlineId: "T", exampleValues: new() { ["v"] = "B1a" });
        var s2 = MakeScenario("s2", "T (B0a)", outlineId: "T", exampleValues: new() { ["v"] = "B0a" });
        var s3 = MakeScenario("s3", "T (B1b)", outlineId: "T", exampleValues: new() { ["v"] = "B1b" });
        s1.ExamplesBlockIndex = 1;
        s2.ExamplesBlockIndex = 0;
        s3.ExamplesBlockIndex = 1;

        var (groups, _) = ParameterGrouper.Analyze([s1, s2, s3]);
        Assert.Single(groups);
        Assert.Equal(["s2", "s1", "s3"], groups[0].Scenarios.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Members_without_block_index_keep_order_and_land_after_indexed_ones()
    {
        var s1 = MakeScenario("s1", "T (n1)", outlineId: "T", exampleValues: new() { ["v"] = "n1" });
        var s2 = MakeScenario("s2", "T (b0)", outlineId: "T", exampleValues: new() { ["v"] = "b0" });
        var s3 = MakeScenario("s3", "T (n2)", outlineId: "T", exampleValues: new() { ["v"] = "n2" });
        s2.ExamplesBlockIndex = 0;

        var (groups, _) = ParameterGrouper.Analyze([s1, s2, s3]);
        Assert.Single(groups);
        Assert.Equal(["s2", "s1", "s3"], groups[0].Scenarios.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Blockless_group_member_order_is_unchanged()
    {
        var s1 = MakeScenario("s1", "T (b)", outlineId: "T", exampleValues: new() { ["v"] = "b" });
        var s2 = MakeScenario("s2", "T (a)", outlineId: "T", exampleValues: new() { ["v"] = "a" });

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        Assert.Single(groups);
        Assert.Equal(["s1", "s2"], groups[0].Scenarios.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Block_fields_survive_the_analyze_deep_clone()
    {
        var s1 = MakeScenario("s1", "T (a)", outlineId: "T", exampleValues: new() { ["v"] = "a" });
        var s2 = MakeScenario("s2", "T (b)", outlineId: "T", exampleValues: new() { ["v"] = "b" });
        s1.ExamplesBlockName = "named block";
        s1.ExamplesBlockDescription = "a description";
        s1.ExamplesBlockIndex = 0;
        s2.ExamplesBlockIndex = 1;

        var (groups, _) = ParameterGrouper.Analyze([s1, s2]);
        var first = groups[0].Scenarios.Single(s => s.Id == "s1");
        Assert.Equal("named block", first.ExamplesBlockName);
        Assert.Equal("a description", first.ExamplesBlockDescription);
        Assert.Equal(0, first.ExamplesBlockIndex);
    }

    private static Scenario MakeScenario(
        string id, string displayName,
        string? outlineId = null,
        Dictionary<string, string>? exampleValues = null,
        Dictionary<string, object?>? exampleRawValues = null,
        string? exampleDisplayName = null,
        Dictionary<string, string>? exampleFlatValues = null,
        ExecutionResult result = ExecutionResult.Passed)
    {
        return new Scenario
        {
            Id = id,
            DisplayName = displayName,
            OutlineId = outlineId,
            ExampleValues = exampleValues,
            ExampleRawValues = exampleRawValues,
            ExampleDisplayName = exampleDisplayName,
            ExampleFlatValues = exampleFlatValues,
            Result = result
        };
    }
}
