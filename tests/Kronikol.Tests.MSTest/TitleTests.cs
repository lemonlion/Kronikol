using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kronikol.MSTest;
using Kronikol.Reports;

namespace Kronikol.Tests.MSTest;

/// <summary>
/// What the MSTest lane names a test (plans/TITLES_AS_WRITTEN_PLAN.md 4.1, 4.2, 4.4): the method's name by the rule, words
/// in capitals kept; a DisplayName as written, a data row under a <c>[TestMethod(DisplayName = …)]</c> with its values;
/// and the id 5.16.4 gave it, which the data file computes from the recorded name.
/// </summary>
[TestClass]
public class TitleTests
{
    private static MSTestScenarioInfo Info(string method, string? displayName = null, string? methodDisplayName = null,
        string?[]? parameterNames = null) => new()
    {
        TestClassSimpleName = "OrderTests",
        TestMethodName = method,
        TestDisplayName = displayName,
        MethodDisplayName = methodDisplayName,
        TestId = $"OrderTests.{method}.{displayName}",
        Outcome = UnitTestOutcome.Passed,
        ParameterNames = parameterNames,
    };

    /// <summary>The scenario as the data file writes it: its name, its stableId and the name the id keys on, if another.</summary>
    private static (string Name, string StableId, string? StableIdName) Written(Feature feature)
    {
        var path = Path.Combine(Path.GetTempPath(), "kronikol-mstest-titles-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var at = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
            var written = ReportGenerator.GenerateTestRunReportData([feature], at, at.AddSeconds(1), path, DataFormat.Json);
            using var document = JsonDocument.Parse(File.ReadAllText(written));
            var scenario = document.RootElement.GetProperty("features")[0].GetProperty("scenarios")[0];
            return (scenario.GetProperty("name").GetString()!, scenario.GetProperty("stableId").GetString()!,
                scenario.TryGetProperty("stableIdName", out var key) ? key.GetString() : null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void A_method_s_name_reads_with_its_capitals_and_keeps_its_id()
    {
        var feature = new[] { Info("Gets_the_UK_VAT_rate") }.ToFeatures().Single();

        var (name, stableId, stableIdName) = Written(feature);
        Assert.AreEqual("Gets the UK VAT rate", name);
        Assert.AreEqual("Gets the uk vat rate", stableIdName);
        Assert.AreEqual(ScenarioStableId.Compute(null, "Order Tests", "Gets the uk vat rate"), stableId);
    }

    [TestMethod]
    public void A_display_name_is_shown_as_written_and_keeps_its_id()
    {
        var feature = new[] { Info("SendsOrders", displayName: "Orders are sent to Kafka (v1.2)") }.ToFeatures().Single();

        var scenario = feature.Scenarios.Single();
        Assert.AreEqual("Orders are sent to Kafka (v1.2)", scenario.DisplayName);
        Assert.IsNull(scenario.ExampleValues);
        // 5.16.4 re-bracketed it as a method's arguments, "Orders are sent to kafka [v1.2]", and hashed that row
        var (_, stableId, stableIdName) = Written(feature);
        Assert.AreEqual("Orders are sent to kafka [v1.2]", stableIdName);
        Assert.AreEqual(ScenarioStableId.Compute(null, "Order Tests", "Orders are sent to kafka [v1.2]", "Orders are sent to kafka",
            new Dictionary<string, string> { ["arg0"] = "v1.2" }), stableId);
    }

    [TestMethod]
    public void A_data_row_under_a_written_display_name_keeps_it_and_its_values()
    {
        var feature = new[]
        {
            Info("AddsVat", displayName: "Adds VAT (1,2)", methodDisplayName: "Adds VAT", parameterNames: ["net", "rate"]),
            Info("AddsVat", displayName: "Adds VAT (3,4)", methodDisplayName: "Adds VAT", parameterNames: ["net", "rate"]),
        }.ToFeatures().Single();

        var row = feature.Scenarios.First();
        Assert.AreEqual("Adds VAT [1,2]", row.DisplayName);
        Assert.AreEqual("1", row.ExampleValues!["net"]);
        Assert.AreEqual("Adds VAT", ParameterGrouper.Analyze(feature.Scenarios).Groups.Single().GroupDisplayName);
        var (_, stableId, stableIdName) = Written(new Feature { DisplayName = feature.DisplayName, Scenarios = [row] });
        Assert.AreEqual("Adds vat [1,2]", stableIdName);
        Assert.AreEqual(ScenarioStableId.Compute(null, "Order Tests", "Adds vat [1,2]", "Adds vat",
            new Dictionary<string, string> { ["net"] = "1", ["rate"] = "2" }), stableId);
    }

    [TestMethod]
    public void A_default_data_row_name_is_the_method_s_with_its_values()
    {
        var scenario = new[] { Info("AddsVat", displayName: "AddsVat (1,2)", parameterNames: ["net", "rate"]) }.ToFeatures().Single().Scenarios.Single();

        Assert.AreEqual("Adds vat [1,2]", scenario.DisplayName);
    }

    [TestMethod]
    public void The_word_list_spells_the_names_the_lane_made_and_leaves_a_display_name()
    {
        // plans/TITLES_AS_WRITTEN_PLAN.md 4.5: the class's and the method's names are the lane's, a DisplayName the test's
        var features = new[] { Info("PaysViaAcmePay"), Info("PaysAsWritten", displayName: "Pays via acme pay, as written") }.ToFeatures();
        var ids = features[0].Scenarios.Select(s => ScenarioStableId.For(null, features[0], s)).ToArray();

        new Kronikol.Naming.TitleWords(["AcmePay", "OrderTests"]).ApplyTo(features);

        Assert.AreEqual("OrderTests", features[0].DisplayName);
        CollectionAssert.AreEquivalent(new[] { "Pays via AcmePay", "Pays via acme pay, as written" }, features[0].Scenarios.Select(s => s.DisplayName).ToArray());
        CollectionAssert.AreEqual(ids, features[0].Scenarios.Select(s => ScenarioStableId.For(null, features[0], s)).ToArray());
    }

    [TestMethod]
    public void A_data_row_s_outline_title_is_the_lane_s_only_without_a_method_display_name()
    {
        var own = new[] { Info("AddsVat", displayName: "AddsVat (1,2)", parameterNames: ["net", "rate"]) }.ToFeatures().Single().Scenarios.Single();
        var written = new[] { Info("AddsVat", displayName: "Adds VAT (1,2)", methodDisplayName: "Adds VAT", parameterNames: ["net", "rate"]) }
            .ToFeatures().Single().Scenarios.Single();

        Assert.IsTrue(own.OutlineTitleGenerated);
        Assert.IsFalse(written.OutlineTitleGenerated);
    }
}
