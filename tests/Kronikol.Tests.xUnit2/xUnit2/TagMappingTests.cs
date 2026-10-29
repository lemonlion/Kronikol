using Kronikol.Reports;
using Kronikol.xUnit2;
using Xunit.Abstractions;
using static Kronikol.Tests.xUnit2.XunitMessages;

namespace Kronikol.Tests.xUnit2;

// What the facts below capture: test classes with Category traits on the class, on a base class and on the methods, and
// a trait of another key. No [Fact], so xUnit never runs them.
[TestTracking]
[Trait("Category", "Slow")]
[Trait("Produces", "Orders")]
public class CategorizedSinkTests
{
    [Trait("Category", "Smoke")]
    [Trait("Category", "slow")]
    public void Tagged() { }

    public void Untagged() { }

    [Trait("Category", "happy-path")]
    public void Happy() { }
}

[TestTracking]
[Trait("Category", "Inherited")]
public abstract class CategorizedSinkBase;

[Trait("Category", "Own")]
public class DerivedCategorizedSinkTests : CategorizedSinkBase
{
    [Trait("Category", "Smoke")]
    public void Inherits() { }
}

/// <summary>
/// Which of a test's <c>Category</c> traits are its feature's and which its own (plans/TAGS_PLAN.md §4.7, §5.2): the
/// class's, its base classes' included, are the feature's, and the method's are the scenario's, on both of the
/// adapter's capture paths: the results sink, which makes the scenario when xUnit starts the test, and the attribute's
/// <c>Before</c>, which makes it when no sink handed one over. No other trait key is a tag (Q7). 4.x read none of them.
/// The expected tags are arrays, not collection expressions: with C# 14's span conversions <c>Assert.Equal(["Slow"],
/// tags)</c> is ambiguous on xunit.assert 2.6.1 to 2.9.0, which the floor search builds against
/// (plans/CLIENT_FLOORS_PLAN.md R3a).
/// </summary>
[Collection("CollectedScenarios")]
public class TagMappingTests : IDisposable
{
    private readonly List<IReadOnlyList<ScenarioInfo>> _written = [];

    public TagMappingTests() => XUnit2TestTrackingContext.CollectedScenarios.Clear();

    public void Dispose() => XUnit2TestTrackingContext.CollectedScenarios.Clear();

    private IReadOnlyList<ScenarioInfo> ThroughTheSink(params ITest[] tests)
    {
        using (var sink = new KronikolResultsSink(new RecordingSink(), (scenarios, _, _, _) => _written.Add(scenarios)))
        {
            sink.OnMessage(AssemblyStarting());
            foreach (var test in tests)
            {
                sink.OnMessage(Starting(test));
                sink.OnMessage(Passed(test));
                sink.OnMessage(Finished(test));
            }
            sink.OnMessage(AssemblyFinished());
        }
        return Assert.Single(_written);
    }

    private static ScenarioInfo ThroughTheAttribute(Type type, string method)
    {
        new TestTrackingAttribute().Before(type.GetMethod(method)!);
        return XUnit2TestTrackingContext.CollectedScenarios.Values.Single(s => s.MethodMatchKey == $"{type.FullName}.{method}");
    }

    [Fact]
    public void The_sink_gives_the_feature_the_class_s_category_and_the_scenario_the_method_s()
    {
        var scenarios = ThroughTheSink(Test<CategorizedSinkTests>(nameof(CategorizedSinkTests.Tagged)),
            Test<CategorizedSinkTests>(nameof(CategorizedSinkTests.Untagged)));

        var feature = scenarios.ToFeatures().Single();

        Assert.Equal(new[] { "Slow" }, feature.Tags);
        // the method's slow repeats the class's Slow in another case, so it is the feature's (§4.3)
        Assert.Equal(new[] { "Smoke" }, feature.Scenarios.Single(s => s.DisplayName == "Tagged").Tags);
        Assert.Null(feature.Scenarios.Single(s => s.DisplayName == "Untagged").Tags);
    }

    [Fact]
    public void The_attribute_gives_the_feature_the_class_s_category_and_the_scenario_the_method_s()
    {
        var tagged = ThroughTheAttribute(typeof(CategorizedSinkTests), nameof(CategorizedSinkTests.Tagged));
        var untagged = ThroughTheAttribute(typeof(CategorizedSinkTests), nameof(CategorizedSinkTests.Untagged));

        var feature = new[] { tagged, untagged }.ToFeatures().Single();

        Assert.Equal(new[] { "Slow" }, feature.Tags);
        Assert.Equal(new[] { "Smoke" }, feature.Scenarios.Single(s => s.Id == tagged.Id).Tags);
        Assert.Null(feature.Scenarios.Single(s => s.Id == untagged.Id).Tags);
    }

    [Fact]
    public void A_base_class_s_category_is_its_derived_classes_feature_tag_on_both_paths()
    {
        var fromSink = ThroughTheSink(Test<DerivedCategorizedSinkTests>(nameof(DerivedCategorizedSinkTests.Inherits))).ToFeatures().Single();
        var fromAttribute = new[] { ThroughTheAttribute(typeof(DerivedCategorizedSinkTests), nameof(DerivedCategorizedSinkTests.Inherits)) }
            .ToFeatures().Single();

        foreach (var feature in new[] { fromSink, fromAttribute })
        {
            Assert.Equal(new[] { "Own", "Inherited" }, feature.Tags);
            Assert.Equal(new[] { "Smoke" }, feature.Scenarios.Single().Tags);
        }
    }

    // Both classes are the feature Orders. Until 5.2.7 it took the categories of whichever test finished first, so the
    // other class's tests carried them and their own were nowhere; the one both classes have stays the feature's.
    private static void AssertEachKeepsItsOwn(Feature feature)
    {
        Assert.Equal(new[] { "Shop" }, feature.Tags);
        Assert.Equal(new[] { "Web" }, feature.Scenarios.Single(s => s.DisplayName == "Pay").Tags);
        Assert.Equal(new[] { "Api" }, feature.Scenarios.Single(s => s.DisplayName == "Refund").Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Through_the_sink_two_classes_of_one_name_keep_their_own_categories(bool apiFirst)
    {
        var web = Test<Web.Orders>(nameof(Web.Orders.Pay));
        var api = Test<Api.Orders>(nameof(Api.Orders.Refund));

        AssertEachKeepsItsOwn((apiFirst ? ThroughTheSink(api, web) : ThroughTheSink(web, api)).ToFeatures().Single());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Through_the_attribute_two_classes_of_one_name_keep_their_own_categories(bool apiFirst)
    {
        var web = ThroughTheAttribute(typeof(Web.Orders), nameof(Web.Orders.Pay));
        var api = ThroughTheAttribute(typeof(Api.Orders), nameof(Api.Orders.Refund));

        AssertEachKeepsItsOwn((apiFirst ? new[] { api, web } : [web, api]).ToFeatures().Single());
    }

    [Fact]
    public void A_happy_path_category_marks_the_happy_path_and_is_not_a_tag()
    {
        var scenario = ThroughTheSink(Test<CategorizedSinkTests>(nameof(CategorizedSinkTests.Happy))).ToFeatures().Single().Scenarios.Single();

        Assert.True(scenario.IsHappyPath);
        Assert.Null(scenario.Tags);
    }
}
