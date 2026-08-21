using Kronikol.Constants;

namespace Kronikol.Tests.Constants;

public class DependencyPaletteAiTests
{
    [Fact]
    public void AI_category_resolves_to_its_own_type_colour_and_shape()
    {
        Assert.Equal("AI", DependencyCategories.AI);
        Assert.Equal(DependencyType.AI, DependencyPalette.Resolve(DependencyCategories.AI));
        Assert.Equal(DependencyType.AI, DependencyPalette.Resolve("ai"));
        Assert.Equal("#16A085", DependencyPalette.GetColor(DependencyCategories.AI));
        Assert.Equal("control", DependencyPalette.GetSequenceShape(DependencyType.AI));
        Assert.NotEqual(DependencyPalette.GetColor(DependencyCategories.AI), DependencyPalette.GetColor(null));
    }

    [Fact]
    public void User_override_still_wins_for_AI()
    {
        Assert.Equal("#123456", DependencyPalette.GetColor(DependencyCategories.AI, new Dictionary<string, string> { ["AI"] = "#123456" }));
    }
}
