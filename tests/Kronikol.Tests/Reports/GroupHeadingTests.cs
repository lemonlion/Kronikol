using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// An outline's group is headed by what its rows show, never by its key read by a humaniser a second time
/// (plans/TITLES_AS_WRITTEN_PLAN.md 4.3, F4, B4).
/// </summary>
public class GroupHeadingTests
{
    private static Scenario Row(string id, string name, string outlineId, string? outlineTitle = null, bool asWritten = false,
        bool keyedByOutline = false) => new()
    {
        Id = id,
        DisplayName = name,
        OutlineId = outlineId,
        OutlineTitle = outlineTitle,
        NameAsWritten = asWritten,
        KeyedByOutline = keyedByOutline,
        ExampleValues = new() { ["row"] = id },
        Result = ExecutionResult.Passed,
    };

    private static string Heading(params Scenario[] rows) => Assert.Single(ParameterGrouper.Analyze(rows).Groups).GroupDisplayName;

    [Fact]
    public void A_generated_theory_is_headed_by_its_rows_shared_name_with_its_capitals()
    {
        // 5.16.4 humanised the outline id again: "Ships to the uk"
        Assert.Equal("Ships to the UK", Heading(
            Row("r1", "Ships to the UK [country: \"GB\"]", "ShipsToTheUK", "Ships to the UK"),
            Row("r2", "Ships to the UK [country: \"IE\"]", "ShipsToTheUK", "Ships to the UK")));
    }

    [Fact]
    public void A_theory_s_written_display_name_heads_its_group_as_written()
    {
        Assert.Equal("Orders ship to the UK and to Kafka", Heading(
            Row("r1", "Orders ship to the UK and to Kafka [country: \"GB\"]", "ShipsToTheUK", "Orders ship to the UK and to Kafka", asWritten: true),
            Row("r2", "Orders ship to the UK and to Kafka [country: \"IE\"]", "ShipsToTheUK", "Orders ship to the UK and to Kafka", asWritten: true)));
    }

    [Fact]
    public void Rows_named_apart_are_headed_by_the_outline_s_own_title_not_the_first_row()
    {
        // per-row names (TUnit [Arguments(DisplayName)], NUnit TestName, xUnit v3 TestDisplayName): a first-member rule
        // would head the group "Happy path UK"
        Assert.Equal("Places an order", Heading(
            Row("r1", "Happy path UK", "PlacesAnOrder", "Places an order", asWritten: true),
            Row("r2", "Happy path US", "PlacesAnOrder", "Places an order", asWritten: true)));
    }

    [Fact]
    public void A_LightBDD_outline_is_headed_as_LightBDD_wrote_it()
    {
        // B4: 5.16.4 lowered it, "Pays with visa", though the rows read "VISA"
        Assert.Equal("Pays with VISA", Heading(
            Row("r1", "Pays with VISA \"4111\"", "Pays with VISA", "Pays with VISA"),
            Row("r2", "Pays with VISA \"4012\"", "Pays with VISA", "Pays with VISA")));
    }

    [Fact]
    public void An_ingested_outline_is_headed_as_the_input_wrote_it()
    {
        Assert.Equal("Checkout via PayPal", Heading(
            Row("r1", "Checkout via PayPal for EUR", "Checkout via PayPal", "Checkout via PayPal"),
            Row("r2", "Checkout via PayPal for GBP", "Checkout via PayPal", "Checkout via PayPal")));
    }

    [Fact]
    public void A_Gherkin_outline_keeps_its_title_as_written()
    {
        Assert.Equal("Pays with <card>", Heading(
            Row("r1", "Pays with VISA", "Pays with <card>", asWritten: true),
            Row("r2", "Pays with AMEX", "Pays with <card>", asWritten: true)));
    }

    [Fact]
    public void A_Reqnroll_row_named_from_its_values_keeps_its_outline_s_title_even_where_rows_read_alike()
    {
        Assert.Equal("Pays with <card>", Heading(
            Row("r1", "Pays with VISA", "Pays with <card>", asWritten: true, keyedByOutline: true),
            Row("r2", "Pays with VISA", "Pays with <card>", asWritten: true, keyedByOutline: true)));
    }

    [Fact]
    public void Written_names_keep_the_brackets_they_wrote()
    {
        // TUnit and NUnit rows named whole, apart only inside their own parentheses: the method's title heads them, not
        // the text before the parenthesis
        Assert.Equal("Pays by card", Heading(
            Row("r1", "Happy path (UK)", "Ns.Payments.PaysByCard", outlineTitle: "Pays by card", asWritten: true),
            Row("r2", "Happy path (US)", "Ns.Payments.PaysByCard", outlineTitle: "Pays by card", asWritten: true)));
        // a Reqnroll outline with no placeholder: its rows read as its title, brackets and all
        Assert.Equal("Refund is issued (partial)", Heading(
            Row("r1", "Refund is issued (partial)", "Refund is issued (partial)", asWritten: true),
            Row("r2", "Refund is issued (partial)", "Refund is issued (partial)", asWritten: true)));
        // the arguments a lane put after a written name go
        Assert.Equal("Adds VAT", Heading(Row("r1", "Adds VAT [net: 1]", "Adds VAT", outlineTitle: "Adds VAT", asWritten: true)));
    }

    [Fact]
    public void CapitaliseTitles_recases_the_lane_s_outline_title_as_it_does_the_rows()
    {
        // a LightBDD name format in lower case, its values inline: 5.16.4 headed it "Paying with card"
        var feature = new Feature
        {
            DisplayName = "Payments",
            Scenarios =
            [
                Row("r1", "paying with card \"4111\"", "paying with card", outlineTitle: "paying with card"),
                Row("r2", "paying with card \"5500\"", "paying with card", outlineTitle: "paying with card"),
            ],
        };

        StepText.ApplyToTitles([feature]);

        Assert.Equal("Paying with card", Heading(feature.Scenarios));
    }

    [Fact]
    public void A_model_no_lane_titled_falls_back_to_the_heading_5_16_4_gave()
    {
        // a consumer's own Feature[], or a data file written before 5.16.5, whose rows are named apart
        Assert.Equal("Ships to the uk", Heading(
            Row("r1", "Ships GB", "ShipsToTheUK"),
            Row("r2", "Ships IE", "ShipsToTheUK")));
    }
}
