using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A report's form controls must not survive a page reload with a value the report itself is not
/// honouring. Firefox restores &lt;select&gt;/&lt;input&gt; state on a plain refresh (Chromium does not),
/// so a reader who picked YAML once came back to a scenario dropdown reading YAML over notes the
/// freshly-loaded script had rendered as JSON — the dropdown lied about the report's actual state.
/// The report's state lives in its script (and, when shared, in the URL hash); the browser must not
/// second-guess it, which <c>autocomplete="off"</c> on each control is what tells it.
/// </summary>
public class FormStateRestorationTests
{
    private const string PlantUmlSourceWithJsonNote = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc

        caller -> svc : POST /api/orders
        note left
        {
        "item": "Widget",
        "qty": 2
        }
        end note
        svc --> caller : 201 Created
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
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "When", Text = "I create an order", Status = ExecutionResult.Passed },
                        ]
                    }
                ]
            }
        };

        var diagrams = new[] { new DiagramAsCode("t1", "", PlantUmlSourceWithJsonNote) };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Note_format_dropdowns_opt_out_of_browser_form_restoration()
    {
        var content = GenerateReport("FormRestore_NoteFormat.html");

        var selects = ReportMarkup.Elements(content, "select", "class=\"note-format-select\"");

        Assert.NotEmpty(selects);
        Assert.All(selects, select => Assert.Contains("autocomplete=\"off\"", select));
    }

    [Fact]
    public void Truncate_lines_dropdowns_opt_out_of_browser_form_restoration()
    {
        var content = GenerateReport("FormRestore_TruncateLines.html");

        var selects = ReportMarkup.Elements(content, "select", "class=\"truncate-lines-select\"");

        Assert.NotEmpty(selects);
        Assert.All(selects, select => Assert.Contains("autocomplete=\"off\"", select));
    }

    [Fact]
    public void Search_and_duration_filter_inputs_opt_out_of_browser_form_restoration()
    {
        var content = GenerateReport("FormRestore_Filters.html");

        var searchBar = ReportMarkup.Elements(content, "input", "id=\"searchbar\"");
        var durationThreshold = ReportMarkup.Elements(content, "input", "id=\"duration-threshold\"");

        Assert.NotEmpty(searchBar);
        Assert.All(searchBar, input => Assert.Contains("autocomplete=\"off\"", input));
        Assert.NotEmpty(durationThreshold);
        Assert.All(durationThreshold, input => Assert.Contains("autocomplete=\"off\"", input));
    }
}
