using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Tests for the deep link to scenario feature.
/// Each scenario gets a stable anchor ID so CI summaries can link directly to a specific failure.
/// </summary>
public class DeepLinkReportTests
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
    public void Report_scenario_has_id_attribute()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "DeepLinkId.html");
        Assert.Contains("id=\"scenario-", content);
    }

    [Fact]
    public void Report_scenario_anchor_is_derived_from_name()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "DeepLinkName.html");
        Assert.Contains("id=\"scenario-create-order\"", content);
    }

    [Fact]
    public void Report_scenario_anchor_handles_special_characters()
    {
        var features = MakeFeatures(("t1", "Order (with VAT) creates 200 OK", ExecutionResult.Passed));
        var content = GenerateReport(features, "DeepLinkSpecialChars.html");
        Assert.Contains("id=\"scenario-order-with-vat-creates-200-ok\"", content);
    }

    [Fact]
    public void Report_expands_scenario_from_hash_on_load()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "DeepLinkHashExpand.html");

        // Anchored inside the two functions that do the work. The old assertion was a bare
        // `Contains("location.hash")` over the whole report, which any other mention satisfied.
        var parse = ExtractFunctionBody(content, "parse_url_hash");
        Assert.Contains("reveal_url_anchor(anchor)", parse);
        var reveal = ExtractFunctionBody(content, "reveal_url_anchor");
        Assert.Contains("setAttribute('open', '')", reveal);
        Assert.Contains("scrollIntoView", reveal);
    }

    [Fact]
    public void Report_scenario_has_anchor_link_button()
    {
        var features = MakeFeatures(("t1", "Create order", ExecutionResult.Passed));
        var content = GenerateReport(features, "DeepLinkAnchorBtn.html");
        Assert.Contains("scenario-link", content);
    }

    /// <summary>Brace-matched function body, so an assertion lands inside the function it names.</summary>
    private static string ExtractFunctionBody(string content, string functionName)
    {
        var idx = content.IndexOf($"function {functionName}(", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Function '{functionName}' not found in the report");
        var braceStart = content.IndexOf('{', idx);
        var depth = 0;
        for (var i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}') depth--;
            if (depth == 0) return content[braceStart..(i + 1)];
        }
        throw new Exception($"Unmatched braces in '{functionName}'");
    }
}
