using System.Text.RegularExpressions;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Every name and text a test run hands the report is drawn as text, never as markup. Names come from test
/// code, and through <c>kronikol ingest</c> and <c>kronikol merge</c> from files written by other tools, so a
/// <c>&lt;</c> in one is a character to show. Until 4.5.1 the feature and scenario headers and the report's
/// title wrote theirs raw: a scenario named "Place an order for &lt;item&gt;" lost its "&lt;item&gt;" and a
/// name holding a tag drew the tag.
/// </summary>
[Collection("DiagramsFetcher")]
public class ReportHtmlEncodingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-encoding-" + Guid.NewGuid().ToString("N"));

    public ReportHtmlEncodingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    // A marker per field, so a failure names the field that leaked.
    private static string M(string field) => $"<kx-{field}>";

    private static Feature[] EveryText(bool grouped) =>
    [
        new Feature
        {
            DisplayName = "Feature " + M("feature-name"),
            Description = "About " + M("feature-description"),
            Endpoint = "/api/" + M("feature-endpoint"),
            Labels = ["label " + M("feature-label")],
            Scenarios =
            [
                new Scenario
                {
                    Id = "enc-1", DisplayName = "Place an order for " + M("scenario-name"),
                    Description = "About " + M("scenario-description"),
                    Labels = ["label " + M("scenario-label")], Categories = ["cat " + M("scenario-category")],
                    Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(12),
                    ErrorMessage = "Expected " + M("error-message"), ErrorStackTrace = "at " + M("stack-trace"),
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a step " + M("step-text"), Status = ExecutionResult.Failed }],
                },
                new Scenario
                {
                    Id = "enc-2", DisplayName = "Adjust " + M("outline-name") + "(by: 1)", OutlineId = "Adjust " + M("outline-id"),
                    ExampleValues = new Dictionary<string, string> { ["by " + M("example-key")] = "1 " + M("example-value") },
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(5),
                },
                new Scenario
                {
                    Id = "enc-3", DisplayName = "Adjust " + M("outline-name") + "(by: 2)", OutlineId = "Adjust " + M("outline-id"),
                    ExampleValues = new Dictionary<string, string> { ["by " + M("example-key")] = "2 " + M("example-value") },
                    Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(6),
                },
            ]
        }
    ];

    private string Render(string fileName, bool includeTestRunData, bool grouped = true) =>
        File.ReadAllText(ReportGenerator.GenerateHtmlReport(
            [], EveryText(grouped), DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(_dir, fileName), "Title " + M("title"), includeTestRunData,
            groupParameterizedTests: grouped));

    // Script bodies are left out: a script is not parsed as markup, and the data blocks carry the text as
    // JSON, which ReportJsonEncodingTests covers.
    private static string WithoutScripts(string html) =>
        Regex.Replace(html, "<script\\b[^>]*>.*?</script>", "<script></script>", RegexOptions.Singleline);

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void No_text_from_the_run_is_drawn_as_markup(bool includeTestRunData, bool grouped)
    {
        var html = WithoutScripts(Render($"Encoding-{includeTestRunData}-{grouped}.html", includeTestRunData, grouped));

        var leaked = Regex.Matches(html, "<kx-[a-z-]+>").Select(m => m.Value).Distinct().ToArray();
        Assert.True(leaked.Length == 0, "drawn as markup: " + string.Join(", ", leaked));
    }

    [Fact]
    public void The_headers_show_the_names_as_written()
    {
        var html = Render("Headers.html", includeTestRunData: true);

        Assert.Contains("<summary class=\"h2 failed\">Feature &lt;kx-feature-name&gt;", html);
        Assert.Contains("title=\"Failed — an assertion or runtime failure occurred\">Place an order for &lt;kx-scenario-name&gt;", html);
        Assert.Contains("<title>Title &lt;kx-title&gt;</title>", html);
        Assert.Contains("<h1>Title &lt;kx-title&gt;</h1>", html);
    }
}
