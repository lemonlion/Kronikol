using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// In plain-img mode the diagram source is emitted uncompressed inside a
/// <c>.raw-plantuml &lt;pre&gt;</c>. A payload containing <c>&lt;</c> or <c>&amp;</c> must be
/// HTML-encoded there: unencoded it yields malformed markup and breaks the
/// <c>textContent</c> round-trip the deep-search verify pass depends on.
/// </summary>
public class RawPlantUmlEncodingTests
{
    private static Feature[] SingleScenarioFeature(string scenarioId = "s1") =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = scenarioId, DisplayName = "Create order", Result = ExecutionResult.Passed,
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a valid request", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private const string HostileSource = "@startuml\nA -> B : payload {\"html\": \"<script>alert(1)</script>\", \"cmp\": \"a < b & c\"}\n@enduml";

    [Fact]
    public void Img_mode_raw_plantuml_pre_is_html_encoded()
    {
        var diagrams = new[] { new DefaultDiagramsFetcher.DiagramAsCode("s1", "img.png", HostileSource) };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, SingleScenarioFeature(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "RawPumlEncoding.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.Server);
        var content = File.ReadAllText(path);

        Assert.Contains("raw-plantuml", content);
        Assert.DoesNotContain("<script>alert(1)</script>", content);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", content);
        Assert.Contains("a &lt; b &amp; c", content);
    }

    [Fact]
    public void Img_mode_parameterized_group_raw_plantuml_pre_is_html_encoded()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Withdraw $200", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$200" },
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "the account has funds", Status = ExecutionResult.Passed }]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Withdraw $500", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Amount"] = "$500" },
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "the account has funds", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };
        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode("s1", "img1.png", HostileSource),
            new DefaultDiagramsFetcher.DiagramAsCode("s2", "img2.png", HostileSource)
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "RawPumlEncodingParam.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.Server);
        var content = File.ReadAllText(path);

        Assert.Contains("raw-plantuml", content);
        Assert.DoesNotContain("<script>alert(1)</script>", content);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", content);
    }
}
