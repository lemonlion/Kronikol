using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class UrlHashFilterTests
{
    private static Feature[] MakeFeatures() =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1",
                    DisplayName = "Test A",
                    IsHappyPath = true,
                    Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1),
                    Tags = ["Smoke"]
                }
            ]
        }
    ];

    private static string GenerateReport(string fileName)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], MakeFeatures(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    /// <summary>
    /// Extracts the body of a JS function by matching braces from the function declaration.
    /// </summary>
    private static string ExtractFunctionBody(string content, string functionName)
    {
        var marker = $"function {functionName}(";
        var idx = content.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Function '{functionName}' not found in content");

        // Find the opening brace
        var braceStart = content.IndexOf('{', idx);
        Assert.True(braceStart >= 0);

        var depth = 0;
        for (var i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}') depth--;
            if (depth == 0) return content[braceStart..(i + 1)];
        }
        throw new Exception($"Unmatched braces in function '{functionName}'");
    }

    // --- Bug 1: All filter functions should call update_url_hash() ---

    [Fact]
    public void Filter_statuses_calls_update_url_hash()
    {
        var content = GenerateReport("UrlHashStatus.html");
        var body = ExtractFunctionBody(content, "filter_statuses");
        Assert.Contains("update_url_hash()", body);
    }

    [Fact]
    public void Filter_tags_calls_update_url_hash()
    {
        // 5.0.0: Happy paths is the Happy Path tag chip, so filter_tags writes it (hp=1 Only, hpx=1 Hide)
        var content = GenerateReport("UrlHashHappyPath.html");
        Assert.Contains("update_url_hash()", ExtractFunctionBody(content, "filter_tags"));
        var hash = ExtractFunctionBody(content, "url_hash_filters");
        Assert.Contains("parts.push('hp=1')", hash);
        Assert.Contains("parts.push('hpx=1')", hash);
    }

    [Fact]
    public void Run_search_scenarios_calls_update_url_hash()
    {
        var content = GenerateReport("UrlHashSearch.html");
        var body = ExtractFunctionBody(content, "run_search_scenarios");
        Assert.Contains("update_url_hash()", body);
    }

    [Fact]
    public void Filter_dependencies_calls_update_url_hash()
    {
        var content = GenerateReport("UrlHashDeps.html");
        var body = ExtractFunctionBody(content, "filter_dependencies");
        Assert.Contains("update_url_hash()", body);
    }

    // --- Bug 1: update_url_hash should save the tags; a category is a tag (plans/TAGS_PLAN.md §4.10) ---

    [Fact]
    public void Update_url_hash_saves_the_tags_and_writes_no_categories()
    {
        var content = GenerateReport("UrlHashSavesTags.html");
        var body = ExtractFunctionBody(content, "url_hash_filters");
        Assert.Contains("names('tags', chip_values('tag', 'in').filter(notHappy));", body);
        Assert.Contains("names('tagx', chip_values('tag', 'out').filter(notHappy));", body);
        Assert.DoesNotContain("cats", body);
        Assert.DoesNotContain("catmode", body);
    }

    // --- Bug 2: apply_url_hash (parse_url_hash until 5.2.0) should restore a 4.x link's categories, as tags ---

    [Fact]
    public void Parse_url_hash_reads_a_4x_link_s_categories_as_tags()
    {
        var content = GenerateReport("UrlHashRestoresCats.html");
        var body = ExtractFunctionBody(content, "apply_url_hash");
        Assert.Contains("params.cats", body);
        Assert.Contains("params.catmode", body);
        Assert.DoesNotContain("filter_categories", body);
    }
}
