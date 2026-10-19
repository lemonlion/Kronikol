using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Pins that the scenario filter toggles — databases above all — never rewrite the embedded
/// component diagram. The overview declares its dependency nodes with the same
/// <c>database</c>/<c>collections</c> keywords the databases filter strips from scenario
/// sequence diagrams, so an unguarded filter silently drops dependency nodes and edges
/// from the architecture overview (user-reported against a real production report).
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ComponentDiagramDatabaseToggleTests : PlaywrightTestBase
{
    public ComponentDiagramDatabaseToggleTests(PlaywrightFixture fixture) : base(fixture) { }

    private string GenerateComponentDatabasesReport(string fileName,
        Action<ReportConfigurationOptions>? configure = null) =>
        ReportTestHelper.GenerateReportWithComponentDiagramAndDatabases(TempDir, OutputDir, fileName, configure);

    private async Task OpenComponentDiagramPanel()
    {
        await OpenReportTabAsync("components"); // 5.0.0: a tab of the report bar (plans/OPTION_C_PLAN.md S3)
        await Page.WaitForFunctionAsync("""
            () => {
                var section = document.getElementById('component-diagram');
                if (!section || window.getComputedStyle(section).display === 'none') return false;
                return !!section.querySelector('.plantuml-browser svg');
            }
        """, null, new() { Timeout = 30000, PollingInterval = 200 });
    }

    private async Task<string> GetComponentDiagramSource()
    {
        return await Page.EvaluateAsync<string>("""
            () => document.querySelector('#component-diagram .plantuml-browser').getAttribute('data-plantuml') || ''
        """);
    }

    private async Task<string> GetComponentDiagramSvgText()
    {
        return await Page.EvaluateAsync<string>("""
            () => {
                var svg = document.querySelector('#component-diagram .plantuml-browser svg');
                return svg ? svg.textContent : '';
            }
        """);
    }

    private async Task<string> GetScenarioDiagramSource()
    {
        return await Page.EvaluateAsync<string>("""
            () => {
                var container = document.querySelector('#report-content [data-diagram-type="plantuml"]');
                return container ? (container.getAttribute('data-plantuml') || '') : '';
            }
        """);
    }

    private async Task WaitForScenarioSourceWithout(string needle)
    {
        await Page.WaitForFunctionAsync($$"""
            () => {
                var container = document.querySelector('#report-content [data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && !source.includes('{{needle}}');
            }
        """, null, new() { Timeout = 30000, PollingInterval = 200 });
    }

    private async Task WaitForScenarioSourceWith(string needle)
    {
        await Page.WaitForFunctionAsync($$"""
            () => {
                var container = document.querySelector('#report-content [data-diagram-type="plantuml"]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                var source = container.getAttribute('data-plantuml');
                return source && source.includes('{{needle}}');
            }
        """, null, new() { Timeout = 30000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Databases_toggle_leaves_component_diagram_intact()
    {
        await Page.GotoAsync(GenerateComponentDatabasesReport("CompDbToggleIntact.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await OpenComponentDiagramPanel();

        var componentBefore = await GetComponentDiagramSource();
        Assert.Contains("database \"Spanner\"", componentBefore);
        Assert.Contains("collections \"Redis\"", componentBefore);

        // Stamp the rendered SVG node — the filter must not even re-render the overview,
        // so the exact same DOM node must survive the toggle.
        await Page.EvaluateAsync(
            "() => document.querySelector('#component-diagram .plantuml-browser svg').dataset.pinned = '1'");

        // the toolbar is the Features tab's since 5.0.0 (plans/OPTION_C_PLAN.md S3); the drawn overview stays in its panel
        await OpenReportTabAsync("features");
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();
        await WaitForScenarioSourceWithout("spanner");

        // The scenario sequence diagram was stripped...
        var scenarioAfter = await GetScenarioDiagramSource();
        Assert.DoesNotContain("spanner", scenarioAfter);

        // ...but the component diagram keeps every dependency node and edge.
        var componentAfter = await GetComponentDiagramSource();
        Assert.Contains("database \"Spanner\"", componentAfter);
        Assert.Contains("collections \"Redis\"", componentAfter);
        Assert.Contains("breakfastProvider -[#E74C3C]-> spanner", componentAfter);
        Assert.Contains("breakfastProvider -[#F39C12]-> redis", componentAfter);

        var svgText = await GetComponentDiagramSvgText();
        Assert.Contains("Spanner", svgText);
        Assert.Contains("Redis", svgText);

        var pinnedSurvived = await Page.EvaluateAsync<bool>("""
            () => {
                var svg = document.querySelector('#component-diagram .plantuml-browser svg');
                return !!(svg && svg.dataset.pinned === '1');
            }
        """);
        Assert.True(pinnedSurvived, "component diagram was re-rendered by the databases toggle — filters must not touch it");
    }

    [Fact]
    public async Task Hiding_databases_before_opening_panel_keeps_component_diagram_intact()
    {
        await Page.GotoAsync(GenerateComponentDatabasesReport("CompDbToggleBeforeOpen.html"));
        await Page.Locator("details.feature").First.WaitForAsync();

        // Hide databases while the component diagram panel is still closed and unrendered —
        // its FIRST render must not apply the filter (the _preProcessSource path).
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();
        await OpenComponentDiagramPanel();

        var componentSource = await GetComponentDiagramSource();
        Assert.Contains("database \"Spanner\"", componentSource);
        Assert.Contains("collections \"Redis\"", componentSource);

        var svgText = await GetComponentDiagramSvgText();
        Assert.Contains("Spanner", svgText);
        Assert.Contains("Redis", svgText);
    }

    [Fact]
    public async Task Databases_hidden_default_keeps_component_diagram_intact()
    {
        await Page.GotoAsync(GenerateComponentDatabasesReport("CompDbDefaultHidden.html", o =>
        {
            o.TestRunReportToggleDefaults.DatabasesShown = false;
            o.TestRunReportToggleDefaults.ComponentDiagramVisible = true;
        }));
        // the page opens on the Components tab (5.0.0), its features hidden
        await Page.Locator(".report-tabs").WaitForAsync();

        // The panel is seeded visible, so the component diagram renders on load —
        // under a databases-hidden DEFAULT it must still draw every dependency.
        await Page.WaitForFunctionAsync("""
            () => {
                var section = document.getElementById('component-diagram');
                if (!section || window.getComputedStyle(section).display === 'none') return false;
                return !!section.querySelector('.plantuml-browser svg');
            }
        """, null, new() { Timeout = 30000, PollingInterval = 200 });

        var componentSource = await GetComponentDiagramSource();
        Assert.Contains("database \"Spanner\"", componentSource);
        Assert.Contains("collections \"Redis\"", componentSource);

        var svgText = await GetComponentDiagramSvgText();
        Assert.Contains("Spanner", svgText);
        Assert.Contains("Redis", svgText);

        // The scenario sequence diagrams DO honour the hidden default.
        await OpenReportTabAsync("features");
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForScenarioSourceWithout("spanner");
        var scenarioSource = await GetScenarioDiagramSource();
        Assert.DoesNotContain("spanner", scenarioSource);
    }

    [Fact]
    public async Task Steps_and_assertions_toggles_do_not_reapply_database_filter_to_component_diagram()
    {
        await Page.GotoAsync(GenerateComponentDatabasesReport("CompDbStepsAssertions.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await OpenComponentDiagramPanel();

        // Hide databases, then toggle steps and assertions — their queue builders re-apply
        // the databases filter to every container they process and must skip the overview.
        // the toolbar is the Features tab's since 5.0.0 (plans/OPTION_C_PLAN.md S3); the drawn overview stays in its panel
        await OpenReportTabAsync("features");
        await Page.Locator("button[data-toggle='databases']").First.ClickAsync();
        await WaitForScenarioSourceWithout("spanner");

        await Page.Locator("button[data-toggle='steps']").First.ClickAsync();
        await WaitForScenarioSourceWithout("stepDelimiter");
        var componentAfterSteps = await GetComponentDiagramSource();
        Assert.Contains("database \"Spanner\"", componentAfterSteps);
        Assert.Contains("collections \"Redis\"", componentAfterSteps);

        // Assertions start shown since 5.0.0, so this click HIDES them — the queue builder runs
        // over every container either way, re-applying the databases filter as it goes.
        await WaitForScenarioSourceWith("<<assertionNote>>");
        await Page.Locator("button[data-toggle='assertions']").First.ClickAsync();
        await WaitForScenarioSourceWithout("<<assertionNote>>");
        var componentAfterAssertions = await GetComponentDiagramSource();
        Assert.Contains("database \"Spanner\"", componentAfterAssertions);
        Assert.Contains("collections \"Redis\"", componentAfterAssertions);
    }
}
