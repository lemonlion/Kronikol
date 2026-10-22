using System.Reflection;
using Kronikol.Reports;
using Kronikol.xUnit3;
using Xunit.Sdk;

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

    /// <summary>An xUnit interface that answers the getters <see cref="Values"/> names, and nothing else.</summary>
    public class Fake : DispatchProxy
    {
        internal Dictionary<string, object?> Values { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name.StartsWith("get_", StringComparison.Ordinal) && Values.TryGetValue(targetMethod.Name[4..], out var value)
                ? value
                : throw new NotSupportedException(targetMethod.Name);
    }

    private static T FakeOf<T>(Dictionary<string, object?> values)
    {
        var fake = DispatchProxy.Create<T, Fake>();
        ((Fake)(object)fake!).Values = values;
        return fake;
    }

    /// <summary>
    /// The finished context of a test <paramref name="method"/> of a class named Orders whose <c>Category</c> traits are
    /// <paramref name="classCategories"/>, which xUnit merges into the test's, as the report reads it.
    /// </summary>
    private static ITestContext OrdersTest(string method, params string[] classCategories)
    {
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> traits =
            new Dictionary<string, IReadOnlyCollection<string>> { ["Category"] = classCategories };
        var testClass = FakeOf<ITestClass>(new() { ["TestClassSimpleName"] = "Orders", ["Traits"] = traits });
        var test = FakeOf<ITest>(new() { ["UniqueID"] = $"Orders.{method}.{Guid.NewGuid():N}", ["TestDisplayName"] = method, ["Traits"] = traits });
        return FakeOf<ITestContext>(new()
        {
            ["Test"] = test, ["TestClass"] = testClass, ["TestCase"] = null, ["TestState"] = TestResultState.ForPassed(0m)
        });
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_classes_of_one_name_keep_their_own_categories(bool apiFirst)
    {
        // Two classes Orders in two namespaces are one feature. Until 5.2.7 it took the class traits of whichever test
        // finished first, so the other class's tests carried them and their own were nowhere; the one both have stays
        // the feature's.
        var web = OrdersTest("Pay", "Web", "Shop");
        var api = OrdersTest("Refund", "Api", "Shop");

        var feature = (apiFirst ? new[] { api, web } : [web, api]).ToFeatures().Single();

        Assert.Equal(["Shop"], feature.Tags);
        Assert.Equal(["Web"], feature.Scenarios.Single(s => s.DisplayName == "Pay").Tags);
        Assert.Equal(["Api"], feature.Scenarios.Single(s => s.DisplayName == "Refund").Tags);
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
