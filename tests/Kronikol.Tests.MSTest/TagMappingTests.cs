using Kronikol.MSTest;
using Kronikol.Reports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Every test in this project carries it; nothing filters on a category here. It is the assembly level of the facts below.
[assembly: TestCategory("Assembly level")]

namespace Kronikol.Tests.MSTest;

/// <summary>
/// Which of a test's <c>[TestCategory]</c> values are its feature's and which its own (plans/TAGS_PLAN.md §4.7, §5.2): the
/// class's and the assembly's are the feature's, the method's are the scenario's. 4.x read none of them. The capture facts
/// call the cleanup MSTest calls after each test, so they read what the cleanup records for the running test itself.
/// </summary>
[TestClass]
[TestCategory("Slow")]
public class TagMappingTests : DiagrammedComponentTest
{
    /// <summary>A category attribute of the project's own, as MSTest lets a suite define them.</summary>
    private sealed class NightlyAttribute : TestCategoryBaseAttribute
    {
        public override IList<string> TestCategories => ["Nightly"];
    }

    private MSTestScenarioInfo Captured()
    {
        TestTrackingCleanup();
        var id = DiagrammedComponentTest.GetCurrentTestId();
        return DiagrammedTestRun.TestContexts.Last(info => info.TestId == id);
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public void The_class_s_and_the_assembly_s_categories_are_the_feature_s_and_the_method_s_are_the_scenario_s()
    {
        var info = Captured();

        CollectionAssert.AreEqual(new[] { "Slow", "Assembly level" }, info.FeatureTags);
        CollectionAssert.AreEqual(new[] { "Smoke" }, info.Tags);
    }

    [TestMethod]
    [Nightly]
    public void A_category_attribute_of_the_suite_s_own_is_read_like_TestCategory()
    {
        CollectionAssert.AreEqual(new[] { "Nightly" }, Captured().Tags);
    }

    [TestMethod]
    public void A_method_with_no_category_records_none()
    {
        Assert.IsNull(Captured().Tags);
    }

    [TestMethod]
    [TestCategory("happy-path")]
    public void A_happy_path_category_marks_the_happy_path_and_is_not_a_tag()
    {
        var scenario = new[] { Captured() }.ToFeatures().Single().Scenarios.Single();

        Assert.IsTrue(scenario.IsHappyPath);
        Assert.IsNull(scenario.Tags);
    }

    [TestMethod]
    public void The_report_gives_the_feature_its_tags_and_each_scenario_its_own_through_the_classifier()
    {
        MSTestScenarioInfo Info(string method, string[]? tags) => new()
        {
            TestClassSimpleName = "Orders", TestMethodName = method, TestId = "Orders." + method, Outcome = UnitTestOutcome.Passed,
            FeatureTags = ["category:Slow", "slow", "Nightly"], Tags = tags
        };

        // Pay's SLOW repeats the class's Slow in another case, so it is the feature's (§4.3)
        var feature = new[] { Info("Pay", ["Smoke", "endpoint:/orders", "@smoke", "SLOW"]), Info("Refuse", null) }.ToFeatures().Single();

        CollectionAssert.AreEqual(new[] { "Slow", "Nightly" }, feature.Tags);
        CollectionAssert.AreEqual(new[] { "Smoke" }, feature.Scenarios.Single(s => s.Id == "Orders.Pay").Tags);
        Assert.IsNull(feature.Scenarios.Single(s => s.Id == "Orders.Refuse").Tags);
    }
}
