using Kronikol.TUnit;
using CategoryAttribute = TUnit.Core.CategoryAttribute;

// The assembly level of the facts below; TUnit does not run this project, so it reaches no test.
[assembly: Category("Assembly level")]

namespace Kronikol.Tests.TUnit;

/// <summary>
/// Which of a test's <c>[Category]</c> values are its feature's and which its own (plans/TAGS_PLAN.md §4.7, §5.2). TUnit
/// merges the method's, the class's and the assembly's into <c>TestDetails.Categories</c> with no level, so the class's
/// and the assembly's are read from the class by reflection and are the feature's (Q12), and the scenario's own are the
/// rest. 4.x read none of them, though it read <c>Happy Path</c> from the same list. TUnit builds its contexts itself, so
/// the facts drive the seam <c>ToFeatures</c> reads them through.
/// </summary>
public class TagMappingTests
{
    [Category("Slow")]
    private sealed class Categorized : DiagrammedComponentTest;

    [Category("Inherited")]
    private abstract class CategorizedBase : DiagrammedComponentTest;

    [Category("Own")]
    private sealed class Derived : CategorizedBase;

    [HappyPath]
    private sealed class AllHappy : DiagrammedComponentTest;

    private sealed class Plain : DiagrammedComponentTest;

    [Fact]
    public void The_class_s_categories_and_the_assembly_s_are_the_feature_s()
    {
        Assert.Equal(["Slow", "Assembly level"], TestTags.OfFeature(typeof(Categorized)));
    }

    [Fact]
    public void A_base_class_s_category_is_the_feature_s_after_the_class_s_own()
    {
        Assert.Equal(["Own", "Inherited", "Assembly level"], TestTags.OfFeature(typeof(Derived)));
    }

    [Fact]
    public void A_test_s_own_tags_are_its_categories_less_the_feature_s()
    {
        // as TUnit lists them: the method's, then the class's and the assembly's; the method's slow repeats Slow (§4.3)
        var (tags, happy) = TestTags.OfScenario(["Smoke", "slow", "Slow", "Assembly level"], TestTags.OfFeature(typeof(Categorized)));

        Assert.Equal(["Smoke"], tags);
        Assert.False(happy);
    }

    [Theory]
    [InlineData("Happy Path")]
    [InlineData("happy-path")]
    public void A_happy_path_category_marks_the_happy_path_and_is_not_a_tag(string name)
    {
        var (tags, happy) = TestTags.OfScenario([name, "Smoke"], TestTags.OfFeature(typeof(Plain)));

        Assert.Equal(["Smoke"], tags);
        Assert.True(happy);
    }

    [Fact]
    public void HappyPath_on_a_class_marks_every_one_of_its_scenarios_as_4x_did_and_is_no_tag()
    {
        var feature = TestTags.OfFeature(typeof(AllHappy));
        var (tags, happy) = TestTags.OfScenario(["Happy Path", "Assembly level"], feature);

        Assert.Equal(["Assembly level"], feature);
        Assert.Empty(tags);
        Assert.True(happy);
    }
}
