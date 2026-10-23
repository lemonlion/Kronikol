using System.Reflection;
using LightBDD.Core.Formatting.NameDecorators;
using LightBDD.Core.Metadata;
using LightBDD.Core.Results;
using Kronikol.LightBDD;
using Kronikol.Reports;

namespace Kronikol.Tests.LightBDD;

[Collection("DiagramsFetcher")]
public class StandardPipelineFormatterTests : IDisposable
{
    private static readonly FieldInfo DiagramsField =
        typeof(DefaultDiagramsFetcher).GetField("_diagrams", BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly string _reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public StandardPipelineFormatterTests()
    {
        DiagramsField.SetValue(null, null);
    }

    public void Dispose()
    {
        DiagramsField.SetValue(null, null);
        foreach (var file in Directory.GetFiles(_reportsDir, $"*{_suffix}*"))
            try { File.Delete(file); } catch { /* best effort */ }
    }

    [Fact]
    public void Default_Options_is_new_ReportConfigurationOptions()
    {
        var formatter = new StandardPipelineFormatter();
        Assert.NotNull(formatter.Options);
    }

    [Fact]
    public void Default_ExpectedTestCount_on_options_is_null()
    {
        var formatter = new StandardPipelineFormatter();
        Assert.Null(formatter.Options.ExpectedTestCount);
    }

    [Fact]
    public void Format_skips_specifications_when_scenario_count_below_expected()
    {
        var formatter = new StandardPipelineFormatter
        {
            Options = new ReportConfigurationOptions
            {
                ExpectedTestCount = () => 5,
                HtmlTestRunReportFileName = $"TestRunReport_{_suffix}",
                HtmlSpecificationsFileName = $"Specifications_{_suffix}",
                YamlSpecificationsFileName = $"Specifications_{_suffix}"
            }
        };

        var feature = new StubFeatureResult("F1")
            .WithScenario(new StubScenarioResult("s1", "Scenario 1", duration: TimeSpan.FromMilliseconds(100)))
            .WithScenario(new StubScenarioResult("s2", "Scenario 2", duration: TimeSpan.FromMilliseconds(100)));

        formatter.Format(Stream.Null, feature);

        Assert.False(File.Exists(Path.Combine(_reportsDir, $"Specifications_{_suffix}.html")));
        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    [Fact]
    public void Format_proceeds_when_scenario_count_equals_expected()
    {
        var formatter = new StandardPipelineFormatter
        {
            Options = new ReportConfigurationOptions
            {
                ExpectedTestCount = () => 2,
                HtmlTestRunReportFileName = $"TestRunReport_{_suffix}",
                HtmlSpecificationsFileName = $"Specifications_{_suffix}",
                YamlSpecificationsFileName = $"Specifications_{_suffix}"
            }
        };

        var feature = new StubFeatureResult("F1")
            .WithScenario(new StubScenarioResult("s1", "Scenario 1", duration: TimeSpan.FromMilliseconds(100)))
            .WithScenario(new StubScenarioResult("s2", "Scenario 2", duration: TimeSpan.FromMilliseconds(100)));

        formatter.Format(Stream.Null, feature);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    [Fact]
    public void Format_proceeds_when_scenario_count_exceeds_expected()
    {
        var formatter = new StandardPipelineFormatter
        {
            Options = new ReportConfigurationOptions
            {
                ExpectedTestCount = () => 1,
                HtmlTestRunReportFileName = $"TestRunReport_{_suffix}",
                HtmlSpecificationsFileName = $"Specifications_{_suffix}",
                YamlSpecificationsFileName = $"Specifications_{_suffix}"
            }
        };

        var feature = new StubFeatureResult("F1")
            .WithScenario(new StubScenarioResult("s1", "Scenario 1", duration: TimeSpan.FromMilliseconds(100)))
            .WithScenario(new StubScenarioResult("s2", "Scenario 2", duration: TimeSpan.FromMilliseconds(100)));

        formatter.Format(Stream.Null, feature);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    [Fact]
    public void Format_proceeds_when_expected_count_is_null()
    {
        var formatter = new StandardPipelineFormatter
        {
            Options = new ReportConfigurationOptions
            {
                ExpectedTestCount = null,
                HtmlTestRunReportFileName = $"TestRunReport_{_suffix}",
                HtmlSpecificationsFileName = $"Specifications_{_suffix}",
                YamlSpecificationsFileName = $"Specifications_{_suffix}"
            }
        };

        var feature = new StubFeatureResult("F1")
            .WithScenario(new StubScenarioResult("s1", "Scenario 1", duration: TimeSpan.FromMilliseconds(100)));

        formatter.Format(Stream.Null, feature);

        Assert.True(File.Exists(Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html")));
    }

    [Fact]
    public void Format_does_not_overwrite_existing_report_when_zero_features()
    {
        var formatter = new StandardPipelineFormatter
        {
            Options = new ReportConfigurationOptions
            {
                HtmlTestRunReportFileName = $"TestRunReport_{_suffix}",
                HtmlSpecificationsFileName = $"Specifications_{_suffix}",
                YamlSpecificationsFileName = $"Specifications_{_suffix}"
            }
        };

        // Seed a pre-existing report
        Directory.CreateDirectory(_reportsDir);
        var preExisting = Path.Combine(_reportsDir, $"TestRunReport_{_suffix}.html");
        File.WriteAllText(preExisting, "<html>previous</html>");

        // Call with zero features (simulates xUnit3 discovery pass)
        formatter.Format(Stream.Null);

        Assert.Equal("<html>previous</html>", ReportHtml.Read(preExisting));
    }

    // ── Stubs ──

    private class StubFeatureResult : IFeatureResult
    {
        private readonly List<IScenarioResult> _scenarios = [];

        public StubFeatureResult(string name) => Info = new StubFeatureInfo(name);
        public IFeatureInfo Info { get; }
        public IEnumerable<IScenarioResult> GetScenarios() => _scenarios;

        public StubFeatureResult WithScenario(StubScenarioResult scenario)
        {
            _scenarios.Add(scenario);
            return this;
        }
    }

    private class StubScenarioResult : IScenarioResult
    {
        public StubScenarioResult(string id, string name,
            ExecutionStatus status = ExecutionStatus.Passed,
            TimeSpan? duration = null)
        {
            Info = new StubScenarioInfo(name);
            Status = status;
            ExecutionTime = duration.HasValue
                ? new ExecutionTime(DateTimeOffset.UtcNow, duration.Value)
                : null;
        }

        public IScenarioInfo Info { get; }
        public ExecutionStatus Status { get; }
        public string? StatusDetails => null;
        public ExecutionTime? ExecutionTime { get; }
        public IEnumerable<IStepResult> GetSteps() => [];
    }

    private class StubFeatureInfo : IFeatureInfo
    {
        public StubFeatureInfo(string name)
        {
            Name = new StubNameInfo(name);
            RuntimeId = Guid.NewGuid();
        }

        public INameInfo Name { get; }
        public Guid RuntimeId { get; }
        public string? Description => null;
        public IEnumerable<string> Labels => [];
    }

    private class StubScenarioInfo : IScenarioInfo
    {
        public StubScenarioInfo(string name)
        {
            Name = new StubNameInfo(name);
            RuntimeId = Guid.NewGuid();
        }

        public INameInfo Name { get; }
        public Guid RuntimeId { get; }
        public IFeatureInfo Parent => null!;
        public IEnumerable<string> Labels => [];
        public IEnumerable<string> Categories => [];
        public string? Description => null;
    }

    private class StubNameInfo : INameInfo
    {
        private readonly string _name;
        public StubNameInfo(string name) => _name = name;
        public string NameFormat => _name;
        public IEnumerable<INameParameterInfo> Parameters => [];
        public override string ToString() => _name;
        public string Format(INameDecorator decorator) => decorator.DecorateNameFormat(_name);
    }
}
