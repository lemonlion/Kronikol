using System.Reflection;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.xUnit3;
using Xunit.Sdk;

namespace Kronikol.Tests.xUnit3;

/// <summary>
/// What the xUnit v3 lane names a test, read from its own running context as the report reads it after the test
/// (plans/TITLES_AS_WRITTEN_PLAN.md 4.1, 4.2, 4.4): xUnit's default name by the rule, words in capitals kept; a name the
/// test wrote as written; and the id 5.16.4 gave it, which the data file computes from the recorded name.
/// </summary>
public class TitleTests : DiagrammedComponentTest
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

    /// <summary>The scenario as the data file writes it: its name, its stableId and the name the id keys on, if another.</summary>
    private static (string Name, string StableId, string? StableIdName) Written(Feature feature)
    {
        var path = Path.Combine(Path.GetTempPath(), "kronikol-xunit3-titles-" + Guid.NewGuid().ToString("N") + ".json");
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

    [Fact]
    public void Gets_the_UK_VAT_rate()
    {
        var feature = Mapped();

        var (name, stableId, stableIdName) = Written(feature);
        Assert.Equal("Gets the UK VAT rate", name);
        // 5.16.4 showed and hashed "Gets the uk vat rate"; the id keeps to it
        Assert.Equal("Gets the uk vat rate", stableIdName);
        Assert.Equal(ScenarioStableId.Compute(RunSuite.Current, feature.DisplayName, "Gets the uk vat rate"), stableId);
    }

    [Fact(DisplayName = "Orders are sent to Kafka and stored in MongoDB (v1.2)")]
    public void A_display_name_is_shown_as_written()
    {
        var feature = Mapped();

        var (name, stableId, stableIdName) = Written(feature);
        Assert.Equal("Orders are sent to Kafka and stored in MongoDB (v1.2)", name);
        Assert.Equal("Orders are sent to kafka and stored in mongo db (v1.2)", stableIdName);
        // 5.16.4 also read the closing parenthesis as a row's values; the id keeps to that too
        Assert.Equal(ScenarioStableId.Compute(RunSuite.Current, feature.DisplayName, "Orders are sent to kafka and stored in mongo db (v1.2)",
            "Orders are sent to kafka and stored in mongo db", new Dictionary<string, string> { ["arg0"] = "v1.2" }), stableId);
        Assert.Null(feature.Scenarios.Single().ExampleValues);
    }

    [Theory]
    [InlineData("GB", TestDisplayName = "Order v1.2 ships")]
    public void A_row_s_own_name_is_shown_as_written(string country)
    {
        Assert.Equal("GB", country);
        var scenario = Mapped().Scenarios.Single();

        // B1: 5.16.4 read the row's name as a method path and kept what followed its last dot, "2 ships"
        Assert.Equal("Order v1.2 ships [country: \"GB\"]", scenario.DisplayName);
    }

    [Theory]
    [InlineData("GB", Label = "U.K.")]
    public void A_row_s_label_follows_the_method_s_title(string country)
    {
        Assert.Equal("GB", country);
        var scenario = Mapped().Scenarios.Single();

        // B1: 5.16.4 cut the label at its dot and lower-cased it
        Assert.Equal("A row s label follows the method s title [U.K.]", scenario.DisplayName);
    }

    [Theory]
    [InlineData("x", TestDisplayName = "A lone surrogate's code is rejected")]
    public void Rejects_UD800(string s)
    {
        Assert.Equal("x", s);

        // xUnit's formatter throws on this method's name under UseEscapeSequences, a reading the lane tries: it still
        // names the row, where it threw and lost the report
        Assert.Equal("A lone surrogate's code is rejected [s: \"x\"]", Mapped().Scenarios.Single().DisplayName);
    }

    // the names the lane made, which the word list spells as the suite does (plans/TITLES_AS_WRITTEN_PLAN.md 4.5)
    private static Feature Listed(params string[] words)
    {
        var feature = Mapped();
        new Kronikol.Naming.TitleWords(words).ApplyTo([feature]);
        return feature;
    }

    [Fact]
    public void Pays_via_Acme_pay()
    {
        var feature = Listed("AcmePay", "TitleTests");

        Assert.Equal("TitleTests", feature.DisplayName);
        Assert.Equal("Pays via AcmePay", feature.Scenarios.Single().DisplayName);
    }

    [Fact(DisplayName = "Pays via acme pay, as written")]
    public void A_display_name_is_not_rewritten_by_the_word_list()
    {
        var feature = Listed("AcmePay");

        Assert.Equal("Pays via acme pay, as written", feature.Scenarios.Single().DisplayName);
        Assert.True(feature.NameGenerated);
        Assert.False(feature.Scenarios.Single().NameGenerated);
    }

    [Theory]
    [InlineData("uk")]
    public void Adds_vat_for(string country)
    {
        var scenario = Listed("VAT", "UK").Scenarios.Single();

        // the method's words, never the row's value
        Assert.Equal("Adds VAT for [country: \"uk\"]", scenario.DisplayName);
        Assert.True(scenario.OutlineTitleGenerated);
        Assert.Equal("uk", country);
    }
}
