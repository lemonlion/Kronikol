using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A sub-step's data table is drawn under it, read from the page as painted (<c>plans/REPORT_FORMAT_PLAN.md</c> F20).
/// Until 4.27.1 a sub-step left its tables to the combined table under a scenario's steps, which gathers top-level
/// steps' only, so the table was drawn nowhere.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class SubStepTableTests(PlaywrightFixture fixture) : PlaywrightTestBase(fixture)
{
    [Fact]
    public async Task Opening_a_composite_step_shows_its_sub_step_s_table()
    {
        await Page.GotoReportAsync(Generate("SubStepTable.html"));
        // The feature and scenario open as any reader opens them; the composite step is the click under test.
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature, details.scenario').forEach(d => d.open = true)");
        await Page.Locator("details.step-collapsible > summary").First.ClickAsync();

        var cell = Page.Locator(".sub-steps .step-param-table[data-param='users'] td", new() { HasTextString = "Alice" }).First;
        await Expect(cell).ToBeVisibleAsync();
        var box = await cell.BoundingBoxAsync();
        Assert.True(box is { Width: > 0, Height: > 0 }, "the sub-step's table cell has no area on the page");
    }

    private string Generate(string fileName)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Users",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "register", DisplayName = "Register users", Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "I register the users", Status = ExecutionResult.Passed,
                                SubSteps =
                                [
                                    new ScenarioStep
                                    {
                                        Keyword = "And", Text = "the users are", Status = ExecutionResult.Passed,
                                        Parameters =
                                        [
                                            new StepParameter
                                            {
                                                Name = "users",
                                                Kind = StepParameterKind.Tabular,
                                                TabularValue = new TabularParameterValue(
                                                    [new TabularColumn("Name", false), new TabularColumn("Age", false)],
                                                    [new TabularRow(TableRowType.Matching,
                                                        [new TabularCell("Alice", null, VerificationStatus.NotApplicable),
                                                         new TabularCell("30", null, VerificationStatus.NotApplicable)])])
                                            }
                                        ]
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ];

        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, fileName), "Sub-step tables", true, diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
