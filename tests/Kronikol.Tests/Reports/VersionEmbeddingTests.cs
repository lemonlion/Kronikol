using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class VersionEmbeddingTests
{
    private static Feature[] SimpleFeatures() =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }]
        }
    ];

    [Fact]
    public void Html_report_contains_generator_meta_tag()
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], SimpleFeatures(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "VersionMeta.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);

        Assert.Contains("<meta name=\"generator\" content=\"Kronikol v", content);
    }

    [Fact]
    public void Html_report_contains_kronikol_version_in_the_generator_meta()
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], SimpleFeatures(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "VersionSummary.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);

        // 5.0.0 (plans/OPTION_C_PLAN.md S2): the run band has no hidden version row; the meta says it
        Assert.Contains($"<meta name=\"generator\" content=\"Kronikol v{ReportGenerator.KronikolVersion}", content);
    }

    [Fact]
    public void Html_report_draws_no_kronikol_version_row()
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], SimpleFeatures(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "VersionHidden.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        var content = ReportHtml.Read(path);

        // 4.x drew a hidden "Kronikol Version:" row in the summary table; the run band draws none (5.0.0)
        Assert.DoesNotContain("Kronikol Version", content);
    }

    [Fact]
    public void KronikolVersion_is_not_null_or_empty()
    {
        Assert.NotNull(ReportGenerator.KronikolVersion);
        Assert.NotEmpty(ReportGenerator.KronikolVersion);
        Assert.NotEqual("unknown", ReportGenerator.KronikolVersion);
    }
}
