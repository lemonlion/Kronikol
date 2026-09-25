using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// An option the run's rendering mode ignores says so (DIAGRAM_COLOURS_PLAN S3b). <c>BrowserJs</c> (the default)
/// and <c>NodeJs</c> load the engine without its theme bundle, so a <c>PlantUmlTheme</c> set under either draws
/// every diagram unthemed. Before 3.30.0 nothing said so; the run now records one
/// <see cref="DiagnosticKind.OptionNotApplied"/> per ignored option.
/// </summary>
[Collection("DiagramsFetcher")]
public class OptionDiagnosticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-optdiag-" + Guid.NewGuid().ToString("N"));

    public OptionDiagnosticsTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private List<DiagnosticEntry> Run(Action<ReportConfigurationOptions> configure, Feature[]? features = null)
    {
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = _dir,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        };
        configure(options);
        features ??= [new Feature { DisplayName = "Orders", Scenarios = [new Scenario { Id = "optdiag-" + Guid.NewGuid().ToString("N"), DisplayName = "Pay", Result = ExecutionResult.Passed }] }];

        var collector = new ReportDiagnosticsCollector();
        using (ReportDiagnosticsScope.Begin(collector))
            ReportGenerator.CreateStandardReportsWithDiagrams(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
        return collector.Entries.Where(e => e.Kind == DiagnosticKind.OptionNotApplied).ToList();
    }

    [Theory]
    [InlineData(PlantUmlRendering.BrowserJs)]
    [InlineData(PlantUmlRendering.NodeJs)]
    public void A_theme_under_a_mode_that_loads_no_theme_bundle_is_recorded_once(PlantUmlRendering mode)
    {
        var entry = Assert.Single(Run(o => { o.PlantUmlTheme = "cerulean"; o.PlantUmlRendering = mode; }));

        Assert.Contains("PlantUmlTheme \"cerulean\"", entry.Message);
        Assert.Contains($"PlantUmlRendering.{mode}", entry.Message);
        Assert.Contains("unthemed", entry.Message);
        Assert.Null(entry.ScenarioId);
    }

    [Theory]
    [InlineData(PlantUmlRendering.Server)]
    [InlineData(PlantUmlRendering.Local)]
    public void A_theme_under_a_mode_that_applies_it_is_not_recorded(PlantUmlRendering mode)
    {
        Assert.Empty(Run(o =>
        {
            o.PlantUmlTheme = "cerulean";
            o.PlantUmlRendering = mode;
            if (mode != PlantUmlRendering.Local) return;
            o.PlantUmlImageFormat = PlantUmlImageFormat.Base64Svg;
            o.LocalDiagramRenderer = (_, _) => "<svg/>"u8.ToArray();
        }));
    }

    [Fact]
    public void A_run_with_no_scenarios_records_nothing()
    {
        // A discovery pass, or a filter that matched nothing, writes no report: it must not warn either.
        Assert.Empty(Run(o => o.PlantUmlTheme = "cerulean", features: []));
    }

    [Fact]
    public void The_run_prints_the_entry_once_as_a_warning()
    {
        // The line names no directory, so it is read from this thread's own output (ThreadScopedConsole).
        string console;
        using (var scoped = new ThreadScopedConsole())
        {
            Run(o => o.PlantUmlTheme = "cerulean");
            console = scoped.Text;
        }

        var line = Assert.Single(console.Split('\n'), l => l.Contains("PlantUmlTheme \"cerulean\"", StringComparison.Ordinal));
        Assert.StartsWith("⚠ WARNING: PlantUmlTheme \"cerulean\" has no effect under PlantUmlRendering.BrowserJs", line);
    }

    [Fact]
    public void No_theme_is_not_recorded()
    {
        Assert.Empty(Run(o => o.PlantUmlRendering = PlantUmlRendering.BrowserJs));
    }

    [Fact]
    public void The_component_diagrams_theme_is_recorded_when_the_component_diagram_is_generated()
    {
        var entry = Assert.Single(Run(o =>
        {
            o.GenerateComponentDiagram = true;
            o.ComponentDiagramOptions = new global::Kronikol.ComponentDiagram.ComponentDiagramOptions { PlantUmlTheme = "superhero" };
        }));
        Assert.Contains("ComponentDiagramOptions.PlantUmlTheme \"superhero\"", entry.Message);

        Assert.Empty(Run(o => o.ComponentDiagramOptions = new global::Kronikol.ComponentDiagram.ComponentDiagramOptions { PlantUmlTheme = "superhero" }));
    }

    [Fact]
    public void Both_themes_set_record_one_entry_each()
    {
        Assert.Equal(2, Run(o =>
        {
            o.PlantUmlTheme = "cerulean";
            o.GenerateComponentDiagram = true;
            o.ComponentDiagramOptions = new global::Kronikol.ComponentDiagram.ComponentDiagramOptions { PlantUmlTheme = "superhero" };
        }).Count);
    }

    [Fact]
    public void The_entry_reaches_the_written_report_and_its_schema()
    {
        Run(o => o.PlantUmlTheme = "cerulean");

        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "TestRunReport.json")));
        var entry = Assert.Single(report.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "OptionNotApplied");
        Assert.True(!entry.TryGetProperty("scenarioId", out var id) || id.ValueKind == JsonValueKind.Null);
        Assert.Contains("\"OptionNotApplied\"", File.ReadAllText(Path.Combine(_dir, "TestRunReport.schema.json")));
    }
}
