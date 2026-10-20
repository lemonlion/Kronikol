using System.Reflection;
using Kronikol.NUnit4;
using Kronikol.Reports;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Kronikol.Tests.NUnit4;

/// <summary>
/// Which of a test's categories are its feature's and which its own (plans/TAGS_PLAN.md §4.7, §5.2): the fixture's and
/// the assembly's are the feature's, the test's and, for a <c>[TestCase]</c>, its method's are the scenario's. The trees
/// are built by hand in the shape NUnit 4.6.0 builds them (plans/TAGS_PLAN.harness/nunit-testcase-probe/results.txt): a
/// <c>[TestCase]</c> sits under a <c>ParameterizedMethodSuite</c> that holds its method's <c>[Category]</c> and
/// <c>[HappyPath]</c>, and the fixture is that suite's parent, not the case's. The attributes are applied with their own
/// <c>ApplyToTest</c>, as NUnit's builders apply them.
/// </summary>
public class TagMappingTests
{
    private sealed class Fixture : DiagrammedComponentTest
    {
        public void Plain() { }

        public void Cases(int x) { }
    }

    private static TestFixture NewFixture(params IApplyToTest[] attributes)
    {
        var fixture = new TestFixture(new TypeWrapper(typeof(Fixture)));
        foreach (var attribute in attributes)
            attribute.ApplyToTest(fixture);
        return fixture;
    }

    private static TestMethod Plain(TestSuite fixture, params IApplyToTest[] attributes)
    {
        var test = new TestMethod(new MethodWrapper(typeof(Fixture), nameof(Fixture.Plain)), fixture);
        foreach (var attribute in attributes)
            attribute.ApplyToTest(test);
        fixture.Add(test);
        return test;
    }

    private static ParameterizedMethodSuite Cases(TestSuite fixture, params IApplyToTest[] attributes)
    {
        var suite = new ParameterizedMethodSuite(new MethodWrapper(typeof(Fixture), nameof(Fixture.Cases)));
        foreach (var attribute in attributes)
            attribute.ApplyToTest(suite);
        fixture.Add(suite);
        return suite;
    }

    private static TestMethod Case(ParameterizedMethodSuite suite, int x, string? category = null)
    {
        var test = new TestMethod(new MethodWrapper(typeof(Fixture), nameof(Fixture.Cases)), suite) { Name = $"Cases({x})" };
        if (category is not null)
            test.Properties.Add(PropertyNames.Category, category); // what [TestCase(Category = …)] sets on its case
        suite.Add(test);
        return test;
    }

    private static Feature[] Features(params TestMethod[] tests) =>
        tests.Select(test =>
        {
            var result = test.MakeTestResult();
            result.SetResult(ResultState.Success);
            return new TestContext(new TestExecutionContext { CurrentTest = test, CurrentResult = result });
        }).ToFeatures();

    [Test]
    public void A_fixture_s_category_is_its_feature_s_tag_and_a_test_s_is_its_scenario_s()
    {
        var fixture = NewFixture(new CategoryAttribute("Slow"));
        var plain = Plain(fixture, new CategoryAttribute("Smoke"), new CategoryAttribute("slow"));

        var feature = Features(plain).Single();

        Assert.That(feature.Tags, Is.EqualTo(new[] { "Slow" }));
        // the test's slow repeats the fixture's Slow in another case, so it is the feature's (§4.3)
        Assert.That(feature.Scenarios.Single().Tags, Is.EqualTo(new[] { "Smoke" }));
    }

    [Test]
    public void A_test_case_carries_its_own_category_and_its_method_s()
    {
        var fixture = NewFixture(new CategoryAttribute("FixtureCat"));
        var cases = Cases(fixture, new CategoryAttribute("MethodCat"));
        var first = Case(cases, 1);
        var second = Case(cases, 2, category: "RowCat");

        var feature = Features(first, second).Single();

        Assert.That(feature.Tags, Is.EqualTo(new[] { "FixtureCat" }));
        Assert.That(feature.Scenarios.Single(s => s.Id == first.Id).Tags, Is.EqualTo(new[] { "MethodCat" }));
        // nearest first, as NUnit's own AllCategories() lists them
        Assert.That(feature.Scenarios.Single(s => s.Id == second.Id).Tags, Is.EqualTo(new[] { "RowCat", "MethodCat" }));
    }

    [Test]
    public void A_test_case_method_marked_happy_path_is_a_happy_path()
    {
        // F14: [HappyPath] on a [TestCase] method sits on its method suite, and 4.x read only the case's own properties
        var fixture = NewFixture();
        var cases = Cases(fixture, new HappyPathAttribute());
        var only = Case(cases, 1);

        var scenario = Features(only).Single().Scenarios.Single();

        Assert.That(scenario.IsHappyPath, Is.True);
        Assert.That(scenario.Tags, Is.Null);
    }

    [Test]
    public void A_fixture_whose_first_test_is_a_test_case_keeps_its_endpoint()
    {
        // F14: 4.x read the endpoint from the first test's parent, which for a [TestCase] is its method suite
        var fixture = NewFixture(new EndpointAttribute("/api/x"));
        var cases = Cases(fixture);
        var first = Case(cases, 1);
        var plain = Plain(fixture);

        var feature = Features(first, plain).Single();

        Assert.That(feature.Endpoint, Is.EqualTo("/api/x"));
        Assert.That(feature.Tags, Is.Null);
    }

    [Test]
    public void An_assembly_s_category_is_a_tag_of_every_feature_in_it()
    {
        // Q12: [assembly: Category] reaches every fixture, so it is a feature tag, after the fixture's own
        var assembly = new TestAssembly(Assembly.GetExecutingAssembly(), "Probe.dll");
        new CategoryAttribute("Nightly").ApplyToTest(assembly);
        var fixture = NewFixture(new CategoryAttribute("Slow"));
        assembly.Add(fixture);
        var plain = Plain(fixture);

        var feature = Features(plain).Single();

        Assert.That(feature.Tags, Is.EqualTo(new[] { "Slow", "Nightly" }));
        Assert.That(feature.Scenarios.Single().Tags, Is.Null);
    }

    [Test]
    public void Kronikol_s_markers_are_not_tags_and_a_happy_path_category_marks_the_happy_path()
    {
        var fixture = NewFixture(new EndpointAttribute("/orders"), new CategoryAttribute("category:Orders"));
        var marked = Plain(fixture, new HappyPathAttribute(), new CategoryAttribute("Smoke"));
        var cases = Cases(fixture, new CategoryAttribute("happy-path"));
        var named = Case(cases, 1);

        var feature = Features(marked, named).Single();

        Assert.That(feature.Tags, Is.EqualTo(new[] { "Orders" }));
        var first = feature.Scenarios.Single(s => s.Id == marked.Id);
        Assert.That(first.IsHappyPath, Is.True);
        Assert.That(first.Tags, Is.EqualTo(new[] { "Smoke" }));
        var second = feature.Scenarios.Single(s => s.Id == named.Id);
        Assert.That(second.IsHappyPath, Is.True);
        Assert.That(second.Tags, Is.Null);
    }

    [Test]
    public void A_test_with_no_category_in_a_fixture_with_none_has_no_tags()
    {
        var plain = Plain(NewFixture());

        var feature = Features(plain).Single();

        Assert.That(feature.Tags, Is.Null);
        Assert.That(feature.Scenarios.Single().Tags, Is.Null);
    }
}
