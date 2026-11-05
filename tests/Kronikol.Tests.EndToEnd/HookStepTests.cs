using Kronikol.Reports;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What the page paints for a step method that ran with no framework step open, #223's shape: its row stands in the order it
/// ran with no keyword, its bar is drawn and hidden with the rest, and every Gherkin step keeps its own sub-steps. The rows here
/// are what the Reqnroll adapter lists for T01 and T05 (plans/STEP_IDENTITY_PLAN.md).
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class HookStepTests : PlaywrightTestBase
{
    public HookStepTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Diagram = """
        @startuml
        actor "Tests" as tests
        participant "Shop" as shop

        hnote across <<stepDelimiter>> #black:<color:white>Reference data is seeded
        tests -> shop : POST /seed
        hnote across <<stepDelimiter>> #black:<color:white>Given a registered customer
        tests -> shop : POST /customers
        hnote across <<stepDelimiter>> #black:<color:white>When the customer places the order
        tests -> shop : POST /orders
        hnote across <<stepDelimiter>> #black:<color:white>Then the order is confirmed
        tests -> shop : POST /orders/1
        @enduml
        """;

    private static ScenarioStep Row(string? keyword, string text, params string[] subSteps) => new()
    {
        Keyword = keyword,
        Text = text,
        Status = ExecutionResult.Passed,
        SubSteps = subSteps.Length > 0 ? subSteps.Select(sub => new ScenarioStep { Text = sub, Status = ExecutionResult.Passed }).ToArray() : null,
    };

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Hook steps",
            Scenarios =
            [
                new Scenario
                {
                    Id = "t1",
                    DisplayName = "T01 a step method in BeforeScenario",
                    Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1),
                    Steps =
                    [
                        Row(null, "Reference data is seeded"),
                        Row("Given", "a registered customer", "A customer is registered"),
                        Row("When", "the customer places the order", "An order is submitted"),
                        Row("Then", "the order is confirmed", "The order is read back"),
                    ],
                },
                new Scenario
                {
                    Id = "t5",
                    DisplayName = "T05 a step method that fails in BeforeScenario",
                    Result = ExecutionResult.Failed,
                    Duration = TimeSpan.FromSeconds(1),
                    ErrorMessage = "seeding failed",
                    Steps = [new ScenarioStep { Text = "Seeding fails", Status = ExecutionResult.Failed }],
                },
            ],
        }
    ];

    private string Report(string fileName, bool testRunReport = true)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [new DiagramAsCode("t1", "", Diagram)], Features(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), testRunReport ? "Test Report" : "Specifications", testRunReport,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        return new Uri(path).AbsoluteUri;
    }

    private async Task<IReadOnlyList<string>> PaintedRows(string scenario) =>
        await Page.EvaluateAsync<string[]>("""
            name => {
                const scenario = [...document.querySelectorAll('details.scenario')].find(s => s.textContent.includes(name));
                for (let p = scenario; p; p = p.parentElement) if (p.tagName === 'DETAILS') p.open = true;
                scenario.querySelectorAll('details').forEach(d => { d.open = true; });
                const depth = e => { let n = 0; for (let p = e.parentElement; p && p !== scenario; p = p.parentElement) if (p.classList.contains('step')) n++; return n; };
                const own = (step, selector) => [...step.querySelectorAll(selector)].find(c => c.closest('.step') === step)?.textContent.trim().replace(/\s+/g, ' ') ?? '';
                return [...scenario.querySelectorAll('.step')].map(step =>
                    `${'  '.repeat(depth(step))}${own(step, '.step-status')}|${own(step, '.step-keyword')}|${own(step, '.step-text')}`);
            }
            """, scenario);

    private async Task WaitForTheDiagram() =>
        await Page.WaitForFunctionAsync("""
            () => {
                const text = document.querySelector('details.scenario[open] svg text');
                return !!text && !window._plantumlRendering;
            }
            """, null, new() { PollingInterval = 200, Timeout = 120_000 });

    private async Task<string[]> PaintedBars() =>
        await Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('details.scenario[open] svg text')]
                .map(t => t.textContent.trim().split(/\s+/)[0])
                .filter(word => /^(Reference|Given|When|Then)$/.test(word))
            """);

    [Fact]
    public async Task A_hook_steps_row_stands_first_with_no_keyword_and_each_sub_step_stays_under_its_own_parent()
    {
        await Page.GotoReportAsync(Report("HookStepRows.html"));

        Assert.Equal(
        [
            "✓||Reference data is seeded",
            "✓|Given|a registered customer",
            "  ✓||A customer is registered",
            "✓|When|the customer places the order",
            "  ✓||An order is submitted",
            "✓|Then|the order is confirmed",
            "  ✓||The order is read back",
        ], await PaintedRows("T01"));
    }

    [Fact]
    public async Task Its_bar_is_drawn_with_the_others_and_the_Steps_control_hides_it_with_them()
    {
        await Page.GotoReportAsync(Report("HookStepBars.html"));
        await PaintedRows("T01");
        await WaitForTheDiagram();

        Assert.Equal(["Reference", "Given", "When", "Then"], await PaintedBars());

        var scenario = Page.Locator("details.scenario[open]").First;
        await OpenScenarioSettingsAsync(scenario);
        await scenario.Locator(".toggle-btn[data-toggle='steps'][data-shown='true']").First.ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                const container = document.querySelector('details.scenario[open] [data-diagram-type="plantuml"]');
                return !!container && !container._noteRendering && !window._plantumlRendering
                    && !(container.getAttribute('data-plantuml') ?? '').includes('stepDelimiter')
                    && !!container.querySelector('svg text');
            }
            """, null, new() { PollingInterval = 200, Timeout = 120_000 });

        Assert.Empty(await PaintedBars());
    }

    [Fact]
    public async Task A_hook_step_that_fails_is_painted_as_the_failed_row()
    {
        await Page.GotoReportAsync(Report("HookStepFailed.html"));

        Assert.Equal(["✕||Seeding fails"], await PaintedRows("T05"));
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public async Task Its_text_is_painted_as_a_Gherkin_steps_is_in_each_theme(string scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light });
        await Page.GotoReportAsync(Report($"HookStepTheme{scheme}.html"));
        await PaintedRows("T01");

        var colours = await Page.EvaluateAsync<string[]>("""
            () => {
                const scenario = document.querySelector('details.scenario[open]');
                const steps = [...scenario.querySelectorAll('.step')].filter(s => s.parentElement.closest('.step') === null);
                return steps.slice(0, 2).map(step => getComputedStyle(step.querySelector('.step-text')).color);
            }
            """);

        Assert.Equal(colours[1], colours[0]);
    }

    [Fact]
    public async Task On_the_Specifications_page_its_row_stands_first_too()
    {
        await Page.GotoReportAsync(Report("HookStepSpecifications.html", testRunReport: false));

        var rows = await PaintedRows("T01");

        Assert.Equal("||Reference data is seeded", rows[0].TrimStart('✓'));
        Assert.Contains("Given", rows[1]);
    }
}
