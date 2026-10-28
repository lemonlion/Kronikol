using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A scenario whose title the rule now reads better, or whose written name is now shown as written, keeps the id 5.16.4
/// gave it: its lane records what 5.16.4 hashed (plans/TITLES_AS_WRITTEN_PLAN.md 4.4, F6).
/// </summary>
public class ScenarioIdKeyTests
{
    private static readonly Dictionary<string, string> EuValues = new() { ["arg0"] = "eu" };

    [Fact]
    public void The_id_keys_on_the_recorded_name_outline_and_values()
    {
        var scenario = new Scenario
        {
            Id = "s1",
            DisplayName = "Ships to UK [EU]",
            NameAsWritten = true,
            StableIdKey = new ScenarioIdKey("Ships to uk [eu]", "Ships to uk", EuValues),
        };

        Assert.Equal(ScenarioStableId.Compute("suite", "Orders", "Ships to uk [eu]", "Ships to uk", EuValues),
            ScenarioStableId.For("suite", "Orders", scenario));
        Assert.Equal("Ships to uk [eu]", ScenarioStableId.KeyName(scenario));
    }

    [Fact]
    public void Without_a_key_the_id_keys_on_the_scenario_as_it_is()
    {
        var plain = new Scenario { Id = "s1", DisplayName = "Ships", OutlineId = "Ships", ExampleValues = EuValues };
        var row = new Scenario { Id = "s2", DisplayName = "Ships to EU", OutlineId = "Ships to <region>", KeyedByOutline = true };

        Assert.Equal(ScenarioStableId.Compute(null, "Orders", "Ships", "Ships", EuValues), ScenarioStableId.For(null, "Orders", plain));
        Assert.Equal("Ships to <region>", ScenarioStableId.KeyName(row));
    }

    [Fact]
    public void A_key_is_recorded_only_where_the_scenario_differs_from_what_5_16_4_hashed()
    {
        var frozen = new ScenarioIdKey("Gets api", "Gets api", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

        Assert.Null(ScenarioStableId.KeyWhereChanged(frozen, "Gets api", "Gets api", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }));
        Assert.Same(frozen, ScenarioStableId.KeyWhereChanged(frozen, "Gets API", "Gets api", frozen.ExampleValues));
        Assert.Same(frozen, ScenarioStableId.KeyWhereChanged(frozen, "Gets api", "Gets API", frozen.ExampleValues));
        Assert.Same(frozen, ScenarioStableId.KeyWhereChanged(frozen, "Gets api", "Gets api", new Dictionary<string, string> { ["a"] = "1" }));
        Assert.Null(ScenarioStableId.KeyWhereChanged(new ScenarioIdKey("X", null, null), "X", null, new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Capitalising_titles_capitalises_the_key_as_it_did_the_name_it_stands_for(bool capitalise)
    {
        // 5.16.4 hashed the name after CapitaliseTitles: "returns 404" as "Returns 404", and past a marker glyph
        var feature = new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1",
                    DisplayName = "- returns 404 for the UK [EU]",
                    NameAsWritten = true,
                    StableIdKey = new ScenarioIdKey("- returns 404 for the uk [eu]", "- returns 404 for the uk", EuValues),
                },
            ],
        };
        if (capitalise)
            StepText.ApplyToTitles([feature]);

        var key = feature.Scenarios[0].StableIdKey!;
        Assert.Equal(capitalise ? "- Returns 404 for the uk [eu]" : "- returns 404 for the uk [eu]", key.Name);
        Assert.Equal(capitalise ? "- Returns 404 for the uk" : "- returns 404 for the uk", key.OutlineId);
        Assert.Same(EuValues, key.ExampleValues);
    }
}
