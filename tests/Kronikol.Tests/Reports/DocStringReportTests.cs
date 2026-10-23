using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class DocStringReportTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "DocString.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    private static Feature[] FeaturesWithStep(ScenarioStep step) =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                    Steps = [step]
                }
            ]
        }
    ];

    [Fact]
    public void Step_with_docstring_renders_pre_block()
    {
        var features = FeaturesWithStep(new ScenarioStep
        {
            Text = "Given a request body",
            Status = ExecutionResult.Passed,
            DocString = "{ \"name\": \"test\" }"
        });

        var content = GenerateReport(features);
        Assert.Contains("<pre class=\"step-docstring\">", content);
        Assert.Contains("{ &quot;name&quot;: &quot;test&quot; }", content);
    }

    [Fact]
    public void Step_with_docstring_html_encodes_content()
    {
        var features = FeaturesWithStep(new ScenarioStep
        {
            Text = "Given malicious input",
            Status = ExecutionResult.Passed,
            DocString = "<script>alert('xss')</script>"
        });

        var content = GenerateReport(features);
        Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;", content);
        Assert.DoesNotContain("<script>alert('xss')</script>", content);
    }

    [Fact]
    public void Step_without_docstring_omits_pre_block()
    {
        var features = FeaturesWithStep(new ScenarioStep
        {
            Text = "Given something simple",
            Status = ExecutionResult.Passed
        });

        var content = GenerateReport(features);
        Assert.DoesNotContain("<pre class=\"step-docstring\">", content);
    }

    [Fact]
    public void Step_with_docstring_media_type_renders_code_class()
    {
        var features = FeaturesWithStep(new ScenarioStep
        {
            Text = "Given JSON payload",
            Status = ExecutionResult.Passed,
            DocString = "{ \"key\": \"value\" }",
            DocStringMediaType = "json"
        });

        var content = GenerateReport(features);
        Assert.Contains("<code class=\"language-json\">", content);
    }
}
