using System.Reflection;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

[Collection("DiagramsFetcher")]
public class ReportToggleTests : IDisposable
{
    private static readonly FieldInfo DiagramsField =
        typeof(DefaultDiagramsFetcher).GetField("_diagrams", BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly string _reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public ReportToggleTests()
    {
        DiagramsField.SetValue(null, null);
    }

    public void Dispose()
    {
        DiagramsField.SetValue(null, null);
        // Clean up files created by this test instance
        foreach (var file in Directory.GetFiles(_reportsDir, $"*{_suffix}*"))
            try { File.Delete(file); } catch { /* best-effort */ }
    }

    private ReportConfigurationOptions MakeOptions(Action<ReportConfigurationOptions>? configure = null)
    {
        var options = new ReportConfigurationOptions
        {
            HtmlSpecificationsFileName = $"Specifications_{_suffix}",
            HtmlTestRunReportFileName = $"TestRunReport_{_suffix}",
            YamlSpecificationsFileName = $"Specifications_{_suffix}"
        };
        configure?.Invoke(options);
        return options;
    }

    private static Feature[] SimpleFeatures =>
    [
        new()
        {
            DisplayName = "Toggle Test Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "1",
                    DisplayName = "Toggle scenario",
                    Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromMilliseconds(100)
                }
            ]
        }
    ];

    // --- Specifications HTML ---

    [Fact]
    public void SpecificationsReport_generated_by_default()
    {
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.html")));
    }

    [Fact]
    public void SpecificationsReport_skipped_when_disabled()
    {
        var options = MakeOptions(o => o.GenerateSpecificationsReport = false);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.False(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.html")));
    }

    // --- Test Run Report HTML ---

    [Fact]
    public void TestRunReport_generated_by_default()
    {
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    [Fact]
    public void TestRunReport_skipped_when_disabled()
    {
        var options = MakeOptions(o => o.GenerateTestRunReport = false);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.False(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    // --- Specifications Data ---

    [Fact]
    public void SpecificationsData_generated_by_default()
    {
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.yml")));
    }

    [Fact]
    public void SpecificationsData_skipped_when_disabled()
    {
        var options = MakeOptions(o => o.GenerateSpecificationsData = false);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.False(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.yml")));
    }

    // --- Test Run Report Data ---

    [Fact]
    public void TestRunReportData_generated_by_default()
    {
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.json")));
    }

    [Fact]
    public void TestRunReportData_skipped_when_disabled()
    {
        var options = MakeOptions(o => o.GenerateTestRunReportData = false);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.False(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.json")));
    }

    // --- Test Run Report Schema ---

    [Fact]
    public void TestRunReportSchema_generated_by_default()
    {
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.schema.json")));
    }

    [Fact]
    public void TestRunReportSchema_skipped_when_disabled()
    {
        var options = MakeOptions(o => o.GenerateTestRunReportSchema = false);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.False(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.schema.json")));
    }

    // --- Specifications blank on failed tests ---

    private static Feature[] FailingFeatures =>
    [
        new()
        {
            DisplayName = "Failing Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "f1",
                    DisplayName = "Failing scenario",
                    Result = ExecutionResult.Failed,
                    ErrorMessage = "assertion failed",
                    Duration = TimeSpan.FromMilliseconds(50)
                }
            ]
        }
    ];

    [Fact]
    public void SpecificationsReport_blank_when_tests_fail()
    {
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            FailingFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        var path = Path.Combine(_reportsDir, $"Specifications_{_suffix}.html");
        Assert.True(File.Exists(path));
        Assert.Equal(string.Empty, File.ReadAllText(path));
    }

    [Fact]
    public void SpecificationsData_blank_when_tests_fail()
    {
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            FailingFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        var path = Path.Combine(_reportsDir, $"Specifications_{_suffix}.yml");
        Assert.True(File.Exists(path));
        Assert.Equal(string.Empty, File.ReadAllText(path));
    }

    [Fact]
    public void TestRunReport_not_blank_when_tests_fail()
    {
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            FailingFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        var path = Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html");
        Assert.True(File.Exists(path));
        Assert.NotEqual(string.Empty, File.ReadAllText(path));
    }

    [Fact]
    public void SpecificationsReport_not_blank_when_only_skipped_and_bypassed()
    {
        var features = new Feature[]
        {
            new()
            {
                DisplayName = "Non-failure Feature",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Skipped", Result = ExecutionResult.Skipped, Duration = TimeSpan.FromMilliseconds(10) },
                    new Scenario { Id = "b1", DisplayName = "Bypassed", Result = ExecutionResult.Bypassed, Duration = TimeSpan.FromMilliseconds(10) },
                    new Scenario { Id = "p1", DisplayName = "Passed", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }
                ]
            }
        };
        var options = MakeOptions();

        ReportGenerator.CreateStandardReportsWithDiagrams(
            features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        var path = Path.Combine(_reportsDir, $"Specifications_{_suffix}.html");
        Assert.True(File.Exists(path));
        Assert.NotEqual(string.Empty, File.ReadAllText(path));
    }

    // --- ExpectedTestCount (partial-run guard) ---

    [Fact]
    public void Specifications_skipped_when_scenario_count_below_expected()
    {
        var options = MakeOptions(o => o.ExpectedTestCount = () => 5);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.False(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.html")));
        Assert.False(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.yml")));
    }

    [Fact]
    public void TestRunReport_still_generated_when_scenario_count_below_expected()
    {
        var options = MakeOptions(o => o.ExpectedTestCount = () => 5);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.json")));
    }

    [Fact]
    public void Reports_generated_when_scenario_count_equals_expected()
    {
        var options = MakeOptions(o => o.ExpectedTestCount = () => 1);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.html")));
        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    [Fact]
    public void Reports_generated_when_scenario_count_exceeds_expected()
    {
        var features = new Feature[]
        {
            new()
            {
                DisplayName = "Multi Feature",
                Scenarios =
                [
                    new Scenario { Id = "1", DisplayName = "S1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(50) },
                    new Scenario { Id = "2", DisplayName = "S2", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(50) }
                ]
            }
        };
        var options = MakeOptions(o => o.ExpectedTestCount = () => 1);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.html")));
        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    [Fact]
    public void Reports_generated_when_expected_count_is_null()
    {
        var options = MakeOptions(o => o.ExpectedTestCount = null);

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.html")));
        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    // --- Zero-scenario guard (discovery pass / empty run) ---

    [Fact]
    public void No_reports_generated_when_features_array_is_empty()
    {
        var options = MakeOptions();
        // Seed a pre-existing report to prove it is NOT overwritten
        Directory.CreateDirectory(_reportsDir);
        var preExisting = Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html");
        File.WriteAllText(preExisting, "<html>previous</html>");

        ReportGenerator.CreateStandardReportsWithDiagrams(
            [], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        // Pre-existing report must still contain the original content
        Assert.Equal("<html>previous</html>", File.ReadAllText(preExisting));
    }

    [Fact]
    public void No_reports_generated_when_all_features_have_zero_scenarios()
    {
        var emptyFeatures = new Feature[]
        {
            new() { DisplayName = "Empty Feature 1" },
            new() { DisplayName = "Empty Feature 2" }
        };
        var options = MakeOptions();
        Directory.CreateDirectory(_reportsDir);
        var preExisting = Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html");
        File.WriteAllText(preExisting, "<html>previous</html>");

        ReportGenerator.CreateStandardReportsWithDiagrams(
            emptyFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        Assert.Equal("<html>previous</html>", File.ReadAllText(preExisting));
    }

    [Fact]
    public void Reports_still_generated_when_features_have_scenarios()
    {
        var options = MakeOptions();
        Directory.CreateDirectory(_reportsDir);
        var preExisting = Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html");
        File.WriteAllText(preExisting, "<html>previous</html>");

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        // Report should have been overwritten with real content
        var content = File.ReadAllText(preExisting);
        Assert.NotEqual("<html>previous</html>", content);
        Assert.Contains("Toggle scenario", content);
    }

    [Fact]
    public void Toggle_defaults_flow_through_the_standard_pipeline_with_specifications_override()
    {
        // One options record → both HTML files via the full pipeline: the Specifications
        // override diverges only Specifications.html; the TestRunReport group applies to both.
        var options = MakeOptions(o =>
        {
            o.TestRunReportToggleDefaults.HeadersShown = false;
            o.SpecificationsToggleDefaults.Details = ReportDetailsState.Expanded;
        });

        ReportGenerator.CreateStandardReportsWithDiagrams(
            SimpleFeatures, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);

        var testRun = File.ReadAllText(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html"));
        Assert.Contains("window._detailsDefault = 'truncated'", testRun);
        Assert.Contains("window._headersHidden = true", testRun);
        Assert.Contains(">Headers Hidden</button>", testRun);

        var specs = File.ReadAllText(Path.Combine(_reportsDir, $"Specifications_{_suffix}.html"));
        Assert.Contains("window._detailsDefault = 'expanded'", specs);
        Assert.Contains("window._headersHidden = true", specs);   // inherited
        Assert.Contains(">Headers Hidden</button>", specs);
    }
}
