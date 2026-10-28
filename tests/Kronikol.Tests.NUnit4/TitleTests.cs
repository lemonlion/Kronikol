using System.Text.Json;
using Kronikol.NUnit4;
using Kronikol.Reports;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Kronikol.Tests.NUnit4;

/// <summary>
/// What the NUnit lane names a test (plans/TITLES_AS_WRITTEN_PLAN.md 4.1, 4.2, 4.4): the method's default name by the
/// rule, words in capitals kept, with or without the namespace and class a name template puts before it; a
/// <c>TestName</c> as written; and the id 5.16.4 gave it, which the data file computes from the recorded name.
/// </summary>
public class TitleTests
{
    private sealed class Orders : DiagrammedComponentTest
    {
        public void Gets_the_UK_VAT_rate() { }

        public void Ships(string country) { }
    }

    private static TestMethod Test(string method, string? name = null)
    {
        var fixture = new TestFixture(new TypeWrapper(typeof(Orders)));
        var test = new TestMethod(new MethodWrapper(typeof(Orders), method), fixture);
        if (name is not null)
            test.Name = name;
        fixture.Add(test);
        return test;
    }

    private static Feature Feature(TestMethod test)
    {
        var result = test.MakeTestResult();
        result.SetResult(ResultState.Success);
        return new[] { new TestContext(new TestExecutionContext { CurrentTest = test, CurrentResult = result }) }.ToFeatures().Single();
    }

    /// <summary>The scenario as the data file writes it: its name, its stableId, the name the id keys on, and its feature's key.</summary>
    private static (string Name, string StableId, string? StableIdName, string FeatureKey) Written(Feature feature)
    {
        var path = Path.Combine(Path.GetTempPath(), "kronikol-nunit-titles-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var at = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
            var written = ReportGenerator.GenerateTestRunReportData([feature], at, at.AddSeconds(1), path, DataFormat.Json);
            using var document = JsonDocument.Parse(File.ReadAllText(written));
            var featureElement = document.RootElement.GetProperty("features")[0];
            var scenario = featureElement.GetProperty("scenarios")[0];
            var featureKey = featureElement.TryGetProperty("stableIdName", out var stableFeature) ? stableFeature.GetString()! : featureElement.GetProperty("name").GetString()!;
            return (scenario.GetProperty("name").GetString()!, scenario.GetProperty("stableId").GetString()!,
                scenario.TryGetProperty("stableIdName", out var key) ? key.GetString() : null, featureKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void A_method_s_name_reads_with_its_capitals_and_keeps_its_id()
    {
        var (name, stableId, stableIdName, featureKey) = Written(Feature(Test(nameof(Orders.Gets_the_UK_VAT_rate))));

        Assert.That(name, Is.EqualTo("Gets the UK VAT rate"));
        Assert.That(stableIdName, Is.EqualTo("Gets the uk vat rate"));
        Assert.That(stableId, Is.EqualTo(ScenarioStableId.Compute(null, featureKey, "Gets the uk vat rate")));
    }

    [Test]
    public void A_name_a_template_puts_the_class_before_is_read_by_the_rule()
    {
        var scenario = Feature(Test(nameof(Orders.Ships), $"{typeof(Orders).FullName}.Ships(\"UK\")")).Scenarios.Single();

        Assert.That(scenario.DisplayName, Is.EqualTo("Ships [\"UK\"]"));
    }

    [TestCase("Orders are sent to Kafka and stored in MongoDB")]
    [TestCase("Login via PayPal (v1.2)")]
    [TestCase("Ships.To.The.UK")]
    public void A_test_name_is_shown_as_written(string testName)
    {
        var feature = Feature(Test(nameof(Orders.Ships), testName));
        var scenario = feature.Scenarios.Single();

        Assert.That(scenario.DisplayName, Is.EqualTo(testName));
        Assert.That(scenario.ExampleValues, Is.Null);
    }

    [Test]
    public void A_test_name_keeps_the_id_5_16_4_gave_it()
    {
        // 5.16.4 humanised it and read its closing parenthesis as a row's values: "Login via pay pal [v1.2]"
        var (name, stableId, stableIdName, featureKey) = Written(Feature(Test(nameof(Orders.Ships), "Login via PayPal (v1.2)")));

        Assert.That(name, Is.EqualTo("Login via PayPal (v1.2)"));
        Assert.That(stableIdName, Is.EqualTo("Login via pay pal [v1.2]"));
        Assert.That(stableId, Is.EqualTo(ScenarioStableId.Compute(null, featureKey, "Login via pay pal [v1.2]", "Login via pay pal",
            new Dictionary<string, string> { ["arg0"] = "v1.2" })));
    }
}
