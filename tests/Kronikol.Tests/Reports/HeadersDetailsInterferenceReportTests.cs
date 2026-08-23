using Kronikol.Constants;
using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

public class HeadersDetailsInterferenceReportTests
{
    private const string PlantUmlSourceWithHeaders = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc

        caller -> svc : POST /api/orders
        note left
        <color:gray>Content-Type: application/json
        <color:gray>Authorization: Bearer token123
        
        {"item":"Widget","qty":2}
        Line 2 of body
        Line 3 of body
        Line 4 of body
        Line 5 of body
        end note

        svc --> caller : 201 Created
        note right
        <color:gray>Content-Type: application/json
        
        {"id":"abc-123","status":"created"}
        end note
        @enduml
        """;

    private static string GenerateReport(string fileName)
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
                        Result = ExecutionResult.Passed,
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I create an order", Status = ExecutionResult.Passed },
                        ]
                    }
                ]
            }
        };

        var diagrams = new[] { new DiagramAsCode("t1", "", PlantUmlSourceWithHeaders) };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void BuildHeadersQueue_initializes_noteSteps_from_detailsDefault()
    {
        // The JS buildHeadersQueue must initialize _noteSteps from window._detailsDefault
        // not leave it as empty {} (which would collapse all notes).
        var content = GenerateReport("HD_BuildHeadersQueue.html");

        // The JS should contain logic to populate _noteSteps from window._detailsDefault
        // when container._noteSteps is first initialized in buildHeadersQueue.
        // Look for the fix: initializing _noteSteps with default state in buildHeadersQueue
        Assert.Contains("_detailsDefault", content);

        // The buildHeadersQueue function must reference window._detailsDefault
        // to properly populate _noteSteps for uninitialized containers
        var buildHeadersIdx = content.IndexOf("function buildHeadersQueue");
        var buildHeadersEnd = content.IndexOf("return queue;", buildHeadersIdx);
        var buildHeadersBody = content[buildHeadersIdx..buildHeadersEnd];
        Assert.Contains("_detailsDefault", buildHeadersBody);
    }

    [Fact]
    public void BuildDetailsQueue_propagates_headersHidden_from_global()
    {
        // The JS buildDetailsQueue should set container._headersHidden from window._headersHidden
        // when it hasn't been set yet, so that re-rendering preserves the headers state.
        var content = GenerateReport("HD_BuildDetailsQueue.html");

        var buildDetailsIdx = content.IndexOf("function buildDetailsQueue");
        var buildDetailsEnd = content.IndexOf("return queue;", buildDetailsIdx);
        var buildDetailsBody = content[buildDetailsIdx..buildDetailsEnd];
        Assert.Contains("_headersHidden", buildDetailsBody);
    }

    [Fact]
    public void PreProcessSource_always_sets_headersHidden()
    {
        // _preProcessSource must always set el._headersHidden, even in the early-return path
        // where state === 'expanded' && !window._headersHidden (which previously skipped it).
        var content = GenerateReport("HD_PreProcess.html");

        var preProcessIdx = content.IndexOf("_preProcessSource");
        var preProcessEnd = content.IndexOf("processRenderQueue", preProcessIdx);
        var preProcessBody = content[preProcessIdx..preProcessEnd];

        // The function should set _headersHidden in all paths, not just the processing path
        // Count occurrences of _headersHidden assignment in the function
        var assignmentCount = 0;
        var searchFrom = 0;
        while (true)
        {
            var idx = preProcessBody.IndexOf("_headersHidden", searchFrom);
            if (idx < 0) break;
            assignmentCount++;
            searchFrom = idx + 1;
        }
        // Should reference _headersHidden multiple times (including the always-set line)
        Assert.True(assignmentCount >= 3, $"_preProcessSource should reference _headersHidden at least 3 times, found {assignmentCount}");
    }

    [Fact]
    public void Report_contains_independent_sync_functions_for_headers_and_details()
    {
        // Verify that syncRadioButtons uses [data-state] selector (not just .details-radio-btn)
        // and syncToggleBtn uses .toggle-btn selector
        var content = GenerateReport("HD_SyncFunctions.html");

        // syncRadioButtons should select '.details-radio-btn[data-state]'
        Assert.Contains(".details-radio-btn[data-state]", content);

        // syncToggleBtn should select '.toggle-btn[data-toggle='
        Assert.Contains(".toggle-btn[data-toggle=", content);
    }

    [Fact]
    public void Headers_toggle_uses_data_toggle_attribute()
    {
        // Headers toggle must use data-toggle="headers" attribute, NOT data-state,
        // to prevent syncRadioButtons from interfering with it.
        var content = GenerateReport("HD_DataAttributes.html");

        var buttons = ReportMarkup.Elements(content, "button", "data-toggle=\"headers\"");

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button =>
        {
            Assert.Contains("data-shown=\"true\"", button);
            Assert.DoesNotContain("data-state", button);
        });
    }

    [Fact]
    public void Toolbar_right_has_no_right_margin()
    {
        // The toolbar-right should not have margin-right so that the toggle buttons
        // (Assertions Shown/Hidden, etc.) align with the right edge of the filtering-box.
        var css = Stylesheets.HtmlReportStyleSheet;
        var idx = css.IndexOf(".toolbar-right {");
        Assert.True(idx >= 0, ".toolbar-right CSS rule should exist");
        var ruleEnd = css.IndexOf('}', idx);
        var rule = css[idx..ruleEnd];
        Assert.DoesNotContain("margin-right", rule);
    }

    [Fact]
    public void Scenario_has_content_visibility_auto_for_large_report_performance()
    {
        var css = Stylesheets.HtmlReportStyleSheet;
        var idx = css.IndexOf(".scenario {");
        Assert.True(idx >= 0, ".scenario CSS rule should exist");
        var ruleEnd = css.IndexOf('}', idx);
        var rule = css[idx..ruleEnd];
        Assert.Contains("content-visibility: auto", rule);
        Assert.Contains("contain-intrinsic-size:", rule);
    }
}
