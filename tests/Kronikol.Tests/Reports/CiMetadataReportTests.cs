using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class CiMetadataReportTests
{
    private static string GenerateReport(Feature[] features, CiMetadata? ciMetadata = null)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "CiMetadata.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            ciMetadata: ciMetadata);
        return ReportHtml.Read(path);
    }

    private static Feature[] SimpleFeatures() =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios = [new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }]
        }
    ];

    [Fact]
    public void Report_shows_ci_metadata_when_present()
    {
        var ci = new CiMetadata(CiEnvironment.GitHubActions, "42", "main", "abc123def456789", null, "owner/repo", "12345");
        var content = GenerateReport(SimpleFeatures(), ci);
        // 5.0.0: the run band's CI zone (plans/OPTION_C_PLAN.md S2) in place of the 4.x table
        Assert.Contains("<div class=\"run-stat\"><dt>Build</dt><dd>#42</dd></div>", content);
        Assert.Contains("<div class=\"run-stat\"><dt>Branch</dt><dd>main</dd></div>", content);
    }

    [Fact]
    public void Report_shows_pipeline_link()
    {
        var ci = new CiMetadata(CiEnvironment.GitHubActions, "42", "main", "abc123", "https://github.com/owner/repo/actions/runs/12345", "owner/repo", "12345");
        var content = GenerateReport(SimpleFeatures(), ci);
        Assert.Contains("<a class=\"run-link\" href=\"https://github.com/owner/repo/actions/runs/12345\"", content);
        Assert.Contains("<dt>Run</dt>", content);
    }

    [Fact]
    public void Report_shows_short_commit_sha_with_full_title()
    {
        var ci = new CiMetadata(CiEnvironment.GitHubActions, "42", "main", "abc123def456789", null, null, null);
        var content = GenerateReport(SimpleFeatures(), ci);
        Assert.Contains("abc123d", content); // 7-char display
        Assert.Contains("title=\"abc123def456789\"", content); // full SHA in title
    }

    [Fact]
    public void Report_omits_ci_section_when_null()
    {
        var content = GenerateReport(SimpleFeatures(), ciMetadata: null);
        Assert.DoesNotContain("<div class=\"run-zone run-ci\">", content);
        Assert.DoesNotContain("<dt>Build</dt>", content);
    }
}
