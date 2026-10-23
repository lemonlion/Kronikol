using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class CustomBrandingReportTests
{
    private static string GenerateReport(Feature[] features, string? customCss = null, string? customFaviconBase64 = null, string? customLogoHtml = null)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "CustomBranding.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            customCss: customCss, customFaviconBase64: customFaviconBase64, customLogoHtml: customLogoHtml);
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
    public void Report_injects_custom_css_after_default_styles()
    {
        var content = GenerateReport(SimpleFeatures(), customCss: "body { background: navy; }");
        Assert.Contains("<style>body { background: navy; }</style>", content);
        // Custom CSS should appear after the default stylesheet
        var defaultStyleEnd = content.IndexOf("</style>");
        var customStyleStart = content.IndexOf("<style>body { background: navy; }</style>");
        Assert.True(customStyleStart > defaultStyleEnd);
    }

    [Fact]
    public void Report_injects_custom_favicon()
    {
        var content = GenerateReport(SimpleFeatures(), customFaviconBase64: "data:image/png;base64,ABC123");
        Assert.Contains("<link rel=\"icon\" href=\"data:image/png;base64,ABC123\">", content);
    }

    [Fact]
    public void Report_uses_default_kronikol_favicon_when_none_specified()
    {
        var content = GenerateReport(SimpleFeatures());
        Assert.Contains("<link rel=\"icon\" href=\"data:image/svg+xml;base64,", content);
        Assert.Contains("aWVudCBpZD0ic2Nyb2xs", content); // scroll gradient from the SVG
    }

    [Fact]
    public void Report_custom_favicon_overrides_default()
    {
        var content = GenerateReport(SimpleFeatures(), customFaviconBase64: "data:image/png;base64,CUSTOM");
        Assert.Contains("<link rel=\"icon\" href=\"data:image/png;base64,CUSTOM\">", content);
        Assert.DoesNotContain($"<link rel=\"icon\" href=\"{Kronikol.Constants.DefaultFavicon.DataUri}\">", content);
    }

    [Fact]
    public void Report_injects_custom_logo_html()
    {
        var content = GenerateReport(SimpleFeatures(), customLogoHtml: "<img src=\"logo.png\" alt=\"Logo\">");
        Assert.Contains("<div class=\"custom-logo\"><img src=\"logo.png\" alt=\"Logo\"></div>", content);
        // Logo should appear before the title in body
        var logoPos = content.IndexOf("<div class=\"custom-logo\">");
        var h1Pos = content.IndexOf("<h1>Test</h1>");
        Assert.True(logoPos < h1Pos, $"Logo at {logoPos} should be before h1 at {h1Pos}");
    }

    [Fact]
    public void Report_works_without_custom_branding()
    {
        var content = GenerateReport(SimpleFeatures());
        Assert.DoesNotContain("custom-logo", content);
        Assert.Contains("<link rel=\"icon\"", content); // default TTD favicon is always present
        // Should only have one <style> block (the default one)
        var styleCount = 0;
        var idx = 0;
        while ((idx = content.IndexOf("<style>", idx)) != -1) { styleCount++; idx++; }
        Assert.Equal(1, styleCount);
    }
}
