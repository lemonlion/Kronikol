using System.Text.RegularExpressions;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Every image the report draws opens in its lightbox (plans/LIST_CAPS_PLAN.md 3.10). A step's image always did; a
/// scenario's own, and a parameterized row's in its panel, were links that opened a new tab, although the wiki promised
/// the lightbox for Playwright's failure screenshot, which is the scenario's own. The browser side is in
/// <c>AttachmentRenderingTests</c>.
/// </summary>
[Collection("DiagramsFetcher")]
public class ScenarioImageLightboxTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-lightbox").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_scenarios_image_a_rows_image_and_a_steps_image_each_open_in_the_lightbox()
    {
        var html = File.ReadAllText(ReportGenerator.GenerateHtmlReport(
            Array.Empty<DiagramAsCode>(), Features(), Start, Start.AddMinutes(1), null, Path.Combine(_dir, "Lightbox.html"), "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs));

        var links = Regex.Matches(html, "<a class=\"attachment-image-link\" [^>]*>").Select(m => m.Value).ToArray();
        foreach (var name in new[] { "own.png", "row.png", "step.png" })
            Assert.Contains($"<a class=\"attachment-image-link\" href=\"attachments/{name}\" onclick=\"openLightbox(event, this)\" title=\"Show {name}, an image this {(name == "step.png" ? "step" : "scenario")} attached, at full size\">", links);
        Assert.Equal(3, links.Length);
        Assert.DoesNotContain(links, link => link.Contains("target=", StringComparison.Ordinal));
    }

    internal static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = "own", DisplayName = "Pays by card", Result = ExecutionResult.Failed,
                    Attachments = [new FileAttachment("own.png", "attachments/own.png", "image/png")],
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "When", Text = "it pays", Status = ExecutionResult.Failed,
                            Attachments = [new FileAttachment("step.png", "attachments/step.png", "image/png")]
                        }
                    ]
                },
                new Scenario
                {
                    Id = "row-1", DisplayName = "Row 1", Result = ExecutionResult.Passed, OutlineId = "outline", ExampleValues = new() { ["n"] = "1" },
                    Attachments = [new FileAttachment("row.png", "attachments/row.png", "image/png")]
                },
                new Scenario
                {
                    Id = "row-2", DisplayName = "Row 2", Result = ExecutionResult.Passed, OutlineId = "outline", ExampleValues = new() { ["n"] = "2" },
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "it is paid", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];
}
