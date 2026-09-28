using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests for the copy scenario name feature.
/// A small copy button next to each scenario title for pasting into bug reports.
/// </summary>
public class CopyScenarioNameReportTests
{
    private static Feature[] MakeFeatures(params (string id, string name, ExecutionResult result)[] scenarios) =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios = scenarios.Select(s => new Scenario
            {
                Id = s.id,
                DisplayName = s.name,
                IsHappyPath = false,
                Result = s.result
            }).ToArray()
        }
    ];

    private static string GenerateReport(Feature[] features, string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Report_scenario_contains_copy_button()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "CopyScenarioBtn.html");
        Assert.Contains("copy-scenario-name", content);
    }

    [Fact]
    public void Report_contains_copy_javascript()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "CopyScenarioJs.html");
        Assert.Contains("copy_scenario_name", content);
    }

    [Fact]
    public void Report_copy_button_uses_clipboard_api()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "CopyScenarioClipboard.html");
        Assert.Contains("navigator.clipboard", content);
    }

    [Fact]
    public void Report_copy_button_copies_through_the_shared_writer_which_works_without_a_secure_context()
    {
        // navigator.clipboard exists only in a secure context: on a report served over plain http from another machine the
        // button threw. The writer it calls falls back to a selection copy (NonSecureOriginTests clicks it there).
        var content = GenerateReport(MakeFeatures(("t1", "Create order", ExecutionResult.Passed)), "CopyScenarioWriter.html");
        Assert.Contains("function copyTextToClipboard(text)", content);
        Assert.Contains("document.execCommand('copy')", content);
        Assert.Contains("copyTextToClipboard(name).then(", content);
    }

    [Fact]
    public void No_report_script_writes_the_clipboard_except_through_the_shared_writer()
    {
        // A copy made with navigator.clipboard.writeText directly throws where the page has no secure context.
        var assembly = typeof(ReportGenerator).Assembly;
        var direct = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && !n.EndsWith("report-copy-text-function.js", StringComparison.OrdinalIgnoreCase))
            .Where(n =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return reader.ReadToEnd().Contains("navigator.clipboard.writeText", StringComparison.Ordinal);
            })
            .ToList();
        Assert.Empty(direct);
        Assert.Contains("function copyTextToClipboard(text)", DiagramContextMenu.GetContextMenuScript());
    }

    [Fact]
    public void Report_copy_button_has_title_attribute()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "CopyScenarioTitle.html");
        Assert.Contains("title=\"Copy scenario name\"", content);
    }

    [Fact]
    public void Report_copy_button_does_not_trigger_scenario_toggle()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "CopyScenarioNoToggle.html");
        Assert.Contains("stopPropagation", content);
    }
}
