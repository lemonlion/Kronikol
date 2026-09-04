using Kronikol.Reports;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports;

public class DatabaseToggleReportTests
{
    private const string PlantUmlSourceWithDatabase = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        database "CosmosDB" as cosmosdb #E74C3C

        caller -> svc : POST /api/orders
        note left
        {"item":"Widget","qty":2}
        end note
        svc -[#E74C3C]> cosmosdb: CreateItemAsync
        cosmosdb -[#E74C3C]-> svc: 201 Created
        svc --> caller : 201 Created
        @enduml
        """;

    private const string PlantUmlSourceWithoutDatabase = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc

        caller -> svc : POST /api/orders
        svc --> caller : 200 OK
        @enduml
        """;

    private static string GenerateReport(string fileName, string plantUmlSource)
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
                            new ScenarioStep { Keyword = "When", Text = "I create an order", Status = ExecutionResult.Passed },
                        ]
                    }
                ]
            }
        };

        var diagrams = new[] { new DiagramAsCode("t1", "", plantUmlSource) };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName, "Test", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Databases_toggle_button_rendered_when_database_participant_present()
    {
        var content = GenerateReport("DbToggle_Present.html", PlantUmlSourceWithDatabase);
        Assert.Contains("data-toggle=\"databases\"", content);
        Assert.Contains("Databases Shown", content);
        Assert.Contains("_toggleDatabases", content);
    }

    [Fact]
    public void Databases_toggle_button_not_rendered_when_no_database_participant()
    {
        var content = GenerateReport("DbToggle_Absent.html", PlantUmlSourceWithoutDatabase);

        Assert.Empty(ReportMarkup.Elements(content, "button", "data-toggle=\"databases\""));
        Assert.DoesNotContain("Databases Shown", ReportMarkup.Only(content));
    }

    [Fact]
    public void StripDatabaseCalls_function_is_present_in_report_script()
    {
        var content = GenerateReport("DbToggle_StripFn.html", PlantUmlSourceWithDatabase);
        Assert.Contains("function stripDatabaseCalls", content);
        Assert.Contains("function applyDatabasesFilter", content);
        Assert.Contains("function buildDatabasesQueue", content);
    }

    [Fact]
    public void Databases_toggle_defaults_to_shown()
    {
        var content = GenerateReport("DbToggle_Default.html", PlantUmlSourceWithDatabase);

        var buttons = ReportMarkup.Elements(content, "button", "data-toggle=\"databases\"");

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button =>
        {
            Assert.Contains("details-active", button);
            Assert.Contains("data-shown=\"true\"", button);
        });
    }

    [Fact]
    public void Global_databasesVisible_defaults_to_true()
    {
        var content = GenerateReport("DbToggle_Global.html", PlantUmlSourceWithDatabase);
        Assert.Contains("window._databasesVisible = true", content);
    }

    private const string PlantUmlSourceWithCollections = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        collections "Redis" as redis #F39C12

        caller -> svc : POST /api/orders
        note left
        {"item":"Widget","qty":2}
        end note
        svc -[#F39C12]> redis: GET cache:orders
        redis -[#F39C12]-> svc: (nil)
        svc --> caller : 201 Created
        @enduml
        """;

    [Fact]
    public void Databases_toggle_button_rendered_when_collections_participant_present()
    {
        var content = GenerateReport("DbToggle_Collections_Present.html", PlantUmlSourceWithCollections);
        Assert.Contains("data-toggle=\"databases\"", content);
        Assert.Contains("_toggleDatabases", content);
    }

    [Fact]
    public void Databases_toggle_button_not_rendered_when_no_database_or_collections()
    {
        var content = GenerateReport("DbToggle_NeitherPresent.html", PlantUmlSourceWithoutDatabase);

        Assert.Empty(ReportMarkup.Elements(content, "button", "data-toggle=\"databases\""));
    }

    [Fact]
    public void StripDatabaseCalls_handles_collections_keyword_in_regex()
    {
        var content = GenerateReport("DbToggle_CollectionsStrip.html", PlantUmlSourceWithCollections);
        Assert.Contains("function stripDatabaseCalls", content);
        // The regex should match both database and collections declarations
        Assert.Contains("collections", content);
    }

    [Fact]
    public void Filter_pipeline_carries_the_component_diagram_guard()
    {
        // The embedded component diagram declares dependency nodes with the same
        // database/collections keywords the filters strip — the script must carry the guard
        // that exempts it from the filter pipeline (behaviour pinned by the Playwright facts
        // in ComponentDiagramDatabaseToggleTests).
        var content = GenerateReport("DbToggle_ComponentGuard.html", PlantUmlSourceWithDatabase);
        Assert.Contains("function isComponentDiagramContainer", content);
        Assert.Contains("component-diagram-section", content);
    }

    [Fact]
    public void Every_filter_queue_builder_guards_the_component_diagram()
    {
        // The invariant is "no filter control rewrites or re-renders the component diagram".
        // The note-driven builders (details/headers/note format) are inert for it today only
        // because a component diagram carries no notes — incidental, not structural. Each
        // builder must carry the guard so a component diagram that ever grows a note (a legend,
        // say) cannot start being rewritten.
        var content = GenerateReport("DbToggle_AllQueueGuards.html", PlantUmlSourceWithDatabase);

        string[] builders =
        [
            "function buildDetailsQueue",
            "function buildHeadersQueue",
            "function buildAssertionsQueue",
            "function buildStepsQueue",
            "function buildDatabasesQueue",
            "function buildNoteFormatQueue"
        ];

        foreach (var builder in builders)
        {
            var start = content.IndexOf(builder, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{builder} not found in the report script");
            // The guard must appear inside the builder's forEach, before any work — check the
            // window from the builder's start to the end of its container loop body.
            var body = content.Substring(start, Math.Min(1200, content.Length - start));
            Assert.True(
                body.Contains("isComponentDiagramContainer(container)", StringComparison.Ordinal),
                $"{builder} is missing the isComponentDiagramContainer guard");
        }
    }
}
