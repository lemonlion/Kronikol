using System.Text.Json;
using Kronikol.Reports;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A scenario's steps line up with their headings (5.0.0, plans/OPTION_C_PLAN.md S12, round 17 item 18): the Steps,
/// Background, failure and diagrams headings draw their triangles in one column, a collapsible step's triangle sits in
/// that column too, and a heading's first letter starts where the steps' status circles do. Measured in Chromium and,
/// where Playwright's Firefox is installed, in Firefox, whose native disclosure marker differs in size (the reason the
/// report draws its own).
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class StepAlignmentTests : PlaywrightTestBase
{
    public StepAlignmentTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Scenario = "#scenario-pay-order";

    private string GenerateAlignmentReport(string fileName)
    {
        ScenarioStep Step(string keyword, string text, ExecutionResult status, params ScenarioStep[] sub) =>
            new() { Keyword = keyword, Text = text, Status = status, SubSteps = sub.Length > 0 ? sub : null };
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Payments",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "pay", DisplayName = "Pay order", Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(80),
                        ErrorMessage = "Expected:    paid\nActual:      declined",
                        BackgroundSteps = [Step("Given", "a shop", ExecutionResult.Passed)],
                        Steps =
                        [
                            Step("Given", "a basket", ExecutionResult.Passed),
                            Step("When", "it is paid", ExecutionResult.Passed,
                                Step("Given", "a card", ExecutionResult.Passed), Step("When", "it is charged", ExecutionResult.Passed)),
                            Step("Then", "the order is paid", ExecutionResult.Failed)
                        ]
                    }
                ]
            }
        };
        var diagrams = new[] { new DiagramAsCode("pay", "", "@startuml\nparticipant Shop\nparticipant Bank\nShop -> Bank: charge\n@enduml") };
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features, DateTime.UtcNow, DateTime.UtcNow, null, fileName, "Alignment", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: true);
        var target = Path.Combine(TempDir, fileName);
        File.Copy(path, target, true);
        return new Uri(target).AbsoluteUri;
    }

    private async Task OpenAsync(IPage page, string fileName)
    {
        await page.GotoReportAsync(GenerateAlignmentReport(fileName));
        await page.Locator("details.feature").First.WaitForAsync();
        // every disclosure open, so each heading and step is laid out
        await page.EvaluateAsync("() => document.querySelectorAll('details').forEach(d => d.open = true)");
    }

    // The left edge of each heading's drawn triangle, the collapsible step's, the first letter of each heading and
    // each top-level step's status circle, in page pixels.
    private const string Measure = $$"""
        () => {
            const s = document.querySelector('{{Scenario}}');
            const box = el => {
                const r = el.getBoundingClientRect();
                return r.left + parseFloat(getComputedStyle(el, '::before').left);
            };
            const firstLetter = el => {
                const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, { acceptNode: n => n.textContent.trim() ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP });
                const text = walker.nextNode();
                const range = document.createRange();
                const at = text.textContent.search(/\S/);
                range.setStart(text, at);
                range.setEnd(text, at + 1);
                return range.getBoundingClientRect().left;
            };
            const headings = ['.scenario-steps', '.scenario-background', '.example-diagrams', 'details.failure-result']
                .map(sel => s.querySelector(sel + ' > summary'));
            return {
                found: headings.map(h => h !== null),
                triangles: headings.filter(h => h).map(box),
                letters: headings.filter(h => h).map(firstLetter),
                step: box(s.querySelector('.scenario-steps > .step-collapsible > summary')),
                circles: Array.from(s.querySelectorAll('.scenario-steps > .step > .step-status, .scenario-steps > .step-collapsible > summary .step-status, .scenario-background > .step > .step-status'))
                    .map(c => c.getBoundingClientRect().left)
            };
        }
        """;

    private record Layout(bool[] Found, double[] Triangles, double[] Letters, double Step, double[] Circles);

    // Playwright cannot fill a positional record, so the layout comes back as JSON
    private static async Task<Layout> MeasureAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>($"() => JSON.stringify(({Measure})())");
        return JsonSerializer.Deserialize<Layout>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static void AssertAligned(Layout l, string browser)
    {
        Assert.Equal([true, true, true, true], l.Found);
        Assert.True(l.Triangles.Max() - l.Triangles.Min() < 0.5, $"{browser}: heading triangles at {string.Join(", ", l.Triangles)}");
        Assert.True(Math.Abs(l.Step - l.Triangles[0]) < 0.5, $"{browser}: a step's triangle at {l.Step}, the headings' at {l.Triangles[0]}");
        // the three steps and the background step
        Assert.Equal(4, l.Circles.Length);
        Assert.True(l.Circles.Max() - l.Circles.Min() < 0.5, $"{browser}: status circles at {string.Join(", ", l.Circles)}");
        foreach (var letter in l.Letters)
            Assert.True(Math.Abs(letter - l.Circles[0]) < 0.5, $"{browser}: a heading's first letter at {letter}, the circles at {l.Circles[0]}");
    }

    [Fact]
    public async Task Headings_and_steps_share_their_columns_in_Chromium()
    {
        await OpenAsync(Page, "StepAlignment_Chromium.html");

        AssertAligned(await MeasureAsync(Page), "Chromium");
    }

    [Fact]
    public async Task Headings_and_steps_share_their_columns_in_Firefox()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        IBrowser? firefox = null;
        try
        {
            firefox = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException)
        {
        }
        Assert.SkipWhen(firefox is null, "Firefox is not installed (CI installs chromium only)");

        await using (firefox)
        {
            var page = await OpenPageAsync(firefox!);
            await OpenAsync(page, "StepAlignment_Firefox.html");

            AssertAligned(await MeasureAsync(page), "Firefox");
        }
    }
}
