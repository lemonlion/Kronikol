using Kronikol.ReqNRoll;
using Kronikol.Reports;
using Reqnroll;

namespace Kronikol.Tests.Reports;

/// <summary>
/// #101: an outline row whose name Kronikol filled keeps the id and the place it had when it was named by its title
/// (plans/OUTLINE_ROW_NAMES_PLAN.md 3.3, facts K1 to K3 at the helper and the converter; the per-output facts are in
/// <c>OutlineRowKeySiteTests</c>).
/// </summary>
public class OutlineRowKeyTests
{
    private const string Suite = "Probe101.xUnit2";
    private const string Title = "A basket is weighed on the UK scale (<case>)";

    private static readonly Dictionary<string, string> TwoApples = new() { ["case"] = "two apples", ["count"] = "2", ["grams"] = "301" };

    private static Scenario MarkedRow() => new()
    {
        Id = "s2",
        DisplayName = "A basket is weighed on the UK scale (two apples)",
        OutlineId = Title,
        ExampleValues = TwoApples,
        KeyedByOutline = true,
    };

    [Fact]
    public void A_marked_row_keys_on_its_outline_s_title()
    {
        Assert.Equal(Title, ScenarioStableId.KeyName(MarkedRow()));
    }

    [Fact]
    public void A_marked_row_s_id_is_the_id_its_title_gave_it()
    {
        // 48092cbd5fd324ec is what 5.4.0 wrote for this row (plans/OUTLINE_ROW_NAMES_PLAN.harness/probe/results/ids-check.txt)
        Assert.Equal("48092cbd5fd324ec", ScenarioStableId.For(Suite, "Baskets", MarkedRow()));
        Assert.Equal(ScenarioStableId.Compute(Suite, "Baskets", Title, Title, TwoApples), ScenarioStableId.For(Suite, "Baskets", MarkedRow()));
    }

    // K2
    [Fact]
    public void An_unmarked_row_whose_name_differs_from_its_outline_keys_on_its_name()
    {
        // the Cucumber lane's shape: named from its pickle, never marked, and its ids are what they were
        var row = MarkedRow();
        row.KeyedByOutline = false;

        Assert.Equal(row.DisplayName, ScenarioStableId.KeyName(row));
        Assert.Equal(ScenarioStableId.Compute(Suite, "Baskets", row.DisplayName, Title, TwoApples), ScenarioStableId.For(Suite, "Baskets", row));
    }

    [Fact]
    public void A_marked_scenario_with_no_outline_keys_on_its_name()
    {
        var row = MarkedRow();
        row.OutlineId = null;

        Assert.Equal(row.DisplayName, ScenarioStableId.KeyName(row));
    }

    [Fact]
    public void An_unmarked_scenario_s_id_is_Compute_s()
    {
        var plain = new Scenario { Id = "p", DisplayName = "A plain scenario" };

        Assert.Equal(ScenarioStableId.Compute(Suite, "F", "A plain scenario"), ScenarioStableId.For(Suite, "F", plain));
        Assert.Equal(ScenarioStableId.Compute(null, "F", "A plain scenario"), ScenarioStableId.For(null, "F", plain));
    }

    // K3
    [Fact]
    public void A_marked_row_keys_on_its_title_as_CapitaliseTitles_re_cased_it()
    {
        // 5.4.0 named this row "a basket of <fruit>", and CapitaliseTitles made it "A basket of <fruit>" before its id was
        // computed. The key is read when the id is computed, so the re-cased title reaches it.
        var row = new Scenario
        {
            Id = "s0", DisplayName = "a basket of apples", OutlineId = "a basket of <fruit>",
            ExampleValues = new() { ["fruit"] = "apples" }, KeyedByOutline = true,
        };
        var feature = new Feature { DisplayName = "Baskets", Scenarios = [row] };

        StepText.ApplyToTitles([feature]);

        Assert.Equal("A basket of apples", row.DisplayName);
        Assert.Equal(ScenarioStableId.Compute(Suite, "Baskets", "A basket of <fruit>", "A basket of <fruit>", row.ExampleValues),
            ScenarioStableId.For(Suite, "Baskets", row));
    }

    // the converter
    private static ReqNRollScenarioInfo Info(string id, string title, string? rowName, int? block, params (string Key, string Value)[] values) => new()
    {
        ScenarioId = id,
        ScenarioTitle = title,
        RowName = rowName,
        FeatureTitle = "Baskets",
        ScenarioTags = [],
        CombinedTags = [],
        ExecutionStatus = ScenarioExecutionStatus.OK,
        OutlineId = values.Length > 0 ? title : null,
        ExampleValues = values.Length > 0 ? values.ToDictionary(v => v.Key, v => v.Value) : null,
        ExamplesBlockIndex = block,
    };

    [Fact]
    public void The_converter_names_a_row_by_its_row_name_and_marks_it()
    {
        var scenario = new[] { Info("s2", Title, "A basket is weighed on the UK scale (two apples)", 0, ("case", "two apples")) }
            .ToFeatures().Single().Scenarios.Single();

        Assert.Equal("A basket is weighed on the UK scale (two apples)", scenario.DisplayName);
        Assert.Equal(Title, scenario.OutlineId);
        Assert.True(scenario.KeyedByOutline);
    }

    [Theory]
    [InlineData("E01 a title without a placeholder", "E01 a title without a placeholder")]
    [InlineData("E03 weighs <count> on <missing>", "E03 weighs <count> on <missing>")]
    [InlineData("E06 <a>", null)]
    public void A_row_named_by_its_title_is_not_marked_and_is_what_it_was(string title, string? rowName)
    {
        var scenario = new[] { Info("s0", title, rowName, 0, ("a", "1")) }.ToFeatures().Single().Scenarios.Single();

        Assert.Equal(title, scenario.DisplayName);
        Assert.False(scenario.KeyedByOutline);
    }

    [Fact]
    public void A_plain_scenario_is_not_marked()
    {
        var scenario = new[] { Info("p", "A plain scenario with <x>", "A plain scenario with <x>", null) }
            .ToFeatures().Single().Scenarios.Single();

        Assert.Equal("A plain scenario with <x>", scenario.DisplayName);
        Assert.False(scenario.KeyedByOutline);
    }

    // K5, at the adapter: its order is the title's, then the block's, then the values', as when every row bore the title
    [Fact]
    public void The_converter_orders_rows_by_their_title_never_by_their_filled_names()
    {
        // block 0 holds zebra, block 1 holds apple: by the filled names apple would come first
        var scenarios = new[]
            {
                Info("s1", "Weigh <case>", "Weigh apple", 1, ("case", "apple")),
                Info("s0", "Weigh <case>", "Weigh zebra", 0, ("case", "zebra")),
            }
            .ToFeatures().Single().Scenarios;

        Assert.Equal(["s0", "s1"], scenarios.Select(s => s.Id));
    }
}
