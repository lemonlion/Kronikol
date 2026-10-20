using System.Reflection;
using Kronikol.Reports;
using Kronikol.xUnit3;

namespace Kronikol.Tests.xUnit3;

/// <summary>
/// Which of a test's <c>Category</c> traits are its feature's and which its own (plans/TAGS_PLAN.md §4.7, §5.2): the
/// class's are the feature's, and the test's, less the class's that xUnit merges into them, are the scenario's. No other
/// trait key is a tag (Q7). 4.x read none of them. Each fact maps its own running test's context, finished as xUnit
/// finishes it before the report reads it.
/// </summary>
[Trait("Category", "Slow")]
[Trait("Produces", "Orders")]
public class TagMappingTests : DiagrammedComponentTest
{
    /// <summary>The running test's context as the report reads it after the test: everything forwarded, the state passed.</summary>
    public class Finished : DispatchProxy
    {
        internal ITestContext Inner { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name == "get_" + nameof(ITestContext.TestState)
                ? TestResultState.ForPassed(0m)
                : targetMethod.Invoke(Inner, args);
    }

    private static Feature Mapped()
    {
        var context = DispatchProxy.Create<ITestContext, Finished>();
        ((Finished)(object)context).Inner = TestContext.Current;
        return new[] { context }.ToFeatures().Single();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void A_class_s_category_trait_is_its_feature_s_tag_and_a_test_s_is_its_scenario_s()
    {
        var feature = Mapped();

        Assert.Equal(["Slow"], feature.Tags);
        Assert.Equal(["Smoke"], feature.Scenarios.Single().Tags);
    }

    [Fact]
    [Trait("Produces", "Refunds")]
    public void A_trait_of_another_key_is_not_a_tag()
    {
        var feature = Mapped();

        Assert.Equal(["Slow"], feature.Tags);
        Assert.Null(feature.Scenarios.Single().Tags);
    }

    [Fact]
    [Trait("Category", "slow")]
    [Trait("Category", "Fast")]
    public void A_test_s_trait_that_repeats_its_class_s_in_another_case_stays_the_feature_s()
    {
        var feature = Mapped();

        Assert.Equal(["Slow"], feature.Tags);
        Assert.Equal(["Fast"], feature.Scenarios.Single().Tags);
    }

    [Fact]
    [Trait("Category", "happy-path")]
    public void A_happy_path_category_marks_the_happy_path_and_is_not_a_tag()
    {
        var scenario = Mapped().Scenarios.Single();

        Assert.True(scenario.IsHappyPath);
        Assert.Null(scenario.Tags);
    }
}
