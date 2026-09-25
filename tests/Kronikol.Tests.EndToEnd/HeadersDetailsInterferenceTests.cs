using Microsoft.Playwright;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

[Collection(PlaywrightCollections.Reports)]
public class HeadersDetailsInterferenceTests : PlaywrightTestBase
{
    public HeadersDetailsInterferenceTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string PlantUmlSourceWithHeaders = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc

        caller -> svc : POST /api/orders
        note left
        <color:#686868>Content-Type: application/json
        <color:#686868>Authorization: Bearer token123
        
        {"item":"Widget","qty":2}
        Line 2 of body
        Line 3 of body
        Line 4 of body
        Line 5 of body
        end note

        svc --> caller : 201 Created
        note right
        <color:#686868>Content-Type: application/json
        <color:#686868>X-Request-Id: abc-123
        
        {"id":"abc-123","status":"created"}
        end note
        @enduml
        """;

    private const string PlantUmlSourceManyLines = """
        @startuml
        actor "Caller" as caller
        participant "Service" as svc

        caller -> svc : POST /api/data
        note left
        <color:#686868>Content-Type: application/json
        <color:#686868>Authorization: Bearer token

        Line01-body
        Line02-body
        Line03-body
        Line04-body
        Line05-body
        Line06-body
        Line07-body
        Line08-body
        Line09-body
        Line10-body
        Line11-body
        Line12-body
        Line13-body
        Line14-body
        Line15-body
        end note

        svc --> caller : 200 OK
        @enduml
        """;

    private new string GenerateReport(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Order Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Create order", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I create an order", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "the order is created", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var diagrams = new[] { new DiagramAsCode("t1", "", PlantUmlSourceWithHeaders) };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private string GenerateReportWithManyLines(string fileName)
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Long Note Feature",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Long note scenario", IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a system", Status = ExecutionResult.Passed },
                        ]
                    }
                ]
            }
        };

        var diagrams = new[] { new DiagramAsCode("t1", "", PlantUmlSourceManyLines) };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);

        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    private async Task WaitForDiagramRender(int timeoutMs = 15000)
    {
        await Page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-plantuml] svg').length > 0",
            null, new() { Timeout = timeoutMs, PollingInterval = 200 });
    }

    private async Task ExpandScenario()
    {
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }

    private async Task<string> GetDataPlantuml() =>
        await Page.Locator("[data-plantuml]").GetAttributeAsync("data-plantuml") ?? "";

    // ── Test: Toggling headers preserves the details state ──

    [Fact]
    public async Task Toggling_headers_hidden_preserves_details_truncated_state()
    {
        await Page.GotoAsync(GenerateReport("HD_HeadersPreservesTrunc.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        var truncBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='truncated']");
        await Expect(truncBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));

        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();

        truncBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='truncated']");
        await Expect(truncBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));

        var source = await GetDataPlantuml();
        Assert.Contains("Widget", source);
    }

    [Fact]
    public async Task Toggling_headers_hidden_preserves_details_expanded_state()
    {
        await Page.GotoAsync(GenerateReport("HD_HeadersPreservesExp.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        await Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        var expandedBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']");
        await Expect(expandedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));

        var source = await GetDataPlantuml();
        Assert.Contains("Widget", source);
        Assert.DoesNotContain("color:#686868", source);
    }

    [Fact]
    public async Task Toggling_details_expanded_preserves_headers_hidden_state()
    {
        await Page.GotoAsync(GenerateReport("HD_DetailsPreservesHidden.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        await Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Headers should still be hidden after changing details state
        var headersBtn = Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers']");
        var dataShown = await headersBtn.GetAttributeAsync("data-shown");
        Assert.Equal("false", dataShown);

        var source = await GetDataPlantuml();
        Assert.DoesNotContain("color:#686868", source);
        Assert.Contains("Widget", source);
    }

    [Fact]
    public async Task Toggling_details_collapsed_preserves_headers_hidden_state()
    {
        await Page.GotoAsync(GenerateReport("HD_DetailsCollapsedPreservesHidden.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        await Page.Locator(".toolbar-row .details-radio-btn[data-state='collapsed']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Headers should still be hidden after changing details state
        var headersBtn = Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers']");
        var dataShown = await headersBtn.GetAttributeAsync("data-shown");
        Assert.Equal("false", dataShown);
    }

    [Fact]
    public async Task Toggling_headers_shown_after_hidden_preserves_details_state()
    {
        await Page.GotoAsync(GenerateReport("HD_HeadersShownPreserves.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        await Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);
        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='false']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        var expandedBtn = Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']");
        await Expect(expandedBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));

        var source = await GetDataPlantuml();
        Assert.Contains("color:#686868", source);
        Assert.Contains("Widget", source);
    }

    [Fact]
    public async Task Scenario_level_headers_toggle_does_not_affect_details_radio_buttons()
    {
        await Page.GotoAsync(GenerateReport("HD_ScenarioHeadersRadio.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        var scenario = Page.Locator("details.scenario");
        var scenTruncBtn = scenario.Locator(".details-radio-btn[data-state='truncated']");
        await Expect(scenTruncBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));

        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        await Expect(scenTruncBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
    }

    [Fact]
    public async Task Report_level_details_change_preserves_scenario_level_headers_override()
    {
        await Page.GotoAsync(GenerateReport("HD_ReportDetailsScenarioHeaders.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        var scenario = Page.Locator("details.scenario");
        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        await Page.Locator(".toolbar-row .details-radio-btn[data-state='expanded']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        var source = await GetDataPlantuml();
        Assert.Contains("Widget", source);
        Assert.DoesNotContain("color:#686868", source);
    }

    [Fact]
    public async Task Headers_hidden_state_initializes_noteSteps_from_default()
    {
        await Page.GotoAsync(GenerateReport("HD_HeadersInitNoteSteps.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        var source = await GetDataPlantuml();
        Assert.Contains("Widget", source);

        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        source = await GetDataPlantuml();
        Assert.Contains("Widget", source);
        Assert.DoesNotContain("color:#686868", source);
    }

    [Fact]
    public async Task Toggling_headers_hidden_preserves_scenario_truncation_line_count()
    {
        await Page.GotoAsync(GenerateReportWithManyLines("HD_HeadersPreservesLineCount.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        var scenario = Page.Locator("details.scenario");
        await scenario.Locator(".truncate-lines-select").SelectOptionAsync("5");
        await Page.WaitForTimeoutAsync(2000);

        var sourceAfterTrunc = await GetDataPlantuml();
        Assert.Contains("Line01-body", sourceAfterTrunc);
        Assert.Contains("Line02-body", sourceAfterTrunc);
        Assert.DoesNotContain("Line03-body", sourceAfterTrunc);

        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(2000);

        var sourceAfterHeaders = await GetDataPlantuml();
        Assert.Contains("Line01-body", sourceAfterHeaders);
        Assert.Contains("Line05-body", sourceAfterHeaders);
        Assert.DoesNotContain("Line06-body", sourceAfterHeaders);
        Assert.DoesNotContain("color:#686868", sourceAfterHeaders);
    }

    [Fact]
    public async Task Switching_expanded_to_truncated_respects_scenario_dropdown_value()
    {
        await Page.GotoAsync(GenerateReportWithManyLines("HD_ExpandedToTruncDropdown.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        var scenario = Page.Locator("details.scenario");
        await scenario.Locator(".truncate-lines-select").SelectOptionAsync("5");
        await Page.WaitForTimeoutAsync(2000);

        await scenario.Locator(".details-radio-btn[data-state='expanded']").ClickAsync();
        await Page.WaitForTimeoutAsync(2000);

        var expandedSource = await GetDataPlantuml();
        Assert.Contains("Line15-body", expandedSource);

        await scenario.Locator(".details-radio-btn[data-state='truncated']").ClickAsync();
        await Page.WaitForTimeoutAsync(2000);

        var truncatedSource = await GetDataPlantuml();
        Assert.Contains("Line01-body", truncatedSource);
        Assert.Contains("Line02-body", truncatedSource);
        Assert.DoesNotContain("Line03-body", truncatedSource);

        var selectedValue = await scenario.Locator(".truncate-lines-select").InputValueAsync();
        Assert.Equal("5", selectedValue);
    }

    [Fact]
    public async Task Toggling_headers_shown_after_hidden_preserves_scenario_truncation_line_count()
    {
        await Page.GotoAsync(GenerateReportWithManyLines("HD_HeadersShownPreservesLineCount.html"));
        await ExpandScenario();
        await WaitForDiagramRender();

        var scenario = Page.Locator("details.scenario");
        await scenario.Locator(".truncate-lines-select").SelectOptionAsync("5");
        await Page.WaitForTimeoutAsync(2000);

        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='true']").ClickAsync();
        await Page.WaitForTimeoutAsync(2000);
        await scenario.Locator(".toggle-btn[data-toggle='headers'][data-shown='false']").ClickAsync();
        await Page.WaitForTimeoutAsync(2000);

        var source = await GetDataPlantuml();
        Assert.Contains("Line01-body", source);
        Assert.Contains("Line02-body", source);
        Assert.DoesNotContain("Line03-body", source);
    }
}