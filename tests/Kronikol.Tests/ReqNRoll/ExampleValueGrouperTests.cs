using Reqnroll;
using Kronikol.ReqNRoll;

namespace Kronikol.Tests.ReqNRoll;

public class ExampleValueGrouperTests
{
    [Fact]
    public void BuildStructured_WithNoStepTables_ReturnsFlatValues()
    {
        var flatValues = new Dictionary<string, string>
        {
            ["RecipeName"] = "Classic",
            ["Temperature"] = "180"
        };
        var steps = new List<ReqNRollStepInfo>
        {
            new("Given", "a recipe", TableText: null)
        };

        var (exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);

        Assert.Equal(flatValues, exampleValues);
        Assert.Null(exampleRawValues);
    }

    [Fact]
    public void BuildStructured_WithEmptyFlatValues_ReturnsEmpty()
    {
        var flatValues = new Dictionary<string, string>();
        var steps = new List<ReqNRollStepInfo>();

        var (exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);

        Assert.Empty(exampleValues);
        Assert.Null(exampleRawValues);
    }

    [Fact]
    public void BuildStructured_SingleRowTable_ProducesDictionary()
    {
        var flatValues = new Dictionary<string, string>
        {
            ["RecipeName"] = "Classic",
            ["Flour"] = "Plain Flour",
            ["Apples"] = "Granny Smith",
            ["Cinnamon"] = "Ceylon"
        };
        var steps = new List<ReqNRollStepInfo>
        {
            new("Given", "a recipe with the following ingredients:",
                TableText: "| Flour       | Apples       | Cinnamon |\n| Plain Flour | Granny Smith | Ceylon   |")
        };

        var (exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);

        // RecipeName stays as scalar, ingredients grouped
        Assert.True(exampleValues.ContainsKey("RecipeName"));
        Assert.Equal("Classic", exampleValues["RecipeName"]);
        Assert.True(exampleValues.ContainsKey("Ingredients"));
        Assert.False(exampleValues.ContainsKey("Flour"));
        Assert.False(exampleValues.ContainsKey("Apples"));
        Assert.False(exampleValues.ContainsKey("Cinnamon"));

        // Raw values: RecipeName is scalar, Ingredients is a dictionary
        Assert.NotNull(exampleRawValues);
        Assert.IsType<string>(exampleRawValues!["RecipeName"]);
        var ingredients = Assert.IsType<Dictionary<string, object?>>(exampleRawValues["Ingredients"]);
        Assert.Equal("Plain Flour", ingredients["Flour"]);
        Assert.Equal("Granny Smith", ingredients["Apples"]);
        Assert.Equal("Ceylon", ingredients["Cinnamon"]);
    }

    [Fact]
    public void BuildStructured_MultiRowTable_ProducesListOfDictionaries()
    {
        var flatValues = new Dictionary<string, string>
        {
            ["RecipeName"] = "Classic",
            ["Topping1"] = "Streusel",
            ["Topping2"] = "Icing Glaze"
        };
        var steps = new List<ReqNRollStepInfo>
        {
            new("Given", "the following muffin toppings:",
                TableText: "| Name        |\n| Streusel    |\n| Icing Glaze |")
        };

        var (exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);

        Assert.True(exampleValues.ContainsKey("RecipeName"));
        Assert.True(exampleValues.ContainsKey("Toppings"));
        Assert.False(exampleValues.ContainsKey("Topping1"));
        Assert.False(exampleValues.ContainsKey("Topping2"));

        Assert.NotNull(exampleRawValues);
        var toppings = Assert.IsType<List<Dictionary<string, object?>>>(exampleRawValues!["Toppings"]);
        Assert.Equal(2, toppings.Count);
        Assert.Equal("Streusel", toppings[0]["Name"]);
        Assert.Equal("Icing Glaze", toppings[1]["Name"]);
    }

    [Fact]
    public void BuildStructured_MultipleTables_NestUnderParent()
    {
        var flatValues = new Dictionary<string, string>
        {
            ["RecipeName"] = "Classic",
            ["Flour"] = "Plain Flour",
            ["Apples"] = "Granny Smith",
            ["Cinnamon"] = "Ceylon",
            ["Temperature"] = "180",
            ["Topping1"] = "Streusel",
            ["Topping2"] = "Icing Glaze"
        };
        var steps = new List<ReqNRollStepInfo>
        {
            new("Given", "a recipe with the following ingredients:",
                TableText: "| Flour       | Apples       | Cinnamon |\n| Plain Flour | Granny Smith | Ceylon   |"),
            new("And", "with baking at 180 degrees", TableText: null),
            new("And", "the following muffin toppings:",
                TableText: "| Name        |\n| Streusel    |\n| Icing Glaze |")
        };

        var (exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);

        // Scalars: RecipeName stays, Temperature stays (not consumed by any table)
        Assert.Equal("Classic", exampleValues["RecipeName"]);
        Assert.Equal("180", exampleValues["Temperature"]);

        // Multiple tables nested under parent "Recipe" (derived from "a recipe with")
        Assert.True(exampleValues.ContainsKey("Recipe"));
        Assert.False(exampleValues.ContainsKey("Flour"));
        Assert.False(exampleValues.ContainsKey("Topping1"));
        Assert.False(exampleValues.ContainsKey("Ingredients"));
        Assert.False(exampleValues.ContainsKey("Toppings"));

        Assert.NotNull(exampleRawValues);

        // The parent dict contains the nested groups
        var recipe = Assert.IsType<Dictionary<string, object?>>(exampleRawValues!["Recipe"]);
        var ingredients = Assert.IsType<Dictionary<string, object?>>(recipe["Ingredients"]);
        Assert.Equal("Plain Flour", ingredients["Flour"]);

        var toppings = Assert.IsType<List<Dictionary<string, object?>>>(recipe["Toppings"]);
        Assert.Equal(2, toppings.Count);
    }

    [Fact]
    public void A_step_in_capitals_groups_the_same_on_a_machine_whose_culture_cases_i_otherwise()
    {
        // Turkish pairs i with İ and ı with I, and a case-insensitive pattern follows the machine's culture unless told
        // not to: WITH did not match "with" there, so a recipe's tables lost their parent (until 4.16.3).
        var flatValues = new Dictionary<string, string>
        {
            ["Flour"] = "Plain Flour",
            ["Apples"] = "Granny Smith",
            ["Topping1"] = "Streusel",
            ["Topping2"] = "Icing Glaze"
        };
        var steps = new List<ReqNRollStepInfo>
        {
            new("Given", "A RECIPE WITH THE FOLLOWING INGREDIENTS:", TableText: "| Flour       | Apples       |\n| Plain Flour | Granny Smith |"),
            new("And", "THE FOLLOWING MUFFIN TOPPINGS:", TableText: "| Name        |\n| Streusel    |\n| Icing Glaze |")
        };
        string[] Keys() => ExampleValueGrouper.BuildStructured(flatValues, steps).Item1.Keys.Order(StringComparer.Ordinal).ToArray();

        var english = CultureRun.Under("en-US", Keys);
        var turkish = CultureRun.Under("tr-TR", Keys);

        Assert.Equal(["RECIPE"], english);
        Assert.Equal(english, turkish);
    }

    [Fact]
    public void BuildStructured_TableWithNoMatchingValues_NotGrouped()
    {
        var flatValues = new Dictionary<string, string>
        {
            ["RecipeName"] = "Classic"
        };
        var steps = new List<ReqNRollStepInfo>
        {
            // Table values don't match any ExampleValues
            new("Given", "some data:",
                TableText: "| A     | B     |\n| Alpha | Beta  |")
        };

        var (exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);

        // Falls back to flat
        Assert.Single(exampleValues);
        Assert.Equal("Classic", exampleValues["RecipeName"]);
        Assert.Null(exampleRawValues);
    }

    [Theory]
    [InlineData("the following muffin toppings:", "Toppings")]
    [InlineData("with the following ingredients:", "Ingredients")]
    [InlineData("the following baking profiles:", "Profiles")]
    [InlineData("some data:", "Data")]
    public void DeriveGroupName_ExtractsLastNoun(string stepText, string expected)
    {
        var result = ExampleValueGrouper.DeriveGroupName(stepText, ["Col1"]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeriveGroupName_FallsBackToHeaders()
    {
        // "the" is a generic word, so it falls through all patterns
        var result = ExampleValueGrouper.DeriveGroupName("the", ["Flour", "Apples"]);
        Assert.Equal("Flour + Apples", result);
    }

    [Fact]
    public void BuildStructured_ValueMatchingHandlesHeaderNameMatch()
    {
        // When the table header exactly matches an Example column name
        var flatValues = new Dictionary<string, string>
        {
            ["Name"] = "Classic",
            ["Flour"] = "Plain Flour"
        };
        var steps = new List<ReqNRollStepInfo>
        {
            new("Given", "the following ingredients:",
                TableText: "| Flour       |\n| Plain Flour |")
        };

        var (exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);

        // "Flour" consumed by table, "Name" stays as scalar
        Assert.True(exampleValues.ContainsKey("Name"));
        Assert.False(exampleValues.ContainsKey("Flour"));
        Assert.NotNull(exampleRawValues);
    }
}
